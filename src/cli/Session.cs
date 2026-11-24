// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
using System.Text;
namespace PokeCom;

/// <summary>コアのうち、デバッグ用の書き込み。</summary>
internal static class DebugNative
{
    [DllImport("e500core",CallingConvention=CallingConvention.Cdecl)] internal static extern void e500_set_reg(IntPtr m,int id,uint value);
    [DllImport("e500core",CallingConvention=CallingConvention.Cdecl)] internal static extern int e500_debug_write_imem(IntPtr m,int at,byte[] data,int length);
}

/// <summary>本体の構成（アプリの「設定」のうち、画面に関係しないもの）。</summary>
/// <param name="Rom">ROM のファイル</param>
/// <param name="Ram">内蔵 RAM（KB）</param>
/// <param name="Card">RAM カードのイメージファイル（空ならなし）</param>
/// <param name="Disk">CE-140F のフォルダ（空なら、一時フォルダ）</param>
/// <param name="DiskReadOnly">CE-140F を書き込み禁止にするか</param>
/// <param name="DiskConnected">CE-140F を接続するか</param>
/// <param name="Printer">CE-126P を接続するか</param>
/// <param name="AutoPowerOff">自動電源 OFF を働かせるか</param>
internal sealed record Setup(string Rom,int Ram=32,string Card="",string Disk="",bool DiskReadOnly=false,bool DiskConnected=true,bool Printer=true,bool AutoPowerOff=false);

// A machine with no window, run as fast as the PC goes: what the command line
// and the MCP server work on. It is a machine of its own: the RAM files and
// settings of the emulator with a window are never touched. Its CE-140F is a
// scratch folder unless one is given, and a RAM card only the file given.
/// <summary>画面のない本体1台。普段のエミュレータの RAM・設定には触れず、PC の速さいっぱいで動かす。</summary>
internal sealed class Session : IDisposable
{
    /// <summary>エミュレータの1秒のサイクル数。</summary>
    internal const int CpuHz=768000;
    const int KeyDownCycles=18000,KeyUpCycles=24000,ShiftCycles=18000,Slice=CpuHz/64;
    internal Core Core {get;private set;}
    internal Setup Setup {get;private set;}
    internal PrinterPaper Paper {get;}=new();
    readonly string scratch;
    readonly short[] sound=new short[48000];
    byte[] cardSaved=[];
    /// <summary>録音中の音（録音していなければ null）。</summary>
    internal List<short>? Recording {get;set;}

    internal Session(Setup setup) {
        Setup=setup;
        scratch=Directory.CreateTempSubdirectory("pokecom-").FullName;
        try {Core=Make(setup);} catch {Directory.Delete(scratch,true);throw;}
    }
    Core Make(Setup s) {
        var core=new Core(s.Rom,s.Ram,s.Card.Length>0?s.Card:null){Paper=Paper};
        try {
            core.PrinterConnected=s.Printer;Mount(core,s);
            cardSaved=core.CardImage();
            return core;
        } catch {core.Dispose();throw;}
    }
    void Mount(Core core,Setup s)=>core.MountDisk(!s.DiskConnected?null:s.Disk.Length>0?s.Disk:scratch,s.DiskReadOnly);
    /// <summary>CE-140F のフォルダ（接続していなければ例外）。</summary>
    internal string DriveFolder=>!Setup.DiskConnected?throw new InvalidOperationException(L.T("CE-140F は接続されていません。","The CE-140F is not connected.")):Setup.Disk.Length>0?Setup.Disk:scratch;
    public void Dispose() {
        try {SaveCard();} finally {
            Core.Dispose();
            try {Directory.Delete(scratch,true);} catch(Exception e) when (e is IOException or UnauthorizedAccessException) {}
        }
    }
    /// <summary>RAM カードの中身が変わっていれば、イメージファイルに書く。</summary>
    internal void SaveCard() {
        if(!Core.HasCard)return;
        var now=Core.CardImage();
        if(now.AsSpan().SequenceEqual(cardSaved))return;
        Core.SaveCard();cardSaved=now;
    }
    // The settings a machine in use can take at once are taken at once; another
    // RAM size or card is another machine, switched on afresh. The internal RAM
    // goes over when its size stays, as it does in the emulator with a window.
    /// <summary>構成を変える。内蔵 RAM の容量かカードが変われば、電源を入れ直す（容量が同じなら内蔵 RAM の中身は残す）。</summary>
    /// <returns>電源を入れ直したか</returns>
    internal bool Configure(Setup next) {
        if(next.Rom==Setup.Rom && next.Ram==Setup.Ram && next.Card==Setup.Card) {
            if(next.DiskConnected!=Setup.DiskConnected || next.Disk!=Setup.Disk || next.DiskReadOnly!=Setup.DiskReadOnly)Mount(Core,next);
            if(next.Printer!=Setup.Printer)Core.PrinterConnected=next.Printer;
            Setup=next;return false;
        }
        SaveCard();
        var memory=new byte[Core.InternalKB*1024];
        Native.e500_memory(Core.Handle,0,memory,memory.Length,0);
        var old=Core;bool sameRom=next.Rom==Setup.Rom;
        Core=Make(next);Setup=next;old.Dispose();
        if(sameRom && Core.InternalKB*1024==memory.Length)Native.e500_memory(Core.Handle,0,memory,memory.Length,1);
        Boot();return true;
    }

