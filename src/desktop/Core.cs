// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PokeCom;

internal static class Native
{
    const string Dll = "e500core.dll";
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr e500_create(byte[] rom, int size);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr e500_create_config(byte[] rom,int size,int ram,int card);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_capacity(IntPtr m,int card);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_reset(IntPtr m,int clearInternal);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_memory(IntPtr m,int card,byte[] data,int size,int write);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_audio(IntPtr m,[Out]short[] data,int count);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] internal static extern int e500_disk_mount(IntPtr m,string? path,int readOnly);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_disk_status(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_disk_trace(IntPtr m,[Out]byte[] output,int length);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_destroy(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_run(IntPtr m, int cycles);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_key(IntPtr m, int row, int bit, int down);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_release_keys(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_pixels(IntPtr m, [Out] byte[] pixels);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern uint e500_reg(IntPtr m, int id);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_status(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_symbols(IntPtr m, int index);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_peek(IntPtr m,int address);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_dump(IntPtr m,int address,[Out] byte[] data,int length);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_write(IntPtr m,int address,byte[] data,int length);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_imem(IntPtr m,[Out] byte[] data);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern ulong e500_cycles(IntPtr m);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_state_size();
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_save(IntPtr m, [Out] byte[] data, int size);
    [DllImport(Dll, CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_load(IntPtr m, byte[] data, int size);
}

internal sealed record SavedState(int Version, string RomSha256, string DataSha256, byte[] Data);
internal sealed class Core : IDisposable
{
    readonly LcdTextDecoder textDecoder;
    internal IntPtr Handle {get; private set;}
    internal string RomPath {get;}
    internal string RomHash {get;}
    internal string Hash {get;}
    // The RAM card is an image file: its size is the card's capacity and its
    // bytes are the card's contents, written back whenever the internal RAM is.
    internal string? CardPath {get;}
    internal string? CardName=>CardPath is null?null:Path.GetFileName(CardPath);
    internal bool HasCard=>CardPath is not null;
    internal int RomKB {get;}
    internal string RomVersion {get;}
    internal bool IsE650Family=>RomKB==384;
    internal byte[] Pixels {get;} = new byte[240*32];
    internal int InternalKB=>Native.e500_capacity(Handle,0);
    internal int CardKB=>Native.e500_capacity(Handle,1);
    internal Core(string path,int ram=32,string? cardPath=null)
    {
        var rom=File.ReadAllBytes(path);
        // A dump straight from the machine (SAVEM "d:S3A.ROM",&HC0000,&HFFFFF)
        // carries the 16-byte SAVEM header; take the main ROM out of it as it is.
        if(BinaryTransfer.TryReadHeader(rom,out int dumpStart,out int dumpLength)) {
            if(dumpStart==0xc0000 && dumpLength==256*1024)rom=rom.AsSpan(BinaryTransfer.HeaderLength).ToArray();
            else if(dumpStart==0x20000)throw new InvalidDataException($"これは拡張ROM（S3B、{dumpStart:X5}-{dumpStart+dumpLength-1:X5}）のダンプです。単独では起動できないので、tools/merge_savem_rom.py で本体ROM（S3A）と結合した384KBイメージを開いてください。");
            else throw new InvalidDataException($"SAVEMダンプの範囲 {dumpStart:X5}-{dumpStart+dumpLength-1:X5} は内蔵ROM（C0000-FFFFF）ではありません。SAVEM \"d:S3A.ROM\",&HC0000,&HFFFFF で吸い出してください。");
        }
        if(rom.Length!=256*1024 && rom.Length!=384*1024)
            throw new InvalidDataException("PC-E500の256KB ROM、またはPC-E650/PC-U6000の384KB ROMを選択してください。SAVEMヘッダー付きの吸い出しファイルはそのまま開けます。");
        string fileName=Path.GetFileNameWithoutExtension(path);
        if(rom.Length==256*1024 && (fileName.Contains("E650",StringComparison.OrdinalIgnoreCase) || fileName.Contains("U6000",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("PC-E650/PC-U6000には追加128KBを先頭へ結合した384KB ROMが必要です。tools/merge_savem_rom.pyでfullイメージを作成してください。");
        byte[]? card=null;
        if(!string.IsNullOrWhiteSpace(cardPath)) {
            CardPath=Path.GetFullPath(cardPath);
            if(!File.Exists(CardPath))throw new FileNotFoundException("RAMカードのイメージファイルが見つかりません。\n"+CardPath);
            card=File.ReadAllBytes(CardPath);
            if(card.Length%1024!=0 || !HardwareSettings.Sizes.Contains(card.Length/1024))
                throw new InvalidDataException($"RAMカードのイメージは8/16/32/64/128/256KBのいずれかにしてください。\n{CardPath} ({card.Length}バイト)");
        }
        RomKB=rom.Length/1024;
        // The service diagnostic prints these two bytes as the major/minor ROM
        // version. They are the first two bytes of the 16-byte ROM trailer in
        // every known E500-family image (2.2 through 8.4) and in the supplied
        // E650 8.5 image. Read the image itself so renamed files stay accurate.
        byte versionMajor=rom[^16],versionMinor=rom[^15];
        RomVersion=versionMajor<=99 && versionMinor<=99?$"{versionMajor}.{versionMinor}":"不明";
        RomPath=Path.GetFullPath(path);RomHash=Hash=Convert.ToHexString(SHA256.HashData(rom));
        // A stored 32KB setting from a PC-E500 session is not a size the E650/U6000
        // hardware can be built with, so fall back to that machine's stock capacity.
        Handle=Native.e500_create_config(rom,rom.Length,HardwareSettings.CoerceInternal(RomKB,ram),card is null?0:card.Length/1024);
        if(Handle==IntPtr.Zero) throw new InvalidOperationException("エミュレータを初期化できません。");
        if(card is not null)Native.e500_memory(Handle,1,card,card.Length,1);
        // The E650/U6000 character generator table resides in the CE3 extension.
        textDecoder=new LcdTextDecoder(rom);
    }
    internal void Run(int cycles)
    {
        if(Native.e500_run(Handle,cycles)!=0)
            throw new InvalidOperationException($"未定義命令で停止しました。PC={Native.e500_reg(Handle,9):X5}");
    }
    internal void Refresh() => Native.e500_pixels(Handle,Pixels);
    internal byte[] Dump(int address,int length) {
        var data=new byte[length];
        if(Native.e500_dump(Handle,address,data,length)==0)throw new ArgumentOutOfRangeException(nameof(address),"アドレスが範囲外です。");
        return data;
    }
    // Debugger writes: RAM only, so a poke can never patch ROM or fall into
    // unmapped space by accident, and all or nothing.
    internal void Write(int address,byte[] data) {
        if(data.Length==0)return;
        if(Native.e500_write(Handle,address,data,data.Length)==0)
            throw new InvalidOperationException($"{address:X5}-{(long)address+data.Length-1:X5} はRAM以外の領域を含むため書き込めません。内蔵RAM・RAMカードの範囲を指定してください。");
    }
    internal byte[] InternalMemory() {var data=new byte[256];Native.e500_imem(Handle,data);return data;}
    internal ulong Cycles=>Native.e500_cycles(Handle);
    internal uint Register(int id)=>Native.e500_reg(Handle,id);
    internal string ScreenText() {Refresh();return textDecoder.Decode(Pixels);}
    internal void Key(int code,bool down) => Native.e500_key(Handle,code/8,code%8,down?1:0);
    internal void Release() => Native.e500_release_keys(Handle);
    internal void Save(string path)
    {
        byte[] data=new byte[Native.e500_state_size()];
        if(Native.e500_save(Handle,data,data.Length)==0) throw new InvalidOperationException("CE-140Fの通信中・ファイルOPEN中、またはCPUのエラー中は状態保存できません。CLOSE後に保存してください。");
        var json=JsonSerializer.Serialize(new SavedState(2,Hash,Convert.ToHexString(SHA256.HashData(data)),data));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,json);File.Move(temp,path,true);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    internal void Load(string path)
    {
        if(new FileInfo(path).Length>2000000) throw new InvalidDataException("保存ファイルのサイズが不正です。");
        var state=JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path));
        if(state is null || (state.Version!=1 && state.Version!=2) || state.RomSha256!=Hash || state.Data is null ||
           state.DataSha256!=Convert.ToHexString(SHA256.HashData(state.Data)))
            throw new InvalidDataException("保存ファイルが不正、またはROMが異なります。");
        // The snapshot carries the card slot too, but the card is a file of its
        // own: a snapshot must not swap in another card's bytes, and it cannot be
        // resumed with a card of another capacity in the slot. Version 2 keeps the
        // card capacity at offset 12; the v1 layout had no card at all.
        int snapshotCard=state.Data.Length==Native.e500_state_size()?BitConverter.ToInt32(state.Data,12):0;
        if(snapshotCard!=CardKB)
            throw new InvalidDataException($"保存時のRAMカード({(snapshotCard==0?"未装着":snapshotCard+"KB")})と現在のRAMカード({(CardKB==0?"未装着":CardKB+"KB")})が異なります。同じ容量のカードを装着してから読み込んでください。");
        byte[]? card=CardKB>0?CardImage():null;
        if(Native.e500_load(Handle,state.Data,state.Data.Length)==0)
            throw new InvalidDataException("保存ファイルが不正、またはROMが異なります。");
        if(card is not null)Native.e500_memory(Handle,1,card,card.Length,1);
        Refresh();
    }
    internal byte[] CardImage() {
        var data=new byte[CardKB*1024];
        if(data.Length>0)Native.e500_memory(Handle,1,data,data.Length,0);
        return data;
    }
    // Writes the card's contents back into its image file.
    internal void SaveCard() {
        if(CardPath is null || CardKB==0)return;
        AtomicFile.Write(CardPath,CardImage());
    }
    internal void MountDisk(string? path,bool readOnly) {
        if(Native.e500_disk_mount(Handle,path,readOnly?1:0)==0)throw new IOException("CE-140Fの保存フォルダを開けません。");
    }
    internal string DiskTrace() {byte[] b=new byte[16385];int n=Native.e500_disk_trace(Handle,b,b.Length);return System.Text.Encoding.UTF8.GetString(b,0,n);}
    // The internal RAM file for the current capacity: one per capacity, so
    // switching back to a size restores what it held.
    internal string InternalFile(string directory)=>Path.Combine(directory,$"internal-{InternalKB}KB.ram");
    internal void MemoryFiles(string directory,bool load) {
        Directory.CreateDirectory(directory);
        string path=InternalFile(directory);
        if(load) {
            if(!File.Exists(path))return;var data=File.ReadAllBytes(path);
            if(data.Length!=InternalKB*1024 || Native.e500_memory(Handle,0,data,data.Length,1)==0)throw new InvalidDataException("RAM保存ファイルのサイズが不正です: "+path);
        }else {
            var data=new byte[InternalKB*1024];Native.e500_memory(Handle,0,data,data.Length,0);
            AtomicFile.Write(path,data);
        }
    }
    // A fresh card is all 0xFF, which is what the ROM sees in a card that was
    // never initialised, so the first PF1 formats it.
    internal static void CreateCardImage(string path,int kb) {
        if(!HardwareSettings.Sizes.Contains(kb))throw new ArgumentOutOfRangeException(nameof(kb));
        var data=new byte[kb*1024];Array.Fill(data,(byte)0xff);
        AtomicFile.Write(path,data);
    }
    public void Dispose() {if(Handle!=IntPtr.Zero) {Native.e500_destroy(Handle);Handle=IntPtr.Zero;}}
}

internal static class AtomicFile {
    internal static void Write(string path,byte[] bytes) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
