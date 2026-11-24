// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// A BASIC program the machine saved (its own tokenised form) goes straight into
// RAM, with no LOAD typed and no CE-140F.
//
// The ROM keeps its data as a chain of named blocks from the bottom of the
// internal RAM + 18h: DATA (the variables), TEXT (the program), FUNCKEY, AER and
// so on. Each block starts FBh, an 8.3 name, then at +11h the bytes it takes
// up, at +16h the bytes in use, and at +19h the first again. One block holds
// the free space (takes up more than it uses): the variables as a rule, with
// the program packed up against the block after it; while lines are typed in
// PRO mode the ROM moves the program down and gives it the free space. The
// program block from +22h on is the very bytes of a saved file. The ROM keeps
// the program block's address in internal RAM CBh-CDh as well, and works from
// that, not from the chain.
//
// Loading here does what LOAD does: the program, packed up against the block
// after it, takes its room out of the free space; the block before it (the
// one with the free space, or the program's own when it is open) is given what
// is left; and CBh points at the new block. Checked against LOAD typed on the
// same machine, with the ROMs, RAM sizes, card and modes in the tests: the
// blocks and CBh come out byte for byte the same. Anything else - a layout of
// another kind, a file with a password, lines that do not add up - is left to
// LOAD.
// 本体が保存した形（中間コード）の BASIC を、LOAD を打たず CE-140F も使わずに RAM へ直接入れる。
// RAM は内蔵 RAM の下端 +18h からの名前付きの区画の並び（DATA・TEXT・FUNCKEY・AER など）。各区画は FBh・8.3 の名前・
// +11h に区画の大きさ・+16h に使っている大きさ・+19h にもう一度大きさ。空きを持つ区画は1つ（ふつうは変数）で、
// プログラムは次の区画の前に詰めて置かれる（PRO で行を打つ間は、ROM がプログラムを下へ移して空きを持たせる）。
// プログラムの区画の +22h からは、保存したファイルと同じバイト。ROM は区画の場所を内部 RAM の CBh〜CDh にも持つ。
// ここでは LOAD と同じに、プログラムを次の区画の前に詰めて置き、前の区画に残りを持たせ、CBh を直す。
/// <summary>本体が保存した形の BASIC のプログラムを、RAM に直接入れる。</summary>
internal static class BasicImage
{
    const int BlockHeader=0x22,ProgramAt=0x34,ChainStart=0x18;
    static readonly byte[] TextName="TEXT    BAS "u8.ToArray();
    /// <summary>空きに残しておく大きさ（ROM が使う作業の分。LOAD より厳しめ）。</summary>
    const int Margin=0x100;
    static int Le3(ReadOnlySpan<byte> b)=>b[0]|b[1]<<8|b[2]<<16;
    static void Put3(byte[] b,int at,int v){b[at]=(byte)v;b[at+1]=(byte)(v>>8);b[at+2]=(byte)(v>>16);}