    /// <summary>CPU を動かし、音を取り出す（録音中ならためる）。</summary>
    void Step(int cycles) {
        Core.Run(cycles);
        int n;while((n=Native.e500_audio(Core.Handle,sound,sound.Length))>0)Recording?.AddRange(sound.AsSpan(0,n));
    }
    /// <summary>本体を seconds 秒（エミュレータの時間）動かす。</summary>
    internal void Run(double seconds) {
        for(long left=(long)(seconds*CpuHz);left>0;left-=Slice) {
            Step((int)Math.Min(left,Slice));
            if(!Setup.AutoPowerOff)Core.HoldPower();
        }
    }
    /// <summary>表示シンボル BUSY・RUN・PRO。</summary>
    internal (bool Busy,bool Run,bool Pro) Mode() {int s=Native.e500_symbols(Core.Handle,0);return ((s&1)!=0,(s&2)!=0,(s&4)!=0);}
    internal bool PowerOff=>(Native.e500_status(Core.Handle)&1)!=0;
    /// <summary>点いている表示シンボルの名前。</summary>
    internal List<string> Symbols() {
        int a=Native.e500_symbols(Core.Handle,0),b=Native.e500_symbols(Core.Handle,4),c=Native.e500_symbols(Core.Handle,5),d=Native.e500_symbols(Core.Handle,6);
        var on=new List<string>();
        foreach(var (name,value,mask) in new[]{("BUSY",a,1),("RUN",a,2),("PRO",a,4),("DBL",a,8),("小文字",b,1),("カナ",b,2),("HYP",b,4),("CAPS",b,8),("2ndF",b,16),("PRINT",c,1),("E",c,2)})
            if((value&mask)!=0)on.Add(name);
        // DE + G + RAD read DEG / GRAD / RAD, as on the glass.
        string? angle=(c&0x1c) switch{0x18=>"DEG",0x0c=>"GRAD",0x04=>"RAD",_=>null};
        if(angle is not null)on.Add(angle);
        if((d&1)!=0)on.Add("BATT");
        return on;
    }
    internal string Screen()=>Core.ScreenText().Replace("\r\n","\n");
    internal byte[] Picture(int scale=4){Core.Refresh();return Png.Lcd(Core.Pixels,scale);}
    /// <summary>CE-126P の紙の文字（印字の全部）。</summary>
    internal string Printed()=>Paper.Text().Replace("\r\n","\n").TrimEnd('\n');

