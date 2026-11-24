// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text;
namespace PokeCom;

// A machine with no window, run as fast as the PC goes: what the command line
// and the MCP server work on. It is a machine of its own - the RAM files,
// settings and drive folder of the emulator with a window are never touched -
// and its CE-140F is a scratch folder, removed at the end.
/// <summary>画面のない本体1台。普段のエミュレータの RAM・設定・ドライブには触れず、PC の速さいっぱいで動かす。</summary>
internal sealed class Session : IDisposable
{
    /// <summary>エミュレータの1秒のサイクル数。</summary>
    internal const int CpuHz=768000;
    const int KeyDownCycles=18000,KeyUpCycles=24000,ShiftCycles=18000,Slice=CpuHz/64;
    internal Core Core {get;}
    readonly PrinterPaper paper=new();
    readonly string drive;
    internal Session(string rom,int ram) {
        Core=new Core(rom,ram){Paper=paper,PrinterConnected=true};
        drive=Directory.CreateTempSubdirectory("pokecom-").FullName;
        Core.MountDisk(drive,false);
    }
    public void Dispose() {
        Core.Dispose();
        try {Directory.Delete(drive,true);} catch(Exception e) when (e is IOException or UnauthorizedAccessException) {}
    }

    /// <summary>本体を seconds 秒（エミュレータの時間）動かす。</summary>
    internal void Run(double seconds) {
        for(long left=(long)(seconds*CpuHz);left>0;left-=Slice){Core.Run((int)Math.Min(left,Slice));Core.HoldPower();}
    }
    /// <summary>表示シンボル BUSY・RUN・PRO。</summary>
    internal (bool Busy,bool Run,bool Pro) Mode() {int s=Native.e500_symbols(Core.Handle,0);return ((s&1)!=0,(s&2)!=0,(s&4)!=0);}
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
    internal string Printed()=>paper.Text().Replace("\r\n","\n").TrimEnd('\n');

    /// <summary>キーを1つ押して離す（names を + でつなぐと、前のものを押したまま。例：SHIFT+Q）。</summary>
    internal void Key(string names,double hold=0) {
        // "+" itself and "+/-" are keys too.
        string[] parts=Keyboard.Codes.ContainsKey(names)?[names]:names.Split('+',StringSplitOptions.RemoveEmptyEntries);
        var codes=parts.Select(Code).ToArray();
        if(codes.Length==0)throw new ArgumentException(L.T("キーの名前がありません。","No key was named."));
        foreach(int code in codes){Core.Key(code,true);Core.Run(code==codes[^1]?KeyDownCycles:ShiftCycles);}
        if(hold>0)Run(hold);
        foreach(int code in codes.Reverse()){Core.Key(code,false);Core.Run(KeyUpCycles);}
    }
    static readonly Dictionary<string,int> codes=new(Keyboard.Codes,StringComparer.OrdinalIgnoreCase) {
        ["BRK"]=Keyboard.Codes["ON/BRK"],["ON"]=Keyboard.Codes["ON/BRK"],["CLS"]=Keyboard.Codes["C-CE"],
        ["PF1"]=Keyboard.Codes["F1"],["PF2"]=Keyboard.Codes["F2"],["PF3"]=Keyboard.Codes["F3"],["PF4"]=Keyboard.Codes["F4"],["PF5"]=Keyboard.Codes["F5"]
    };
    static int Code(string name)=>codes.TryGetValue(name.Trim(),out int code)?code:throw new ArgumentException(L.T($"キー {name} はありません。使える名前: ",$"There is no key {name}. The names are: ")+string.Join(" ",Keyboard.Codes.Keys));
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
                Core.Key(shift,true);Core.Run(ShiftCycles);Key(Keyboard.Shifted[c]);Core.Key(shift,false);Core.Run(ShiftCycles);
            }
        }
    }
    static bool Typable(char c)=>c==' ' || Keyboard.Codes.ContainsKey(char.ToUpperInvariant(c).ToString()) || Keyboard.Shifted.ContainsKey(c);

    // The first start asks for PF1 (the memory set up), then BASIC for its
    // prompt. At the largest RAM sizes the keys are not looked at for a while
    // after the menu shows, so each is tried until the machine answers.
    /// <summary>電源を入れたばかりの本体を、BASIC の RUN の表示まで進める。</summary>
    internal void Boot() {
        Run(2.5);
        for(int i=0;i<6 && !Mode().Run && !Mode().Pro;i++){Key("F1");Run(2);Key("BASIC");Run(2);}
        if(!Mode().Run && !Mode().Pro)throw new InvalidOperationException(L.T("BASIC の表示になりませんでした。","BASIC did not come up.")+"\n"+Screen());
        Prompt("RUN");
    }
    /// <summary>BASIC の RUN か PRO の入力待ちにする（動いているプログラムは止める）。</summary>
    internal void Prompt(string mode) {
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

    // A program goes in through the CE-140F, the ROM's own LOAD, whether it is
    // text or the machine's own form. Text is made what LOAD takes: CR LF, no
    // empty lines (LOAD stops at one), in the machine's character set.
    /// <summary>BASIC のプログラム（テキストか、本体で保存した形）を読み込む。</summary>
    internal void LoadBasic(byte[] file) {
        Prompt("RUN");
        bool saved=file.Length>0 && file[0]==0xff;
        byte[] data=saved?file:FromText(file);
        File.WriteAllBytes(Path.Combine(drive,"PROG.BAS"),data);
        Type("NEW\n");Run(0.3);Prompt("PRO");Type("NEW\n");Run(0.5);Prompt("RUN");
        Type("LOAD \"X:PROG.BAS\"\n");
        bool done=Finish(30+data.Length/200.0);
        string trace=Core.DiskTrace();
        if(!done || trace.Contains("ERROR",StringComparison.Ordinal))throw new InvalidOperationException(L.T("プログラムを読み込めませんでした。","The program could not be loaded.")+"\n"+Screen());
        // An error in the text is said on the screen and the program stays empty or cut short.
        string shown=Screen();
        if(shown.Contains("error",StringComparison.OrdinalIgnoreCase) || shown.Contains("Undefined",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("プログラムの読み込みでエラーになりました。","Loading the program ended in an error.")+"\n"+shown);
    }
    static byte[] FromText(byte[] file) {
        string text=new UTF8Encoding(false,true).GetString(file.AsSpan(file is [0xef,0xbb,0xbf,..]?3:0));
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
    /// <summary>読み込んだプログラムを実行する。input は、実行中に打つ文字（INPUT への答えなど）。</summary>
    /// <returns>終わったか（false なら、seconds 秒たってもまだ実行中か入力待ち）</returns>
    internal bool RunBasic(string? input,double seconds) {
        Prompt("RUN");Type("RUN\n");
        if(!string.IsNullOrEmpty(input)){Run(0.5);foreach(string line in input.Replace("\r\n","\n").TrimEnd('\n').Split('\n')){Type(line+"\n");Run(0.5);}}
        return Finish(seconds);
    }
}
