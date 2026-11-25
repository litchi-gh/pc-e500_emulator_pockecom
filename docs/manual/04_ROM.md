# 4. ROM を用意する

ROM は含まれていません。お手持ちの実機から吸い出したものを、データの `rom` フォルダに置きます。名前は `PC-E500(7.2).ROM` のように「機種名(版).ROM」にします。

| システム | 置く場所 |
|---|---|
| Windows（zip・ソースからのビルド） | `app\rom\`（`PC-E500.exe` の横の `rom`） |
| Windows（インストーラー） | `%LocalAppData%\PockeComE500\rom\` |
| Ubuntu（.deb） | `~/.local/share/PockeComE500/rom/` |
| Ubuntu（tar.gz） | 展開したフォルダの `rom/` |
| Mac | `~/Library/Application Support/PockeComE500/rom/`、または `pokecom` のあるフォルダの `rom/` |

## PC-E500S / PC-E650 / PC-U6000 の384KB ROM

PC-E500S / PC-E650 / PC-U6000の内蔵ROMは、提示されたメモリマップに合わせて次のように配置します。

| ROMファイル内 | CPUアドレス | 大きさ | チップ選択 |
|---|---:|---:|---|
| `00000-1FFFF` | `20000-3FFFF` | 128KB | CE3 |
| `20000-5FFFF` | `C0000-FFFFF` | 256KB | CE2 |

「ROMを開く」では、PC-E500の256KBイメージとPC-E500S/PC-E650/PC-U6000の384KBイメージを自動判別します。384KBのうち、本体ROMの機種コード（版の下の桁）が3のものをPC-E500Sとして扱います。ステータスバーの ROM の欄に判別した系列（E500・E500S・E650）を、マウスを乗せたときのヒントに ROM の容量とファイルを表示します。384KBファイルは、アドレスの低い追加ROMを先頭、その後ろに256KB本体ROMを置いた形式です。

画面下部のステータスバーには`ROM Ver.x.y`も表示します。この値はファイル名ではなく、サービスメンテ画面が使う本体ROM末尾16バイト内のメジャー／マイナーバージョンから取得します。動作確認に使ったPC-E500 ROMはVer.7.2、PC-E650 ROMはVer.8.5です。

## 実機からのROM吸い出しと `tools/merge_savem_rom.py`

実機の `SAVEM "d:ファイル名",開始アドレス,終了アドレス`（`d:` は接続した機器のデバイス名）でROMをファイルへ書き出します。

```basic
SAVEM "L:S3A.ROM",&HC0000,&HFFFFF
SAVEM "L:S3B.ROM",&H20000,&H3FFFF     ' PC-E500S / PC-E650 / PC-U6000 のみ必要
```

`SAVEM` の出力には16バイトのヘッダー（`FF 00 06 01 10` + 長さ3バイト + 開始アドレス3バイト + `FF FF FF 00 0F`）が付きます。PC-E500系の `S3A.ROM` はヘッダー付きのまま開けます。PC-E500S/E650/U6000は `S3A.ROM` と `S3B.ROM` を `tools/merge_savem_rom.py`（Python 3）で384KBに結合します。

```powershell
python tools/merge_savem_rom.py S3A.ROM S3B.ROM --info                              # ヘッダーの確認
python tools/merge_savem_rom.py S3A.ROM S3B.ROM -o app/rom/PC-U6000-full.rom         # 384KBに結合
python tools/merge_savem_rom.py S3A.ROM -o "app/rom/PC-E500(7.2).ROM"                 # ヘッダー除去のみ (256KB)。名前は「機種名(版).ROM」
python tools/merge_savem_rom.py S3A.ROM S3B.ROM --strip                              # 各ファイルのヘッダーを取り除いて上書き
python tools/merge_savem_rom.py S3B.ROM --main-rom PC-U6000.rom --layout full -o app/rom/PC-U6000-full.rom   # 除去済み本体ROMと結合
```

カバー率が100%でない場合は吸い出しが欠けています。手順の詳細と確認方法は、[画像付きの取扱説明書](../../app/manual/index.html)（ツールバーの「使い方」）の13章にあります。

`tools/merge_savem_rom.py` は、Windows では `python`、Ubuntu と Mac では `python3` で動かします。

E650/U6000の標準内蔵RAMは64KBで、`B0000-BFFFF`へ配置されます。E500SはPC-E500と同じ32KBで、`B8000-BFFFF`へ配置されます。RAMカードは`40000`から容量に応じて配置されます。

---

[← 3. Mac で使う（コマンドライン）](03_MACOS.md) ｜ [目次](../README.md) ｜ [5. 最初の操作 →](05_FIRST_STEPS.md)