    // ---- keys ----
    static readonly Dictionary<string,int> codes=new(Keyboard.Codes,StringComparer.OrdinalIgnoreCase) {
        ["BRK"]=Keyboard.Codes["ON/BRK"],["ON"]=Keyboard.Codes["ON/BRK"],["CLS"]=Keyboard.Codes["C-CE"],
        ["PF1"]=Keyboard.Codes["F1"],["PF2"]=Keyboard.Codes["F2"],["PF3"]=Keyboard.Codes["F3"],["PF4"]=Keyboard.Codes["F4"],["PF5"]=Keyboard.Codes["F5"]
    };
    static int Code(string name)=>codes.TryGetValue(name.Trim(),out int code)?code:throw new ArgumentException(L.T($"キー {name} はありません。使える名前: ",$"There is no key {name}. The names are: ")+string.Join(" ",Keyboard.Codes.Keys));
    // "+" itself and "+/-" are keys too.
    static int[] Codes(string names) {
        string[] parts=codes.ContainsKey(names)?[names]:names.Split('+',StringSplitOptions.RemoveEmptyEntries);
        return parts.Length==0?throw new ArgumentException(L.T("キーの名前がありません。","No key was named.")):parts.Select(Code).ToArray();
    }
    /// <summary>キーの名前が正しいかだけを確かめる。</summary>
    internal static void CheckKey(string names)=>Codes(names);
    /// <summary>キーを1つ押して離す（names を + でつなぐと、前のものを押したまま。例：SHIFT+Q）。</summary>
    internal void Key(string names,double hold=0) {
        var keys=Codes(names);
        for(int i=0;i<keys.Length;i++){Core.Key(keys[i],true);Step(i==keys.Length-1?KeyDownCycles:ShiftCycles);}
        if(hold>0)Run(hold);
        foreach(int code in keys.Reverse()){Core.Key(code,false);Step(KeyUpCycles);}
    }
    /// <summary>キーを押したままにする、または離す。</summary>
    internal void Hold(string names,bool down){foreach(int code in Codes(names)){Core.Key(code,down);Step(KeyDownCycles);}}
    /// <summary>文字をキー入力として打つ（改行は ENTER）。打てない文字があれば、何も打たずに例外。</summary>
    internal void Type(string text) {
        text=text.Replace("\r\n","\n").Replace("\t","    ");
        var bad=text.Where(c=>c!='\n' && !Typable(c)).Distinct().ToArray();
        if(bad.Length>0)throw new ArgumentException(L.T($"本体のキーにない文字です: {new string(bad)}",$"No key types: {new string(bad)}"));
        foreach(char c in text) {
            if(c=='\n'){Key("ENTER");continue;}
            if(c==' '){Key("SPACE");continue;}
            string name=char.ToUpperInvariant(c).ToString();
            if(Keyboard.Codes.ContainsKey(name))Key(name);
            else {
                int shift=Keyboard.Codes["SHIFT"];
                Core.Key(shift,true);Step(ShiftCycles);Key(Keyboard.Shifted[c]);Core.Key(shift,false);Step(ShiftCycles);
            }
        }
    }
    static bool Typable(char c)=>c==' ' || Keyboard.Codes.ContainsKey(char.ToUpperInvariant(c).ToString()) || Keyboard.Shifted.ContainsKey(c);

    // ---- BASIC ----
    // A fresh machine asks for PF1 (the memory set up), then BASIC for its prompt;
    // one whose RAM is set up shows its menu. At the largest RAM sizes the keys are
    // not looked at for a while, so each is tried until the machine answers.
    /// <summary>電源を入れたばかりの本体を、BASIC の RUN の表示まで進める。</summary>
    internal void Boot() {
        Run(2.5);
        for(int i=0;i<7 && !Mode().Run && !Mode().Pro;i++) {
            Key("BASIC");Run(2);
            if(Mode().Run || Mode().Pro)break;
            Key("F1");Run(2);
        }
        if(!Mode().Run && !Mode().Pro)throw new InvalidOperationException(L.T("BASIC の表示になりませんでした。","BASIC did not come up.")+"\n"+Screen());
        Prompt("RUN");
    }
    /// <summary>BASIC の RUN か PRO の入力待ちにする（電源が切れていれば入れ、動いているプログラムは止める）。</summary>
    internal void Prompt(string mode) {
        // Switching off takes the machine a moment; caught in it, it is waited for.
        // 電源が切れる途中なら、切れるのを待ってから入れる。
        if(!Mode().Run && !Mode().Pro && !Mode().Busy && !PowerOff)Run(1);
        if(PowerOff){Key("ON/BRK");Run(1.5);}
        for(int i=0;i<3 && Mode().Busy;i++){Key("ON/BRK");Run(0.5);}
        Key("C-CE");Run(0.2);
        for(int i=0;i<4;i++) {
            var m=Mode();
            if(mode=="PRO"?m.Pro:m.Run)return;
            Key("BASIC");Run(0.5);
        }
        throw new InvalidOperationException(L.T($"BASIC の {mode} の表示にできません。",$"BASIC's {mode} prompt did not come up.")+"\n"+Screen());
    }
    /// <summary>BUSY が消えるまで動かす（seconds 秒まで）。</summary>
    /// <returns>終わったか（false なら、まだ実行中か入力待ち）</returns>
    internal bool Finish(double seconds) {
        // BUSY comes on a moment after ENTER, and goes out between statements for no time at all.
        Run(Math.Min(0.15,seconds));
        for(double t=0.15;t<seconds;t+=0.05) {
            if(!Mode().Busy){Run(0.1);if(!Mode().Busy)return true;}
            Run(0.05);
        }
        return !Mode().Busy;
    }
    /// <summary>BASIC の命令を1行、RUN の表示で実行する。</summary>
    internal bool Exec(string line,double seconds){Prompt("RUN");Type(line.TrimEnd('\r','\n')+"\n");return Finish(seconds);}

