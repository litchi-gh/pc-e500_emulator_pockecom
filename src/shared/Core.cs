// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PokeCom;

/// <summary>
/// ネイティブのエミュレーションコア（e500core.dll）の公開 API の宣言。
/// 関数の仕様は src/native/machine.cpp の各 e500_* 関数のコメントを参照。
/// </summary>
internal static class Native
{
    // "e500core.dll" on Windows, "libe500core.so" on Linux, "libe500core.dylib" on macOS.
    const string Dll = "e500core";
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr e500_create(byte[] rom, int size);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr e500_create_config(byte[] rom,int size,int ram,int card);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_capacity(IntPtr m,int card);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_reset(IntPtr m,int clearInternal);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_memory(IntPtr m,int card,byte[] data,int size,int write);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_audio(IntPtr m,[Out]short[] data,int count);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Auto)] internal static extern int e500_disk_mount(IntPtr m,string? path,int readOnly);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_disk_status(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_disk_trace(IntPtr m,[Out]byte[] output,int length);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_printer_connect(IntPtr m,int on);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_can_save(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_can_load(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_printer_status(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_printer_read(IntPtr m,[Out]byte[] output,int length);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_printer_columns();
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_destroy(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_run(IntPtr m, int cycles);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_key(IntPtr m, int row, int bit, int down);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_release_keys(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_pixels(IntPtr m, [Out] byte[] pixels);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_pixels_duty(IntPtr m, [Out] byte[] duty);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern uint e500_reg(IntPtr m, int id);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_status(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_symbols(IntPtr m, int index);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_peek(IntPtr m,int address);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_dump(IntPtr m,int address,[Out] byte[] data,int length);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_write(IntPtr m,int address,byte[] data,int length);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_imem(IntPtr m,[Out] byte[] data);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern ulong e500_cycles(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_state_size();
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_state_card(byte[] data,int size);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_state_same(byte[] a,byte[] b,int size);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_save(IntPtr m, [Out] byte[] data, int size);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_load(IntPtr m, byte[] data, int size);
}

/// <summary>状態保存ファイル（*.e500state、JSON）の中身。</summary>
/// <param name="Version">ファイル形式の版（1〜3。3 はスナップショットが E500ST04 の形式）</param>
/// <param name="RomSha256">保存したときの ROM の SHA-256（別の ROM では読み込まない）</param>
/// <param name="DataSha256">Data の SHA-256（壊れていないかの確認）</param>
/// <param name="Data">ネイティブのスナップショット（e500_save の出力）</param>
internal sealed record SavedState(int Version, string RomSha256, string DataSha256, byte[] Data);
/// <summary>
/// エミュレーションコア1台分。ROM と RAM の構成を受け取ってネイティブの本体を作り、
/// 実行・キー入力・画面・状態保存・RAM の保存・周辺機器を扱う。使い終わったら Dispose する。
/// </summary>
internal sealed class Core : IDisposable
{
    readonly LcdTextDecoder textDecoder;
    /// <summary>ネイティブの本体（Machine）へのハンドル。</summary>
    internal IntPtr Handle {get; private set;}
    /// <summary>ROM ファイルの絶対パス。</summary>
    internal string RomPath {get;}
    /// <summary>ROM の SHA-256（16進）。Hash も同じ値。状態保存ファイルと自動保存の名前に使う。</summary>
    internal string RomHash {get;}
    internal string Hash {get;}
    // The RAM card is an image file: its size is the card's capacity and its
    // bytes are the card's contents, written back whenever the internal RAM is.
    /// <summary>RAM カードのイメージファイルのパス（カードなしは null）。</summary>
    internal string? CardPath {get;}
    internal string? CardName=>CardPath is null?null:Path.GetFileName(CardPath);
    internal bool HasCard=>CardPath is not null;
    /// <summary>ROM の容量（KB）。256 は PC-E500・E550・1480U・1490U・1490UII、384 は PC-E500S・PC-E650・PC-U6000。</summary>
    internal int RomKB {get;}
    /// <summary>ROM の機種コード（版の下の桁）。PC-E500S は 3、PC-U6000 は 4、PC-E650 は 5。</summary>
    internal int RomModel {get;}
    /// <summary>384KB の ROM のうち PC-E500S のもの（機種コード 3）。</summary>
    internal bool IsE500S=>RomKB==384 && RomModel==3;
    /// <summary>標準の内蔵 RAM（KB）。62256 が 1 個の PC-E500・E500S は 32、2 個の PC-E650・U6000 は 64。</summary>
    internal int StandardRamKB=>StandardRam(RomKB,RomModel);
    internal static int StandardRam(int romKB,int model)=>romKB==384 && model!=3?64:32;
    /// <summary>ROM の版（末尾16バイトの先頭2バイト）。読めなければ「不明」。</summary>
    internal string RomVersion {get;}
    internal bool IsE650Family=>RomKB==384;
    /// <summary>LCD の 240×32 ドット（Refresh で更新、1 なら点灯）。</summary>
    internal byte[] Pixels {get;} = new byte[240*32];
    /// <summary>LCD の 240×32 ドットの、前の Refresh から点いていた時間の割合（0〜255）。点滅で出す灰色もこの濃さになる。</summary>
    internal byte[] Duty {get;} = new byte[240*32];
    /// <summary>ROM の文字フォント（20h から、1文字6バイト）。</summary>
    internal byte[] Font=>textDecoder.Font;
    // Where Run hands what the CE-126P printed; without one it stays in the native buffer.
    /// <summary>CE-126P の印字を受け取る紙。null なら印字はネイティブ側にたまったまま。</summary>
    internal PrinterPaper? Paper {get;set;}
    readonly byte[] printed=new byte[4096];
    /// <summary>内蔵 RAM と RAM カードの容量（KB）。</summary>
    internal int InternalKB=>Native.e500_capacity(Handle,0);
    internal int CardKB=>Native.e500_capacity(Handle,1);
    /// <summary>ROM の大きさが違うときのメッセージ。</summary>
    static string WrongRomSize=>L.T("PC-E500・PC-E550・PC-1480U・PC-1490U・PC-1490UIIの256KB ROM、またはPC-E500S/PC-E650/PC-U6000の384KB ROMを選択してください。SAVEMヘッダー付きの吸い出しファイルはそのまま開けます。","Choose a 256KB ROM of a PC-E500, PC-E550, PC-1480U, PC-1490U or PC-1490UII, or a 384KB PC-E500S/PC-E650/PC-U6000 ROM. A dump with its SAVEM header opens as it is.");
    /// <summary>ROM を読み込み、ネイティブの本体を作る。</summary>
    /// <param name="path">ROM ファイル（256KB・384KB、または SAVEM ヘッダー付きの吸い出し）</param>
    /// <param name="ram">内蔵 RAM の容量（KB）。機種に合わなければ標準の容量にする</param>
    /// <param name="cardPath">RAM カードのイメージファイル（なしは null）</param>
    /// <exception cref="InvalidDataException">ROM やカードの大きさ・形式が正しくない</exception>
    internal Core(string path,int ram=32,string? cardPath=null)
    {
        // ROM を読み、SAVEM ヘッダー付きなら本体 ROM（C0000-FFFFF）だけを取り出す。
        // A file larger than any ROM is refused before it is read: picked by mistake it may be gigabytes.
        // どの ROM より大きいファイルは読む前に断る（間違えて選んだ大きなファイルを読み込まないため）。
        if(new FileInfo(path).Length>384*1024+BinaryTransfer.HeaderLength)throw new InvalidDataException(WrongRomSize);
        var rom=File.ReadAllBytes(path);
        // A dump straight from the machine (SAVEM "d:S3A.ROM",&HC0000,&HFFFFF)
        // carries the 16-byte SAVEM header; take the main ROM out of it as it is.
        if(BinaryTransfer.TryReadHeader(rom,out int dumpStart,out int dumpLength)) {
            if(dumpStart==0xc0000 && dumpLength==256*1024)rom=rom.AsSpan(BinaryTransfer.HeaderLength).ToArray();
            else if(dumpStart==0x20000)throw new InvalidDataException(L.T($"これは拡張ROM（S3B、{dumpStart:X5}-{dumpStart+dumpLength-1:X5}）のダンプです。単独では起動できないので、tools/merge_savem_rom.py で本体ROM（S3A）と結合した384KBイメージを開いてください。",$"This is a dump of the extension ROM (S3B, {dumpStart:X5}-{dumpStart+dumpLength-1:X5}). It cannot boot alone: merge it with the main ROM (S3A) into a 384KB image with tools/merge_savem_rom.py."));
            else throw new InvalidDataException(L.T($"SAVEMダンプの範囲 {dumpStart:X5}-{dumpStart+dumpLength-1:X5} は内蔵ROM（C0000-FFFFF）ではありません。SAVEM \"d:S3A.ROM\",&HC0000,&HFFFFF で吸い出してください。",$"The SAVEM dump covers {dumpStart:X5}-{dumpStart+dumpLength-1:X5}, not the internal ROM (C0000-FFFFF). Dump it with SAVEM \"d:S3A.ROM\",&HC0000,&HFFFFF."));
        }
        // 大きさと、ファイル名から分かる機種の食い違いを確かめる。
        if(rom.Length!=256*1024 && rom.Length!=384*1024)throw new InvalidDataException(WrongRomSize);
        string fileName=Path.GetFileNameWithoutExtension(path);
        if(rom.Length==256*1024 && (fileName.Contains("E650",StringComparison.OrdinalIgnoreCase) || fileName.Contains("U6000",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(L.T("PC-E650/PC-U6000には追加128KBを先頭へ結合した384KB ROMが必要です。tools/merge_savem_rom.pyでfullイメージを作成してください。","PC-E650/PC-U6000 needs a 384KB ROM with the 128KB extension in front. Build the full image with tools/merge_savem_rom.py."));
        // RAM カードのイメージを読み、容量を確かめる。
        byte[]? card=null;
        if(!string.IsNullOrWhiteSpace(cardPath)) {
            CardPath=Path.GetFullPath(cardPath);
            if(!File.Exists(CardPath))throw new FileNotFoundException(L.T("RAMカードのイメージファイルが見つかりません。\n","The RAM card image file was not found.\n")+CardPath);
            // Its size is checked before it is read, as the ROM's is.
            long bytes=new FileInfo(CardPath).Length;
            if(bytes%1024!=0 || bytes>256*1024 || !RamSizes.Sizes.Contains((int)(bytes/1024)))
                throw new InvalidDataException(L.T($"RAMカードのイメージは8/16/32/64/128/256KBのいずれかにしてください。\n{CardPath} ({bytes}バイト)",$"A RAM card image must be 8, 16, 32, 64, 128 or 256KB.\n{CardPath} ({bytes} bytes)"));
            card=File.ReadAllBytes(CardPath);
            if(card.Length!=bytes)throw new InvalidDataException(L.T("RAMカードのイメージが読み込みの途中で変わりました。\n","The RAM card image changed while it was being read.\n")+CardPath);
        }
        RomKB=rom.Length/1024;
        // The service diagnostic prints these two bytes as the major/minor ROM
        // version. They are the first two bytes of the 16-byte ROM trailer in
        // every known E500-family image (2.2 through 8.4) and in the supplied
        // E650 8.5 image. Read the image itself so renamed files stay accurate.
        byte versionMajor=rom[^16],versionMinor=rom[^15];
        RomVersion=versionMajor<=99 && versionMinor<=99?$"{versionMajor}.{versionMinor}":L.T("不明","unknown");
        RomModel=versionMinor;
        RomPath=Path.GetFullPath(path);RomHash=Hash=Convert.ToHexString(SHA256.HashData(rom));
        // A stored 32KB setting from a PC-E500 session is not a size the E650/U6000
        // hardware can be built with, so fall back to that machine's stock capacity.
        Handle=Native.e500_create_config(rom,rom.Length,RamSizes.CoerceInternal(StandardRamKB,ram),card is null?0:card.Length/1024);
        if(Handle==IntPtr.Zero) throw new InvalidOperationException(L.T("エミュレータを初期化できません。","Could not initialise the emulator."));
        if(card is not null)Native.e500_memory(Handle,1,card,card.Length,1);
        // The E650/U6000 character generator table resides in the CE3 extension.
        textDecoder=new LcdTextDecoder(rom);
    }
    // Every run hands what was printed to Paper, so no caller can leave lines in a
    // core it then disposes. With nothing printed that is one call returning 0.
    /// <summary>CPU を指定のサイクル数だけ実行し、印字された行を紙へ渡す。</summary>
    /// <param name="cycles">実行するサイクル数（1,024,000 で約1秒）</param>
    /// <exception cref="InvalidOperationException">未定義の命令で止まった</exception>
    internal void Run(int cycles)
    {
        int result=Native.e500_run(Handle,cycles);
        DrainPrinter();
        if(result!=0)
            throw new InvalidOperationException(L.T($"未定義命令で停止しました。PC={Native.e500_reg(Handle,9):X5}",$"Stopped on an undefined instruction. PC={Native.e500_reg(Handle,9):X5}"));
    }
    /// <summary>ネイティブ側にたまった印字を、すべて紙（Paper）へ移す。</summary>
    void DrainPrinter()
    {
        if(Paper is null)return;
        int n;while((n=Native.e500_printer_read(Handle,printed,printed.Length))>0)Paper.Feed(printed.AsSpan(0,n));
    }
    /// <summary>LCD のドットを Pixels に、前の Refresh から点いていた時間の割合を Duty に読み直す。</summary>
    internal void Refresh() {Native.e500_pixels(Handle,Pixels);Native.e500_pixels_duty(Handle,Duty);}
    /// <summary>メモリを読む（LCD の I/O に触れない、デバッガ用）。</summary>
    /// <param name="address">先頭アドレス（00000〜FFFFF）</param>
    /// <param name="length">バイト数</param>
    internal byte[] Dump(int address,int length) {
        var data=new byte[length];
        if(Native.e500_dump(Handle,address,data,length)==0)throw new ArgumentOutOfRangeException(nameof(address),L.T("アドレスが範囲外です。","Address out of range."));
        return data;
    }
    // Debugger writes: RAM only, so a poke can never patch ROM or fall into
    // unmapped space by accident, and all or nothing.
    /// <summary>メモリに書く（デバッガ用）。範囲がすべて RAM のときだけ書く。</summary>
    /// <param name="address">先頭アドレス</param>
    /// <param name="data">書くバイト列</param>
    /// <exception cref="InvalidOperationException">RAM 以外を含む</exception>
    internal void Write(int address,byte[] data) {
        if(data.Length==0)return;
        if(Native.e500_write(Handle,address,data,data.Length)==0)
            throw new InvalidOperationException(L.T($"{address:X5}-{(long)address+data.Length-1:X5} はRAM以外の領域を含むか、RAMの大きさを超えて折り返すため書き込めません。内蔵RAM・RAMカードの範囲に収まるように指定してください。",$"{address:X5}-{(long)address+data.Length-1:X5} includes non-RAM space, or is longer than the RAM and would wrap round it, and cannot be written. Stay within internal RAM or the RAM card."));
    }
    // The ROM counts down to its auto power off: at every slow-timer tick (about
    // 0.5 s) it takes one from the word at +06h of the IOCS work area, whose
    // address is in internal RAM E6h-E8h, and switches off at 0; a key puts the
    // word back to the start value kept at BFCBCh (04B0h, about 10 minutes, after
    // a reset). Putting it back here holds the power on as a key would, the way the
    // ESP32 build does (pocket.cpp, hold_power). Nothing is written unless the
    // count is in RAM and not above the start value, so a ROM laid out otherwise
    // is left alone.
    // 自動電源 OFF の残り時間（IOCS の作業域 +06h。番地は内部 RAM の E6h〜E8h）を、初めの値（BFCBCh）に戻す
    //（キーを押したのと同じ。ESP32 版の hold_power と同じ）。RAM でない・初めの値より大きいときは書かない。
    /// <summary>自動電源 OFF の残り時間を初めの値に戻す（戻したら true）。</summary>
    internal bool HoldPower() {
        var imem=InternalMemory();
        int at=((imem[0xe6]|imem[0xe7]<<8|imem[0xe8]<<16)&0xfffff)+6;
        if(at+1>0xfffff)return false;
        var start=Dump(0xbfcbc,2);var left=Dump(at,2);
        int init=start[0]|start[1]<<8,count=left[0]|left[1]<<8;
        if(init<2 || count>init || count==init)return false;
        return Native.e500_write(Handle,at,start,2)!=0;
    }
    /// <summary>CPU の内部メモリ（256バイト）を読む。</summary>
    internal byte[] InternalMemory() {var data=new byte[256];Native.e500_imem(Handle,data);return data;}
    // The core has no call that writes internal RAM, so the bytes go through a
    // snapshot: taken, patched where the 256 bytes of internal RAM are found in
    // it (found, not assumed, so the layout stays the native code's own), and
    // restored. Nothing else in the snapshot changes, so the machine goes on as it was.
    // ネイティブに内部 RAM へ書く関数がないので、状態を取り出し、その中の内部 RAM（中身で探す）を直して読み戻す。
    /// <summary>CPU の内部メモリの at から data を書く（状態の取り出しと読み戻しで行う）。</summary>
    /// <exception cref="InvalidOperationException">今は状態を取り出せない、または内部メモリが状態の中に見つからない</exception>
    internal void WriteInternalMemory(int at,byte[] data) {
        if(at<0 || at+data.Length>256)throw new ArgumentOutOfRangeException(nameof(at));
        byte[] state=Snapshot(),imem=InternalMemory();
        int found=state.AsSpan().IndexOf(imem);
        if(found<0 || state.AsSpan(found+1).IndexOf(imem)>=0)throw new InvalidOperationException("Internal RAM not found in the snapshot");
        data.CopyTo(state,found+at);
        if(Native.e500_load(Handle,state,state.Length)==0)throw new InvalidOperationException(CannotSave);
    }
    /// <summary>起動からの CPU のサイクル数。</summary>
    internal ulong Cycles=>Native.e500_cycles(Handle);
    /// <summary>CPU のレジスタの値（id はネイティブの REGNAME）。</summary>
    internal uint Register(int id)=>Native.e500_reg(Handle,id);
    /// <summary>LCD の表示を、ROM のフォントと照らして文字列（40桁×4行）に読み取る。</summary>
    internal string ScreenText() {Refresh();return textDecoder.Decode(Pixels);}
    /// <summary>キーを押す・離す。</summary>
    /// <param name="code">キーコード（行×8＋ビット、Keyboard.Codes の値）</param>
    /// <param name="down">押すなら true</param>
    internal void Key(int code,bool down) => Native.e500_key(Handle,code/8,code%8,down?1:0);
    /// <summary>すべてのキーを離す。</summary>
    internal void Release() => Native.e500_release_keys(Handle);
    // Raised with each native layout, so that a build that cannot read a newer
    // file says so: 3 came with E500ST04. Older ones load still.
    /// <summary>保存するファイル形式の版（ネイティブの形式が変わるたびに上げる。3 は E500ST04）。</summary>
    const int CurrentVersion=4;
    /// <summary>状態を保存する（一時ファイルに書いてから置き換える）。</summary>
    /// <param name="path">保存先（*.e500state）</param>
    /// <exception cref="InvalidOperationException">今は保存できない状態（CannotSave を参照）</exception>
    internal void Save(string path)=>WriteState(path,Hash,Snapshot());
    // Taking the snapshot is a quarter of a millisecond; turning it into the file
    // (hash, JSON, disk) is the rest, and can be done off the UI thread.
    /// <summary>今の状態を取り出す（ファイルには書かない）。</summary>
    /// <exception cref="InvalidOperationException">今は保存できない状態（CannotSave を参照）</exception>
    internal byte[] Snapshot() {
        byte[] data=new byte[Native.e500_state_size()];
        if(Native.e500_save(Handle,data,data.Length)==0) throw new InvalidOperationException(CannotSave);
        return data;
    }
    /// <summary>取り出した状態を状態ファイルに書く（一時ファイルに書いてから置き換える。どのスレッドからでもよい）。</summary>
    /// <param name="path">保存先（*.e500state）</param>
    /// <param name="romHash">ROM の SHA-256（Hash）</param>
    /// <param name="data">Snapshot で取り出した状態</param>
    /// <returns>書いたファイルの更新時刻（UTC）</returns>
    internal static DateTime WriteState(string path,string romHash,byte[] data) {
        var json=JsonSerializer.SerializeToUtf8Bytes(new SavedState(CurrentVersion,romHash,Convert.ToHexString(SHA256.HashData(data)),data));
        return AtomicFile.Write(path,json);
    }
    /// <summary>状態を読み込む。RAM カードはファイルの中身のまま使い、スナップショットからは戻さない。</summary>
    /// <param name="path">状態保存ファイル</param>
    /// <exception cref="InvalidDataException">ファイルが不正、ROM が違う、カードの容量が違う、または知らない形式</exception>
    internal void Load(string path)
    {
        if(new FileInfo(path).Length>2000000) throw new InvalidDataException(L.T("保存ファイルのサイズが不正です。","The state file has an invalid size."));
        // ファイルの形式・ROM・中身のハッシュを確かめる。
        var state=JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path));
        if(state is not null && state.Version>CurrentVersion)
            throw new InvalidDataException(L.T("保存ファイルは新しいバージョンのエミュレータで作られています。","The state file was made by a newer version of the emulator."));
        if(state is null || state.Version<1 || state.RomSha256!=Hash || state.Data is null ||
           state.DataSha256!=Convert.ToHexString(SHA256.HashData(state.Data)))
            throw new InvalidDataException(L.T("保存ファイルが不正、またはROMが異なります。","The state file is invalid or belongs to another ROM."));
        // The snapshot carries the card slot too, but the card is a file of its
        // own: a snapshot must not swap in another card's bytes, and it cannot be
        // resumed with a card of another capacity in the slot. The native core reads
        // the capacity, so the layouts are known in one place; a layout it does not
        // know is no card question but an invalid file.
        // カードの容量はネイティブ側が読む（形式の知識はネイティブ側だけに置く）。
        int snapshotCard=Native.e500_state_card(state.Data,state.Data.Length);
        if(snapshotCard<0)throw new InvalidDataException(L.T("保存ファイルが不正、または対応していない形式です。","The state file is invalid or in a format this version does not support."));
        if(snapshotCard!=CardKB)
            throw new InvalidDataException(L.T($"保存時のRAMカード({(snapshotCard==0?"未装着":snapshotCard+"KB")})と現在のRAMカード({(CardKB==0?"未装着":CardKB+"KB")})が異なります。同じ容量のカードを装着してから読み込んでください。",$"The RAM card at save time ({(snapshotCard==0?"none":snapshotCard+"KB")}) differs from the current one ({(CardKB==0?"none":CardKB+"KB")}). Attach a card of the same capacity before loading."));
        // カードの中身は読み込みの前に取っておき、読み込んだあと書き戻す。
        byte[]? card=CardKB>0?CardImage():null;
        if(Native.e500_load(Handle,state.Data,state.Data.Length)==0)
            throw new InvalidDataException(L.T("保存ファイルが不正、またはROMが異なります。","The state file is invalid or belongs to another ROM."));
        if(card is not null)Native.e500_memory(Handle,1,card,card.Length,1);
        Refresh();
    }
    /// <summary>RAM カードの今の中身（カードなしは空）。</summary>
    internal byte[] CardImage() {
        var data=new byte[CardKB*1024];
        if(data.Length>0)Native.e500_memory(Handle,1,data,data.Length,0);
        return data;
    }
    // Writes the card's contents back into its image file.
    /// <summary>RAM カードの中身をイメージファイルへ書き戻す。</summary>
    internal void SaveCard() {if(CardItem() is PersistentWriter.Item card)AtomicFile.Write(card.Path,card.Data);}
    /// <summary>RAM カードのファイルとその中身（カードがなければ null）。</summary>
    internal PersistentWriter.Item? CardItem()=>CardPath is null || CardKB==0?null:new(CardPath,CardImage());
    /// <summary>CE-140F にフォルダを接続する。</summary>
    /// <param name="path">フォルダ（null なら取り外す）</param>
    /// <param name="readOnly">書き込み禁止にするか</param>
    /// <exception cref="IOException">フォルダを開けない</exception>
    internal void MountDisk(string? path,bool readOnly) {
        if(Native.e500_disk_mount(Handle,path,readOnly?1:0)==0)throw new IOException(L.T("CE-140Fの保存フォルダを開けません。","Could not open the CE-140F folder."));
    }
    /// <summary>CE-126P が接続されているか（設定するとネイティブ側の接続を切り替える）。</summary>
    internal bool PrinterConnected {
        get=>(Native.e500_printer_status(Handle)&1)!=0;
        set=>Native.e500_printer_connect(Handle,value?1:0);
    }
    // The native rule, the same one Save and Load meet: no save with the CE-140F
    // mid-transfer or a file open, or after a CPU fault; no load over a CE-140F
    // transfer or open file. The CE-126P goes into the snapshot, mid-line or not.
    /// <summary>今、状態保存できるか（ネイティブの e500_can_save と同じ規則）。</summary>
    internal bool CanSnapshot=>Native.e500_can_save(Handle)!=0;
    /// <summary>今、状態を読み込めるか（ネイティブの e500_can_load と同じ規則）。</summary>
    internal bool CanLoad=>Native.e500_can_load(Handle)!=0;
    /// <summary>CPU が未定義の命令で止まっているか（状態保存はできないが、リセットで立ち直れる）。</summary>
    internal bool Faulted=>(Native.e500_status(Handle)&4)!=0;
    /// <summary>状態を読み込めないときに示すメッセージ。</summary>
    internal static string CannotLoad=>L.T(DiskJa+"は状態を読み込めません。CLOSEの後に読み込んでください。",$"A state cannot be loaded {DiskEn}. Load after CLOSE.");
    // The CE-140F's part, the same in every message about it.
    /// <summary>状態を保存・読み込みできない理由のうち、CE-140F の分（各メッセージで共通）。</summary>
    const string DiskJa="CE-140Fの通信中・ファイルOPEN中",DiskEn="during a CE-140F transfer or with a file OPEN";
    /// <summary>CPU のエラー中でないのに状態保存できないときに示すメッセージ（理由は CE-140F だけ）。</summary>
    internal static string DiskBusy=>L.T(DiskJa+"は状態保存できません。CLOSEの後にやり直してください。",$"The state cannot be saved {DiskEn}. Try again after CLOSE.");
    /// <summary>状態保存できないときに示すメッセージ。</summary>
    internal static string CannotSave=>L.T(DiskJa+"、またはCPUのエラー中は状態保存できません。CLOSEの後に保存してください。",$"The state cannot be saved {DiskEn}, or after a CPU fault. Save after CLOSE.");
    /// <summary>CE-140F のトレース（コマンドとエラーの記録）。</summary>
    internal string DiskTrace() {byte[] b=new byte[16385];int n=Native.e500_disk_trace(Handle,b,b.Length);return System.Text.Encoding.UTF8.GetString(b,0,n);}
    // The internal RAM file for the current capacity: one per capacity, so
    // switching back to a size restores what it held.
    /// <summary>今の容量の内蔵 RAM を保存するファイルのパス（容量ごとに別のファイル）。</summary>
    /// <param name="directory">保存先のフォルダ</param>
    internal string InternalFile(string directory)=>Path.Combine(directory,$"internal-{InternalKB}KB.ram");
    /// <summary>内蔵 RAM をファイルから読む、またはファイルへ書く。</summary>
    /// <param name="directory">保存先のフォルダ</param>
    /// <param name="load">true なら読む（ファイルがなければ何もしない）、false なら書く</param>
    internal void MemoryFiles(string directory,bool load) {
        Directory.CreateDirectory(directory);
        string path=InternalFile(directory);
        if(load) {
            if(!File.Exists(path))return;var data=File.ReadAllBytes(path);
            if(data.Length!=InternalKB*1024 || Native.e500_memory(Handle,0,data,data.Length,1)==0)throw new InvalidDataException(L.T("RAM保存ファイルのサイズが不正です: ","The RAM file has an invalid size: ")+path);
        }else {var item=InternalItem(directory);AtomicFile.Write(item.Path,item.Data);}
    }
    /// <summary>今の容量の内蔵 RAM のファイルとその中身。</summary>
    /// <param name="directory">保存先のフォルダ</param>
    internal PersistentWriter.Item InternalItem(string directory)=>new(InternalFile(directory),InternalImage());
    /// <summary>内蔵 RAM の今の中身。</summary>
    internal byte[] InternalImage() {
        var data=new byte[InternalKB*1024];Native.e500_memory(Handle,0,data,data.Length,0);
        return data;
    }
    // A fresh card is all 0xFF, which is what the ROM sees in a card that was
    // never initialised, so the first PF1 formats it.
    /// <summary>新しい RAM カードのイメージ（すべて FFh）を作る。</summary>
    /// <param name="path">作るファイル</param>
    /// <param name="kb">容量（8〜256KB）</param>
    internal static void CreateCardImage(string path,int kb) {
        if(!RamSizes.Sizes.Contains(kb))throw new ArgumentOutOfRangeException(nameof(kb));
        var data=new byte[kb*1024];Array.Fill(data,(byte)0xff);
        AtomicFile.Write(path,data);
    }
    /// <summary>ネイティブの本体を破棄する。</summary>
    public void Dispose() {if(Handle!=IntPtr.Zero) {Native.e500_destroy(Handle);Handle=IntPtr.Zero;}}
}

// The autosave every five seconds wrote a megabyte on the UI thread - 7 ms, and
// 17 ms in a OneDrive folder - so the machine and its sound stalled for a frame
// each time; and a file held by a sync client threw out of the tick, which
// paused the machine with a message box. The machine's contents are now taken
// on the UI thread, a copy that costs well under a millisecond, and written on
// another; a failure is kept for the status line and the next round tries again.
// Writes never overlap: a save that has to land now (closing, switching the ROM)
// waits for the one in the background first. A RAM or card file whose bytes
// are what was last written there is left alone, so an idle machine does not
// rewrite its card for the sync client every five seconds.
// 自動保存：中身の取り出しは UI スレッドで、ファイルへの書き込みは別のスレッドで行う。書き込みは重ならない。
// 失敗はステータスバーに出し、次の回にもう一度試す。前に書いた中身と同じ RAM・カードのファイルは書き直さない。
/// <summary>内蔵 RAM・RAM カード・状態のファイルを書く（裏のスレッドでも、その場でも）。</summary>
internal sealed class PersistentWriter {
    /// <summary>書くもの：ファイル、中身、状態ファイルなら ROM の SHA-256（RAM・カードなら null）。</summary>
    internal readonly record struct Item(string Path,byte[] Data,string? RomHash=null);
    Task pending=Task.CompletedTask;
    // What was last written to each RAM or card file, and the file's time after it.
    readonly Dictionary<string,(byte[] Data,DateTime Time)> written=new(StringComparer.OrdinalIgnoreCase);
    volatile Exception? failure;
    /// <summary>裏で書いている途中か。</summary>
    internal bool Busy=>!pending.IsCompleted;
    /// <summary>裏で書き始める（前の書き込みが終わっていなければ何もしない）。</summary>
    /// <returns>書き始めたか</returns>
    internal bool Start(IReadOnlyList<Item> items) {
        if(Busy)return false;
        pending=Task.Run(()=>{
            try {Write(items);failure=null;}
            catch(Exception ex) {failure=ex;}
        });
        return true;
    }
    /// <summary>裏の書き込みが終わるのを待ってから、その場で書く（失敗は例外）。</summary>
    internal void WriteNow(IReadOnlyList<Item> items) {Wait();Write(items);failure=null;}
    /// <summary>裏の書き込みが終わるのを待つ。</summary>
    internal void Wait()=>pending.Wait();
    /// <summary>最後の書き込みが失敗していれば、その理由（次に成功するまで残る。成功していれば null）。</summary>
    internal Exception? Failure=>failure;
    void Write(IReadOnlyList<Item> items) {
        foreach(var item in items) {
            // Skipped only while the file is as this left it: anything else writing
            // there (a card made again in the settings) changes its time. A state
            // counts as the same when only the time it has run differs: a machine
            // left off rewrote 0.75 MB every five seconds for the sync client.
            // 前に書いたままのファイルなら書き直さない。状態は、経過時間だけが違えば同じとみなす
            // （電源を切って置いておくと、5秒ごとに 0.75MB を書き直していた）。
            lock(written) {
                if(written.TryGetValue(item.Path,out var last) && File.Exists(item.Path) && File.GetLastWriteTimeUtc(item.Path)==last.Time &&
                   (item.RomHash is null?last.Data.AsSpan().SequenceEqual(item.Data):last.Data.Length==item.Data.Length && Native.e500_state_same(last.Data,item.Data,item.Data.Length)!=0))continue;
            }
            var time=item.RomHash is null?AtomicFile.Write(item.Path,item.Data):Core.WriteState(item.Path,item.RomHash,item.Data);
            lock(written)written[item.Path]=(item.Data,time);
        }
    }
}

/// <summary>ファイルを一時ファイル経由で置き換える（書き込みの途中で失敗しても元のファイルは壊れない）。</summary>
internal static class AtomicFile {
    // The time is the temporary file's, taken before it is moved into place (a
    // move keeps it): read afterwards, a write by someone else in between would
    // pass for this one.
    /// <summary>bytes を path に書く。フォルダがなければ作る。</summary>
    /// <param name="path">書き込み先</param>
    /// <param name="bytes">中身</param>
    /// <returns>書いたファイルの更新時刻（UTC）</returns>
    internal static DateTime Write(string path,byte[] bytes) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllBytes(temp,bytes);var time=File.GetLastWriteTimeUtc(temp);File.Move(temp,path,true);return time;}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