    // The file as SAVE writes it: FF 00 00 00, the start (34h) and the end of the
    // program counted from the block's start, eight bytes of password, 0D, the
    // lines, FF. Each line is its number (high byte first), its length, the
    // tokens and 0D; the numbers rise.
    /// <summary>本体が保存した形のプログラムか（パスワードなし、行の並びが正しい）。</summary>
    internal static bool IsSaved(byte[] f) {
        if(f.Length<0x14 || f[0]!=0xff || f[1]!=0 || f[2]!=0 || f[3]!=0)return false;
        if(Le3(f.AsSpan(4))!=ProgramAt || Le3(f.AsSpan(7))!=BlockHeader+f.Length-1)return false;
        // A password hides the program; the ROM itself refuses to save one, so such a file is left to LOAD.
        for(int i=10;i<18;i++)if(f[i]!=0)return false;
        if(f[0x12]!=0x0d || f[^1]!=0xff)return false;
        int at=0x13,last=0;
        while(at<f.Length-1) {
            if(at+3>f.Length-1)return false;
            int number=f[at]<<8|f[at+1],length=f[at+2];
            if(number<=last || number>65279 || length<1 || at+3+length>f.Length-1 || f[at+2+length]!=0x0d)return false;
            last=number;at+=3+length;
        }
        return at==f.Length-1;
    }
    /// <summary>区画1つ：先頭の番地、大きさ、使っている大きさ。</summary>
    readonly record struct Block(int At,int Size,int Used,bool Text);
    /// <summary>区画の並びを読む（形が違えば null）。</summary>
    static List<Block>? Chain(Core core) {
        int bottom=0xc0000-core.InternalKB*1024,at=bottom+ChainStart;
        var blocks=new List<Block>();
        while(at<0xc0000-BlockHeader && blocks.Count<64) {
            var h=core.Dump(at,0x1c);
            if(h[0]!=0xfb)break;
            int size=Le3(h.AsSpan(0x11)),used=Le3(h.AsSpan(0x16));
            if(size<used || used<0x1c || Le3(h.AsSpan(0x19))!=size || at+size>0xc0000)return null;
            blocks.Add(new(at,size,used,h.AsSpan(1,12).SequenceEqual(TextName)));
            at+=size;
        }
        return blocks.Count>0?blocks:null;
    }
    /// <summary>区画の並びの空き領域（バイト。並びが読めなければ -1）。</summary>
    internal static int FreeBytes(Core core) {
        var blocks=Chain(core);
        if(blocks is null)return -1;
        return blocks.Sum(b=>b.Size-b.Used);
    }
    /// <summary>テスト用：区画の並び（番地・大きさ・使っている中身）と内部 RAM の CBh〜CDh を1つのバイト列にしたもの。</summary>
    internal static byte[] TestImage(Core core) {
        var image=new List<byte>(core.InternalMemory().AsSpan(0xcb,3).ToArray());
        foreach(var b in Chain(core)??[]) {
            image.AddRange(BitConverter.GetBytes(b.At));image.AddRange(BitConverter.GetBytes(b.Size));
            image.AddRange(core.Dump(b.At,b.Used));
        }
        return image.ToArray();
    }
    /// <summary>プログラムを RAM に入れる。</summary>
    /// <param name="core">本体</param>
    /// <param name="file">本体が保存した形のファイル（IsSaved が true のもの）</param>
    /// <returns>入れたか（RAM の作りが知っているものでなければ false。何も変えていない）</returns>
    /// <exception cref="InvalidOperationException">空きが足りない</exception>
    internal static bool Load(Core core,byte[] file) {
        if(!IsSaved(file) || !core.CanSnapshot)return false;
        var blocks=Chain(core);if(blocks is null)return false;
        int t=blocks.FindIndex(b=>b.Text);
        if(t<1 || blocks.FindAll(b=>b.Text).Count!=1)return false;
        var imem=core.InternalMemory();
        if(Le3(imem.AsSpan(0xcb))!=blocks[t].At)return false;
        var text=blocks[t];var before=blocks[t-1];
        int size=BlockHeader+file.Length;
        // The program is packed up against the block after it, as LOAD leaves it;
        // the block before takes the room left over. That block holds the free
        // space, or the program does (open for typing in PRO mode).
        int top=text.At+text.Size,at=top-size;
        bool open=text.Size>text.Used,beforeFree=before.Size>before.Used;
        if(!open && !beforeFree)return false;
        int room=at-(before.At+before.Used);
        if(room<Margin)throw new InvalidOperationException(L.T($"プログラムが入りきりません（{size}バイト必要で、空きは{Math.Max(0,top-before.At-before.Used-Margin)}バイト）。",$"The program does not fit ({size} bytes needed, {Math.Max(0,top-before.At-before.Used-Margin)} free)."));
        var block=new byte[size];
        core.Dump(text.At,BlockHeader).CopyTo(block,0);
        Put3(block,0x11,size);Put3(block,0x16,size);Put3(block,0x19,size);
        file.CopyTo(block,BlockHeader);
        var head=core.Dump(before.At,0x1c);
        Put3(head,0x11,at-before.At);Put3(head,0x19,at-before.At);
        // Internal RAM first: it is the one step that can still refuse, and then nothing has been written.
        if(at!=text.At)core.WriteInternalMemory(0xcb,[(byte)at,(byte)(at>>8),(byte)(at>>16)]);
        core.Write(at,block);core.Write(before.At,head);
        return true;
    }
}