    // Every ROM has its messages as one table of strings ended by zeros, in order
    // of their numbers from 1, "Syntax error".
    readonly Dictionary<string,HashSet<string>> messageTables=[];
    HashSet<string> Messages() {
        if(messageTables.TryGetValue(Core.RomHash,out var known))return known;
        var found=new HashSet<string>(StringComparer.Ordinal);
        byte[] rom=File.ReadAllBytes(Core.RomPath);
        int at=rom.AsSpan().IndexOf("\0Undefined line\0"u8)+1;
        if(at>0) {
            int start=rom.AsSpan(0,at).LastIndexOf("Syntax error\0"u8);
            for(int i=start,n=1;start>=0 && n<=90 && i<rom.Length;n++) {
                int end=Array.IndexOf(rom,(byte)0,i);
                if(end<0)break;
                if(end>i)found.Add(Encoding.Latin1.GetString(rom,i,end-i));
                i=end+1;
            }
        }
        return messageTables[Core.RomHash]=found;
    }
    /// <summary>BASIC がエラーで止まっているなら、その文（なければ null）。正常に終わると最後の行は &gt; か値になる。</summary>
    internal string? BasicError() {
        var lines=Screen().Split('\n').Select(l=>l.TrimEnd()).Where(l=>l.Length>0 && l!="_").ToList();
        // A long message goes on over the next line.
        for(int n=1;n<=Math.Min(2,lines.Count);n++) {
            string text=string.Concat(lines.TakeLast(n).Select((l,i)=>i<n-1?l.PadRight(40):l)).Trim();
            string bare=System.Text.RegularExpressions.Regex.Replace(text,@" in \d+$","");
            if(Messages().Contains(bare))return text;
        }
        return null;
    }

