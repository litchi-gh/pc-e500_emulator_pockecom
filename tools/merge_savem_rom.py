#!/usr/bin/env python3
"""実機から SAVEM で吸い出した S3 ダンプ (S3A.ROM / S3B.ROM ...) を結合する。

SAVEM ファイルの先頭には 16 バイトのヘッダが付く:

    off 0..3  : FF 00 06 01  種別 (マシン語)
    off 4     : 10           (常に 0x10)
    off 5..7  : データ長   24bit little endian
    off 8..10 : 開始アドレス 24bit little endian
    off 11..13: FF FF FF
    off 14..15: 00 0F

ヘッダを取り除き、各ペイロードを開始アドレスに従って ROM イメージへ配置する。

レイアウト:
    main : 0xC0000-0xFFFFF の 256KB (PC-E500 標準。本エミュレータが読める形式)
    full : 0x20000-0x3FFFF (128KB) + 0xC0000-0xFFFFF (256KB) の 384KB
           (PC-E650 / PC-U6000 用。拡張 ROM を先頭に置く)
"""
import argparse, hashlib, sys

HEADER_LEN = 16
LAYOUTS = {
    # PC-E500 / E550 / 1490UII: 内蔵ROM 256KB のみ
    "main": [(0xC0000, 0x40000)],
    # PC-E650 / U6000: 内蔵4M-ROM CE3 (128KB) + CE2 (256KB) = 384KB
    "full": [(0x20000, 0x20000), (0xC0000, 0x40000)],
}


def parse_savem(path):
    data = open(path, "rb").read()
    if len(data) < HEADER_LEN:
        raise ValueError(f"{path}: ファイルが小さすぎます ({len(data)} バイト)")
    h = data[:HEADER_LEN]
    payload = data[HEADER_LEN:]
    length = int.from_bytes(h[5:8], "little")
    start = int.from_bytes(h[8:11], "little")
    warn = []
    if h[0] != 0xFF or h[4] != 0x10:
        warn.append(f"見慣れないヘッダ: {h.hex(' ')}")
    if length != len(payload):
        warn.append(f"ヘッダの長さ {length:#x} と実データ {len(payload):#x} が不一致")
    return {"path": path, "header": h, "start": start, "length": length,
            "payload": payload, "warn": warn}


def describe(f):
    p = f["payload"]
    ff = sum(1 for x in p if x == 0xFF)
    print(f"{f['path']}")
    print(f"  ヘッダ      : {f['header'].hex(' ')}")
    print(f"  開始アドレス: 0x{f['start']:05X}")
    print(f"  長さ        : 0x{f['length']:X} ({f['length']} バイト)")
    print(f"  範囲        : 0x{f['start']:05X}-0x{f['start'] + len(p) - 1:05X}")
    print(f"  0xFF の割合 : {100 * ff / max(len(p), 1):.1f}%")
    for w in f["warn"]:
        print(f"  警告        : {w}")


def gaps(covered, regions):
    """未取得アドレスを連続範囲にまとめて返す。"""
    out = []
    for base, size in regions:
        run = None
        for a in range(base, base + size + 1):
            filled = a < base + size and covered.get(a, False)
            if not filled and a < base + size:
                if run is None:
                    run = a
            elif run is not None:
                out.append((run, a - 1))
                run = None
    return out


def main():
    ap = argparse.ArgumentParser(description="SAVEM 形式の S3 ダンプを結合する")
    ap.add_argument("files", nargs="*", help="S3A.ROM S3B.ROM ... (16バイトSAVEMヘッダ付き)")
    ap.add_argument("-o", "--output", help="出力する ROM イメージ")
    ap.add_argument("--layout", choices=["auto", "main", "full"], default="auto",
                    help="auto: 0x20000-0x3FFFF を含むダンプがあれば full、なければ main")
    ap.add_argument("--base", help="穴埋めに使う既存 ROM イメージ (同じレイアウト)")
    ap.add_argument("--main-rom", help="ヘッダを除去済みの C0000-FFFFF 256KB ROM")
    ap.add_argument("--fill", type=lambda s: int(s, 0), default=0xFF, help="穴埋めバイト (既定 0xFF)")
    ap.add_argument("--info", action="store_true", help="ヘッダ解析のみ")
    ap.add_argument("--strip", action="store_true",
                    help="結合せず、各入力ファイルから 16 バイトの SAVEM ヘッダを取り除いて上書きする")
    args = ap.parse_args()

    if not args.files and not args.main_rom:
        ap.error("SAVEMファイルまたは --main-rom が必要です")

    files = [parse_savem(p) for p in args.files]
    for f in files:
        describe(f)
        print()
    if args.info:
        return 0

    if args.strip:
        for f in files:
            open(f["path"], "wb").write(f["payload"])
            print(f"ヘッダ除去  : {f['path']} -> {len(f['payload'])} バイト "
                  f"(0x{f['start']:05X}-0x{f['start'] + len(f['payload']) - 1:05X})")
        return 0

    layout = args.layout
    if layout == "auto":
        layout = "full" if any(f["start"] < 0x40000 for f in files) else "main"
        print(f"レイアウト自動判定: {layout}")
    regions = LAYOUTS[layout]
    total = sum(s for _, s in regions)

    def offset(addr):
        off = 0
        for base, size in regions:
            if base <= addr < base + size:
                return off + addr - base
            off += size
        return None

    image = bytearray([args.fill & 0xFF]) * total
    if args.base:
        b = open(args.base, "rb").read()
        if len(b) != total:
            print(f"エラー: --base のサイズ {len(b)} がレイアウト {layout} の {total} と違います", file=sys.stderr)
            return 1
        image = bytearray(b)

    covered, conflicts, outside = {}, [], []
    if args.main_rom:
        main_rom = open(args.main_rom, "rb").read()
        if len(main_rom) != 0x40000:
            print(f"エラー: --main-rom は 262144 バイト必要です ({len(main_rom)} バイト)", file=sys.stderr)
            return 1
        for i, byte in enumerate(main_rom):
            address = 0xC0000 + i
            image[offset(address)] = byte
            covered[address] = True
    for f in files:
        for i, byte in enumerate(f["payload"]):
            a = f["start"] + i
            off = offset(a)
            if off is None:
                outside.append(a)
                continue
            if covered.get(a) and image[off] != byte:
                conflicts.append(a)
            image[off] = byte
            covered[a] = True

    if outside:
        print(f"警告: レイアウト {layout} の範囲外のデータ {len(outside)} バイトを無視しました "
              f"(0x{outside[0]:05X}-0x{outside[-1]:05X})")
    if conflicts:
        print(f"警告: ファイル間で内容が食い違うアドレスが {len(conflicts)} 個あります "
              f"(先頭 0x{conflicts[0]:05X})")

    missing = gaps(covered, regions)
    filled = len(covered)
    print(f"レイアウト  : {layout} ({total} バイト)")
    print(f"カバー率    : {filled}/{total} ({100 * filled / total:.1f}%)")
    for lo, hi in missing:
        print(f"  未取得    : 0x{lo:05X}-0x{hi:05X} ({hi - lo + 1} バイト)")

    if args.output:
        open(args.output, "wb").write(bytes(image))
        print(f"出力        : {args.output} ({len(image)} バイト)")
        print(f"SHA-256     : {hashlib.sha256(image).hexdigest().upper()}")
        if missing and not args.base:
            print("注意: 未取得の範囲は穴埋めバイトのままです。このままでは実機の ROM として動作しません。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