    // A program goes in as the emulator's "Load program" takes it: one the machine
    // saved straight into RAM, and text through the CE-140F, the ROM's own LOAD.
    // Text is made what LOAD takes: CR LF, no empty lines (LOAD stops at one), in
    // the machine's character set. The drive is the scratch folder meanwhile, so a
    // folder of the user's gets no file of ours.
    /// <summary>BASIC のプログラム（テキストか、本体で保存した形）を読み込む。</summary>
    internal void LoadBasic(byte[] file) {
        Prompt("RUN");
        if(BasicImage.IsSaved(file) && BasicImage.Load(Core,file))return;
        bool saved=file.Length>0 && file[0]==0xff;
        byte[] data=saved?file:FromText(file);
        WithScratchDrive(()=>{
            File.WriteAllBytes(Path.Combine(scratch,"PROG.BAS"),data);
            Prompt("PRO");Type("NEW\n");Run(0.5);Prompt("RUN");
            Type("LOAD \"X:PROG.BAS\"\n");
            bool done=Finish(30+data.Length/200.0);
            if(!done)throw new InvalidOperationException(L.T("プログラムを読み込めませんでした。","The program could not be loaded.")+"\n"+Screen());
            if(BasicError() is string error)throw new InvalidOperationException(L.T("プログラムの読み込みでエラーになりました: ","Loading the program ended in an error: ")+error);
        });
    }
    void WithScratchDrive(Action action) {
        var there=Setup;var mine=there with{Disk="",DiskReadOnly=false,DiskConnected=true};
        if(there!=mine)Mount(Core,mine);
        try {action();} finally {
            File.Delete(Path.Combine(scratch,"PROG.BAS"));
            if(there!=mine)Mount(Core,there);
        }
    }
    static byte[] FromText(byte[] file) {
        string text;
        try {text=new UTF8Encoding(false,true).GetString(file.AsSpan(file is [0xef,0xbb,0xbf,..]?3:0));}
        catch(DecoderFallbackException){throw new ArgumentException(L.T("プログラムは UTF-8 のテキストか、本体で保存した形にしてください。","The program must be UTF-8 text, or in the form the machine saves."));}
        var bytes=new List<byte>();int n=0;
        foreach(string raw in text.TrimEnd('\x1a').Split('\n')) {
            n++;string line=raw.Trim();
            if(line.Length==0)continue;
            if(!char.IsAsciiDigit(line[0]))throw new ArgumentException(L.T($"{n} 行目に行番号がありません: {line}",$"Line {n} has no line number: {line}"));
            foreach(char c in line)bytes.Add(ToMachine(c) ?? throw new ArgumentException(L.T($"{n} 行目に、本体にない文字 {c} があります。",$"Line {n} has {c}, which the machine does not have.")));
            bytes.Add(13);bytes.Add(10);
        }
        if(bytes.Count==0)throw new ArgumentException(L.T("プログラムが空です。","The program is empty."));
        return bytes.ToArray();
    }
    static readonly Dictionary<char,byte> machineCodes=Enumerable.Range(0x20,0xe0).Where(b=>b!=0xa0).Select(b=>(b,c:LcdTextDecoder.Unicode((byte)b)))
        .Where(x=>x.c is not null).GroupBy(x=>x.c!.Value).ToDictionary(g=>g.Key,g=>(byte)g.First().b);
    static byte? ToMachine(char c)=>c switch{'¥'=>0x5c,'\t'=>0x20,_=>machineCodes.TryGetValue(c,out byte b)?b:null};
    // The program is saved by the ROM itself, and the machine then put back as it
    // was, screen and all: reading it changes nothing.
    /// <summary>今の BASIC のプログラムを取り出す（本体の状態は変えない）。</summary>
    /// <param name="saved">本体で保存した形か（false ならテキスト）</param>
    /// <returns>中身（プログラムがなければ空）</returns>
    internal byte[] SaveBasic(bool saved) {
        byte[] state=Core.Snapshot();byte[] result=[];
        WithScratchDrive(()=>{
            Prompt("RUN");
            string path=Path.Combine(scratch,"PROG.BAS");
            Type("SAVE \"X:PROG.BAS\""+(saved?"":",A")+"\n");
            bool done=Finish(120);
            if(done && BasicError() is null && File.Exists(path))result=File.ReadAllBytes(path);
            else if(!done)throw new InvalidOperationException(L.T("プログラムを取り出せませんでした。","The program could not be read.")+"\n"+Screen());
        });
        byte[]? card=Core.CardKB>0?Core.CardImage():null;
        if(Native.e500_load(Core.Handle,state,state.Length)==0)throw new InvalidOperationException(Core.CannotLoad);
        if(card is not null)Native.e500_memory(Core.Handle,1,card,card.Length,1);
        return result;
    }
    /// <summary>本体の文字コードのテキストを、Unicode にする（改行は LF）。</summary>
    internal static string FromMachine(byte[] data) {
        var b=new StringBuilder();
        foreach(byte c in data)if(c==10)b.Append('\n');else if(c is not (13 or 0x1a))b.Append(LcdTextDecoder.Unicode(c) ?? '□');
        return b.ToString().TrimEnd('\n');
    }
    /// <summary>読み込んだプログラムを実行する。input は、実行中に打つ文字（INPUT への答えなど）。</summary>
    /// <returns>終わったか（false なら、seconds 秒たってもまだ実行中か入力待ち）</returns>
    internal bool RunBasic(string? input,double seconds) {
        Prompt("RUN");Type("RUN\n");
        if(!string.IsNullOrEmpty(input)){Run(0.5);foreach(string line in input.Replace("\r\n","\n").TrimEnd('\n').Split('\n')){Type(line+"\n");Run(0.5);}}
        return Finish(seconds);
    }

    // ---- machine code ----
    /// <summary>機械語エリアを size バイトにする（ROM の確保の処理）。</summary>
    internal void Reserve(int size) {
        Prompt("RUN");
        var area=MachineCodeArea.Read(Core);int free=BasicImage.FreeBytes(Core);
        if(area is not {} a || free<0)throw new InvalidOperationException(L.T("今の RAM からは機械語エリアを読めません。","The machine-code area cannot be read from the RAM now."));
        int most=MachineCodeArea.Largest(a.Size,free);
        if(size<0 || size>most)throw new InvalidOperationException(L.T($"大きすぎます。いちばん大きくて {most:X}h バイトです。",$"Too large: the most is {most:X}h bytes."));
        if(size==a.Size)return;
        Type(MachineCodeArea.Command(size)+"\n");Run(2);
        // The ROM ends at the main menu; BASIC brings the prompt back.
        for(int i=0;i<4 && !Mode().Run && !Mode().Pro;i++){Key("BASIC");Run(1);}
        Prompt("RUN");
        if(MachineCodeArea.Read(Core) is not {Size:var got} || got!=size)throw new InvalidOperationException(L.T("機械語エリアを確保できませんでした。","The machine-code area was not reserved."));
    }
    // The rules of the emulator's "Load program": only RAM that is there, never over
    // parts in use; a place in BASIC's free space is reserved first. With an
    // address given it is the debugger's "BIN load": any RAM, asked for by name.
    /// <summary>BIN を読み込む。address があればその番地へ（RAM ならどこでも）、なければヘッダーの番地へ。</summary>
    /// <param name="reserve">機械語エリアの外なら、先に確保するか</param>
    /// <returns>結果の説明</returns>
    internal string LoadBin(byte[] file,int? address,bool reserve) {
        bool headed=BinaryTransfer.TryReadHeader(file,out int start,out int length);
        if(address is int to) {
            var (at,n)=BinaryTransfer.Load(Core,headed?file[BinaryTransfer.HeaderLength..]:file,to,false);
            return L.T($"{at:X5}-{at+n-1:X5} に書き込みました。",$"Wrote {at:X5}-{at+n-1:X5}.");
        }
        if(!headed)throw new InvalidDataException(L.T("SAVE M のヘッダーがない BIN は、番地を指定してください。","A BIN without the SAVE M header needs an address."));
        int bottom=0xc0000-Core.InternalKB*1024,end=start+length;
        bool inside=start>=bottom && end<=0xc0000,card=Core.CardKB>0 && start>=0x40000 && end<=0x40000+Core.CardKB*1024;
        if(!inside && !card)throw new InvalidDataException(L.T($"{start:X5}-{end-1:X5} は、この本体の RAM にありません。",$"{start:X5}-{end-1:X5} is not in this machine's RAM."));
        bool inUse=(inside?RamLayout.Internal(Core):RamLayout.Card(Core)).Any(r=>r.Kind is not (RegionKind.Free or RegionKind.MachineCode) && r.Start<end && start<r.End);
        bool outside=inside && end<=0xbfc00 && MachineCodeArea.Read(Core) is {} area && start<area.Start;
        string done="";
        if(outside && reserve) {
            Reserve(0xbfc00-start);
            done=L.T("機械語エリアを確保し、","Reserved the machine-code area and ");
        }
        else if(inUse)throw new InvalidDataException(L.T($"{start:X5}-{end-1:X5} には使用中の所（プログラム・変数・作業域など）があります。番地を指定すれば書けます。",$"{start:X5}-{end-1:X5} holds parts in use (program, variables, work areas). Give the address to write it all the same."));
        BinaryTransfer.Load(Core,file,0,true);
        return done+L.T($"{start:X5}-{end-1:X5} に書き込みました。",$"{(done.Length>0?"wrote":"Wrote")} {start:X5}-{end-1:X5}.");
    }

    // ---- resets ----
    /// <summary>RESET 穴（データは残る）。</summary>
    internal void Reset(){Core.Release();Native.e500_reset(Core.Handle,0);Run(1.5);}
    /// <summary>オールリセット（ON＋RESET）。answer が true なら Y（全消去）、false なら N、null なら確認の画面で止める。</summary>
    internal void AllReset(bool? answer) {
        Core.Release();
        int on=Keyboard.Codes["ON/BRK"];
        Native.e500_reset(Core.Handle,0);
        Core.Key(on,true);Run(2.7);Core.Key(on,false);Run(1.4);
        if(answer is not bool yes)return;
        Key(yes?"Y":"N");Run(6);
    }
    // ---- memory ----
    /// <summary>レジスタの名前と、コアの番号。</summary>
    internal static readonly Dictionary<string,int> Registers=new(StringComparer.OrdinalIgnoreCase){["A"]=0,["IL"]=1,["BA"]=2,["I"]=3,["X"]=4,["Y"]=5,["U"]=7,["S"]=8,["P"]=9,["F"]=10};
    internal void SetRegister(string name,int value) {
        if(!Registers.TryGetValue(name,out int id))throw new ArgumentException(L.T($"レジスタ {name} はありません。使える名前: ",$"There is no register {name}. The names are: ")+string.Join(" ",Registers.Keys));
        DebugNative.e500_set_reg(Core.Handle,id,(uint)value);
    }
    internal void WriteInternal(int at,byte[] data) {
        if(DebugNative.e500_debug_write_imem(Core.Handle,at,data,data.Length)==0)throw new ArgumentException(L.T("内部 RAM に書けるのは 00〜EE です（EF から先は、書くと働く I/O です）。","Internal RAM 00 to EE can be written (from EF on are registers that act when written)."));
    }
}
