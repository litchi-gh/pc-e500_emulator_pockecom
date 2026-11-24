// SPDX-License-Identifier: GPL-2.0-or-later
using System.Globalization;
using System.Text;
namespace PokeCom;

/// <summary>引数の種類。</summary>
internal enum Kind {Text,Number,Integer,Flag,List,Address}
/// <summary>操作の引数1つ。</summary>
/// <param name="Place">コマンドラインで名前なしに書けるか（書く順。0 なら --名前 だけ）</param>
internal sealed record Field(string Name,Kind Kind,string About,bool Required=false,int Place=0);
/// <summary>操作の結果。</summary>
/// <param name="Text">説明（画面のほかに伝えること。なければ空）</param>
/// <param name="Show">画面も見せるか</param>
/// <param name="Code">0 正常、3 時間内に終わらなかった、4 BASIC のエラー</param>
/// <param name="Picture">PNG（なければ null）</param>
internal sealed record Reply(string Text="",bool Show=true,int Code=0,byte[]? Picture=null);
/// <summary>操作1つ。コマンドラインのコマンドと MCP のツールは、どちらもここから作る。</summary>
/// <param name="Plain">本体を使わない操作か</param>
internal sealed record Op(string Name,string About,Field[] Fields,Func<Values,Reply> Call,bool Plain=false);

/// <summary>操作に渡された値（名前から引く）。種類と範囲は、取り出すときに確かめる。</summary>
internal sealed class Values
{
    readonly Dictionary<string,object> values=new(StringComparer.Ordinal);
    internal Values(Op op,IEnumerable<KeyValuePair<string,object>> given) {
        foreach(var (name,value) in given) {
            if(op.Fields.All(f=>f.Name!=name))throw new ArgumentException(L.T($"{op.Name} に {name} はありません。",$"{op.Name} does not take {name}."));
            values[name]=value;
        }
        if(op.Fields.FirstOrDefault(f=>f.Required && !values.ContainsKey(f.Name)) is Field missing)throw new ArgumentException(L.T($"{missing.Name} がありません。",$"{missing.Name} is missing."));
    }
    internal bool Has(string name)=>values.ContainsKey(name);
    internal string? Text(string name)=>values.TryGetValue(name,out var v)?v as string ?? throw Wrong(name,"text"):null;
    internal string Need(string name)=>Text(name) ?? throw new ArgumentException(L.T($"{name} がありません。",$"{name} is missing."));
    internal double Number(string name,double fallback,double max) {
        if(!values.TryGetValue(name,out var v))return fallback;
        double d=v switch{double n=>n,string s when double.TryParse(s,CultureInfo.InvariantCulture,out double n)=>n,_=>double.NaN};
        return d>=0 && d<=max?d:throw Wrong(name,$"a number from 0 to {max.ToString(CultureInfo.InvariantCulture)}");
    }
    internal int Integer(string name,int fallback,int max) {
        double d=Number(name,fallback,max);
        return d==Math.Floor(d)?(int)d:throw Wrong(name,"a whole number");
    }
    internal bool? Flag(string name)=>!values.TryGetValue(name,out var v)?null:v switch{bool b=>b,"" or "true" or "on" or "yes"=>true,"false" or "off" or "no"=>false,_=>throw Wrong(name,"true or false")};
    internal string[] List(string name)=>values.TryGetValue(name,out var v)?v as string[] ?? throw Wrong(name,"a list of texts"):[];
    internal int? Address(string name,int max=0xfffff) {
        if(!values.TryGetValue(name,out var v))return null;
        if(v is double d && d>=0 && d<=max && d==Math.Floor(d))return (int)d;
        if(v is string s) {
            s=s.Trim().ToUpperInvariant();
            foreach(string prefix in new[]{"0X","&H","&","$"})if(s.StartsWith(prefix,StringComparison.Ordinal)){s=s[prefix.Length..];break;}
            if(int.TryParse(s,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out int n) && n>=0 && n<=max)return n;
        }
        throw Wrong(name,$"an address in hex, up to {max:X}");
    }
    /// <summary>16進のバイト列（空白は読み飛ばす）。</summary>
    internal byte[] Bytes(string name) {
        try {return Convert.FromHexString(string.Concat(Need(name).Where(c=>!char.IsWhiteSpace(c))));}
        catch(FormatException){throw Wrong(name,"pairs of hex digits");}
    }
    static ArgumentException Wrong(string name,string wanted)=>new(L.T($"{name} の値が正しくありません（{wanted}）。",$"{name} must be {wanted}."));
}

// Everything the machine can be asked to do, once: the command line's commands
// and the MCP server's tools are both made from this table, so neither has
// anything the other lacks. The descriptions are English: they are read by
// programs, and the help shows them as they are.
/// <summary>操作の表。</summary>
internal sealed class Ops : IDisposable
{
    Setup setup;
    Session? session;
    /// <summary>今の本体（まだなければ起動する）。</summary>
    internal Session Machine {
        get {
            if(session is null) {
                var s=new Session(setup);
                try {s.Boot();} catch {s.Dispose();throw;}
                session=s;
            }
            return session;
        }
    }
    /// <summary>本体がもう動いているか。</summary>
    internal bool Started=>session is not null;
    internal IReadOnlyList<Op> All {get;}
    /// <param name="setup">最初に起動するときの構成（Rom が空なら探す）</param>
    internal Ops(Setup setup){this.setup=setup;All=Table();}
    public void Dispose(){session?.Dispose();session=null;}
    /// <summary>状態ファイルから始める（起動の手順は飛ばす）。</summary>
    internal void StartFrom(string state) {
        var s=new Session(setup);
        try {s.Core.Load(state);} catch {s.Dispose();throw;}
        session?.Dispose();session=s;
    }
    internal Op Find(string name)=>All.FirstOrDefault(o=>o.Name==name.Replace('-','_')) ?? throw new KeyNotFoundException(name);
    /// <summary>操作を行い、RAM カードが変わっていれば書く。</summary>
    internal Reply Do(Op op,Values v) {
        var r=op.Call(v);
        session?.SaveCard();
        return r;
    }

    static Field F(string name,Kind kind,string about,bool required=false,int place=0)=>new(name,kind,about,required,place);
    static Field Seconds(string about)=>F("seconds",Kind.Number,about);
    Reply Ended(bool ended,string text="") {
        if(!ended)return new((text+"\n"+L.T("時間内に終わりませんでした（実行中か入力待ち）。","It did not end in time (running, or waiting for input).")).TrimStart('\n'),Code:3);
        return Machine.BasicError() is string e?new((text+"\n"+L.T("BASIC のエラー: ","BASIC error: ")+e).TrimStart('\n'),Code:4):new(text);
    }
    static byte[] ReadFile(string path) {
        if(path=="-"){using var m=new MemoryStream();using var i=Console.OpenStandardInput();i.CopyTo(m);return m.ToArray();}
        var info=new FileInfo(path);
        if(!info.Exists)throw new FileNotFoundException(L.T($"ファイルが見つかりません: {path}",$"No such file: {path}"));
        if(info.Length>0x100000+BinaryTransfer.HeaderLength)throw new InvalidDataException(L.T("1MB を超えるファイルは読み込めません。","Files larger than 1MB cannot be loaded."));
        return File.ReadAllBytes(path);
    }
    // A program as given: its text, or a file (a BIN by its header or its name).
    (byte[] Data,bool Bin) Program(Values v) {
        string? text=v.Text("program"),file=v.Text("file");
        if((text is null)==(file is null))throw new ArgumentException(L.T("file か program のどちらか1つを指定してください。","Give either file or program."));
        if(text is not null)return (Encoding.UTF8.GetBytes(text),false);
        byte[] data=ReadFile(file!);
        return (data,BinaryTransfer.TryReadHeader(data,out _,out _) || file!.EndsWith(".bin",StringComparison.OrdinalIgnoreCase));
    }
    static string StatePath(Values v) {
        string path=Path.GetFullPath(v.Need("path"));
        return path.EndsWith(".e500state",StringComparison.OrdinalIgnoreCase)?path:throw new ArgumentException(L.T("ファイル名は .e500state で終わるものにしてください。","The file name must end in .e500state."));
    }
    string DriveFile(string name) {
        if(name.Length==0 || name is "." or ".." || name.IndexOfAny(['/','\\',':','\0'])>=0)throw new ArgumentException(L.T("ファイル名だけを指定してください。","Give a file name alone."));
        return Path.Combine(Machine.DriveFolder,name);
    }
    static string Hex(byte[] data,int at,string format) {
        var b=new StringBuilder();
        for(int i=0;i<data.Length;i+=16)b.Append((at+i).ToString(format)).Append(": ").Append(string.Join(" ",data.Skip(i).Take(16).Select(x=>x.ToString("X2")))).Append('\n');
        return b.ToString().TrimEnd('\n');
    }
    string Map(List<Region> regions)=>string.Join("\n",regions.Select(r=>$"{r.Start:X5}-{r.End-1:X5} {RamLayout.Size(r.Size),8}  {r.Name}"));
    string Info() {
        var s=Machine;var c=s.Core;var b=new StringBuilder();
        void Line(string name,string value)=>b.Append(name).Append(": ").Append(value).Append('\n');
        Line("ROM",$"{Path.GetFileName(c.RomPath)}, version {c.RomVersion}, {c.RomKB}KB ({(c.IsE500S?"PC-E500S":c.IsE650Family?"PC-E650/U6000":"PC-E500 family")})");
        Line("Internal RAM",$"{c.InternalKB}KB");
        Line("RAM card",c.HasCard?$"{c.CardKB}KB ({c.CardPath})":"none");
        Line("CE-140F",!s.Setup.DiskConnected?"not connected":(s.Setup.Disk.Length>0?s.Setup.Disk:"a temporary folder")+(s.Setup.DiskReadOnly?" (read only)":""));
        Line("CE-126P",c.PrinterConnected?"connected":"not connected");
        Line("Auto power off",s.Setup.AutoPowerOff?"on":"held off");
        Line("Power",s.PowerOff?"off":"on");
        if(c.Faulted)Line("CPU","stopped on an undefined instruction");
        Line("Symbols",string.Join(" ",s.Symbols()));
        if(MachineCodeArea.Read(c) is {} area)Line("Machine-code area",area.Size>0?$"{area.Start:X5}-BFBFF ({area.Size:X}h bytes)":"none");
        // Switched off, the ROM keeps its memory in another order.
        int free=s.PowerOff?-1:BasicImage.FreeBytes(c);if(free>=0)Line("Free for BASIC",$"{free} bytes");
        Line("Emulated time",$"{c.Cycles/(double)Session.CpuHz:0.0} s");
        return b.ToString().TrimEnd('\n');
    }
    static void Save(string path,byte[] data)=>File.WriteAllBytes(path,data);

    List<Op> Table()=>[
        // ---- the machine ----
        new("start","Starts a fresh machine (RAM empty) at BASIC's RUN prompt. Other operations start one by themselves; this is for a clean start, another ROM or another set-up.",[
            F("rom",Kind.Text,"ROM image file (omit to keep, or find one)"),F("ram",Kind.Integer,"Internal RAM in KB: 8, 16, 32, 64, 128, 256"),F("card",Kind.Text,"RAM card image file"),F("disk",Kind.Text,"Folder the CE-140F drive (X:) keeps its files in")],v=>{
                int ram=v.Integer("ram",setup.Ram,256);CheckRam(ram);
                var next=setup with{Rom=v.Text("rom") is string r?Roms.Find(r):setup.Rom,Ram=ram,Card=v.Text("card") ?? setup.Card,Disk=v.Text("disk") is string d?Path.GetFullPath(d):setup.Disk};
                if(next.Rom.Length==0)next=next with{Rom=Roms.Find(null)};
                // One session at a time: a card's file must not be open in two.
                session?.Dispose();session=null;
                var s=new Session(next);
                try {s.Boot();} catch {s.Dispose();throw;}
                session=s;setup=next;
                return new(Info());
            }),
        new("info","Tells the ROM, RAM, card, drive, printer, power, symbols lit, machine-code area and free memory.",[],_=>new(Info(),false)),
        new("configure","Changes the set-up. Another RAM size or card switches the machine on afresh (the internal RAM's contents stay when its size does); the rest changes at once.",[
            F("ram",Kind.Integer,"Internal RAM in KB"),F("card",Kind.Text,"RAM card image file, or \"none\" to take it out"),F("disk",Kind.Text,"Folder for the CE-140F, \"none\" to disconnect it, or \"temporary\""),
            F("disk_read_only",Kind.Flag,"Write-protect the CE-140F"),F("printer",Kind.Flag,"Connect the CE-126P printer"),F("auto_power_off",Kind.Flag,"Let the machine switch itself off when left alone (about 10 minutes)")],v=>{
                var s=Machine;var next=s.Setup;
                if(v.Has("ram")){int ram=v.Integer("ram",next.Ram,256);CheckRam(ram);next=next with{Ram=ram};}
                if(v.Text("card") is string card)next=next with{Card=card is "none" or ""?"":Path.GetFullPath(card)};
                if(v.Text("disk") is string disk)next=disk switch{"none"=>next with{DiskConnected=false},"temporary" or ""=>next with{DiskConnected=true,Disk=""},_=>next with{DiskConnected=true,Disk=Path.GetFullPath(disk)}};
                if(v.Flag("disk_read_only") is bool ro)next=next with{DiskReadOnly=ro};
                if(v.Flag("printer") is bool printer)next=next with{Printer=printer};
                if(v.Flag("auto_power_off") is bool off)next=next with{AutoPowerOff=off};
                bool again=s.Configure(next);setup=next;
                return new((again?L.T("電源を入れ直しました。","Switched on afresh.")+"\n":"")+Info(),again);
            }),
        new("reset","Presses the RESET hole: the CPU, LCD and keys start again, the data stays.",[],_=>{Machine.Reset();return new();}),
        new("all_reset","ON+RESET, which asks whether to erase everything.",[F("answer",Kind.Text,"\"yes\" erases all data, \"no\" keeps it; omit to stop at the question",place:1)],v=>{
            string? a=v.Text("answer");
            if(a is not (null or "yes" or "no"))throw new ArgumentException(L.T("answer は yes か no です。","answer must be yes or no."));
            Machine.AllReset(a is null?null:a=="yes");return new();
        }),
        new("save_state","Saves the whole machine to a file, to go back to later (the emulator with a window reads it too).",[F("path",Kind.Text,"File name ending in .e500state",true,1)],v=>{string f=StatePath(v);Machine.Core.Save(f);return new(L.T("保存しました: ","Saved ")+f,false);}),
        new("load_state","Goes back to a saved state (of the same ROM and RAM card size).",[F("path",Kind.Text,"File name ending in .e500state",true,1)],v=>{
            string f=StatePath(v);var s=Machine;s.Core.Release();
            if(!s.Core.CanLoad)throw new InvalidOperationException(Core.CannotLoad);
            s.Core.Load(f);return new();
        }),
        // ---- programs ----
        new("run","Replaces the BASIC program with this one, runs it, and shows the screen and what it printed (LPRINT). Only the last four lines of PRINT stay on the screen: use LPRINT for long output. A BIN is only loaded.",[
            F("file",Kind.Text,"Program file: BASIC text (UTF-8), one the machine saved, or a BIN; - is standard input",place:1),F("program",Kind.Text,"The program text itself, one numbered line per line"),
            F("input",Kind.Text,"Typed while it runs, one line per INPUT"),Seconds("Emulated seconds to wait for it to end (60)")],v=>{
                double seconds=v.Number("seconds",60,36000);var (data,bin)=Program(v);var s=Machine;
                if(bin)return new(s.LoadBin(data,null,true));
                s.LoadBasic(data);
                long before=s.Paper.Dropped+s.Paper.Count;
                bool ended=s.RunBasic(v.Text("input"),seconds);
                return Ended(ended,Since(s,before));
            }),
        new("load","Loads a program without running it, as the emulator's \"Load program\" does: BASIC text, one the machine saved, or a BIN with the SAVE M header.",[
            F("file",Kind.Text,"The file; - is standard input",place:1),F("program",Kind.Text,"BASIC text itself"),F("no_reserve",Kind.Flag,"Do not reserve the machine-code area for a BIN outside it")],v=>{
                var (data,bin)=Program(v);var s=Machine;
                if(bin)return new(s.LoadBin(data,null,v.Flag("no_reserve")!=true));
                s.LoadBasic(data);return new(L.T("読み込みました。","Loaded."));
            }),
        new("program","Reads the BASIC program in the machine, changing nothing.",[F("to",Kind.Text,"Write it to this file instead of showing it"),F("saved",Kind.Flag,"In the form the machine saves (with to)")],v=>{
            bool saved=v.Flag("saved")==true;string? to=v.Text("to");
            if(saved && to is null)throw new ArgumentException(L.T("saved には to が要ります。","saved needs to."));
            byte[] data=Machine.SaveBasic(saved);
            string text=saved?"":Session.FromMachine(data);
            if(to is null)return new(text.Length>0?text:L.T("（プログラムはありません）","(no program)"),false);
            Save(to,saved?data:Encoding.UTF8.GetBytes(text+"\n"));return new(L.T("保存しました: ","Saved ")+to,false);
        }),
        new("exec","Runs one line of BASIC at the RUN prompt (PRINT 12*34, LIST, RUN ...), stopping a program that is running.",[F("line",Kind.Text,"The line, without ENTER",true,1),Seconds("Emulated seconds to wait for it to end (60)")],v=>{
            var s=Machine;long before=s.Paper.Dropped+s.Paper.Count;
            bool ended=s.Exec(v.Need("line"),v.Number("seconds",60,36000));return Ended(ended,Since(s,before));
        }),
        new("reserve","Sets the machine-code area's size (0 gives it back). The program and variables stay.",[F("size",Kind.Address,"Bytes in hex",true,1)],v=>{Machine.Reserve(v.Address("size",0x40000)!.Value);return new(Info());}),
        // ---- keys and screen ----
        new("type","Types text on the keyboard as it stands (a newline is ENTER). Letters come out as capitals.",[F("text",Kind.Text,"The text",true,1),Seconds("Emulated seconds to run afterwards (0.5)")],v=>{
            var s=Machine;s.Type(v.Need("text"));s.Run(v.Number("seconds",0.5,36000));return new();
        }),
        new("key","Presses keys one after another. Names: letters, digits, ENTER SPACE BS DEL INS LEFT RIGHT UP DOWN SHIFT CTRL BASIC MENU F1-F5 (PF1-PF5) 2ndF C-CE BRK OFF and the others printed on the case. SHIFT+Q holds SHIFT with Q.",[
            F("keys",Kind.List,"The keys in order",true,1),F("hold",Kind.Number,"Emulated seconds to hold each (0 for a tap)"),Seconds("Emulated seconds to run afterwards (0.3)")],v=>{
                var keys=v.List("keys");
                if(keys.Length is 0 or >1000)throw new ArgumentException(L.T("keys は 1〜1000 個です。","keys must be 1 to 1000 names."));
                foreach(string k in keys)Session.CheckKey(k);
                double hold=v.Number("hold",0,600);var s=Machine;
                foreach(string k in keys){s.Key(k,hold);s.Run(0.1);}
                s.Run(v.Number("seconds",0.3,36000));return new();
            }),
        new("key_down","Presses keys and keeps them down (a game's keys, with wait between).",[F("keys",Kind.List,"The keys",true,1)],v=>Hold(v,true)),
        new("key_up","Lets go of keys.",[F("keys",Kind.List,"The keys, or none for all of them",place:1)],v=>{if(v.List("keys").Length>0)return Hold(v,false);Machine.Core.Release();Machine.Run(0.05);return new();}),
        new("wait","Lets the machine run for a while.",[F("seconds",Kind.Number,"Emulated seconds (1)",place:1)],v=>{Machine.Run(v.Number("seconds",1,36000));return new();}),
        new("wait_text","Lets the machine run until the screen shows a text.",[F("text",Kind.Text,"The text",true,1),Seconds("Emulated seconds at most (60)")],v=>{
            var s=Machine;string text=v.Need("text");double seconds=v.Number("seconds",60,36000);
            for(double t=0;t<seconds;t+=0.05){if(s.Screen().Contains(text,StringComparison.Ordinal))return new();s.Run(0.05);}
            return s.Screen().Contains(text,StringComparison.Ordinal)?new():new(L.T("時間内に表示されませんでした。","It did not come up in time."),Code:3);
        }),
        new("screen","Shows the LCD: the 40x4 characters read from it and the symbols lit, and its dots as a picture.",[F("picture",Kind.Flag,"Also return the dots as a PNG (MCP)"),F("png",Kind.Text,"Save the dots as a PNG file"),F("scale",Kind.Integer,"Pixels per dot, 1 to 16 (4)")],v=>{
            var s=Machine;int scale=v.Integer("scale",4,16);
            if(scale<1)throw new ArgumentException(L.T("scale は 1〜16 です。","scale must be 1 to 16."));
            byte[]? png=v.Has("png") || v.Flag("picture")==true?s.Picture(scale):null;
            if(v.Text("png") is string f)Save(f,png!);
            return new(Picture:v.Flag("picture")==true?png:null);
        }),
        // ---- sound ----
        new("record","Records the sound (BEEP) to a WAV file while the machine runs, after a line of BASIC if one is given.",[F("to",Kind.Text,"The WAV file",true,1),F("line",Kind.Text,"A line of BASIC to run first"),Seconds("Emulated seconds to record (3)")],v=>{
            string to=v.Need("to");double seconds=v.Number("seconds",3,600);var s=Machine;
            string? line=v.Text("line");
            if(line is not null)s.Prompt("RUN");
            s.Recording=[];
            try {if(line is not null)s.Type(line.TrimEnd('\r','\n')+"\n");s.Run(seconds);Save(to,Png.Wave(s.Recording));} finally {s.Recording=null;}
            return new(L.T("録音しました: ","Recorded ")+to);
        }),
        // ---- the CE-126P ----
        new("printed","Shows everything on the CE-126P printer's paper.",[F("to",Kind.Text,"Save it as a text file"),F("png",Kind.Text,"Save a picture of the paper as a PNG file")],v=>{
            var s=Machine;string text=s.Printed();
            if(v.Text("to") is string f)Save(f,Encoding.UTF8.GetBytes(text+"\n"));
            if(v.Text("png") is string p)Save(p,Png.Paper(s.Paper,s.Core.Font));
            return new(text.Length>0?text:L.T("（印字はありません）","(nothing printed)"),false);
        }),
        new("paper_cut","Cuts the CE-126P's paper: what was printed is gone.",[],_=>{Machine.Paper.Clear();return new(L.T("紙を切りました。","Cut."),false);}),
        // ---- the CE-140F ----
        new("drive_files","Lists the files on the CE-140F (X:).",[],_=>{
            var files=new DirectoryInfo(Machine.DriveFolder).GetFiles().OrderBy(f=>f.Name,StringComparer.OrdinalIgnoreCase).Select(f=>$"{f.Length,9}  {f.Name}").ToList();
            return new(files.Count>0?string.Join("\n",files):L.T("（ファイルはありません）","(no files)"),false);
        }),
        new("drive_put","Copies a file to the CE-140F (X:).",[F("from",Kind.Text,"The file",true,1),F("name",Kind.Text,"Its name there, 8.3 (as it is)",place:2)],v=>{
            string from=v.Need("from");string to=DriveFile(v.Text("name") ?? Path.GetFileName(from).ToUpperInvariant());
            Save(to,ReadFile(from));return new(L.T("置きました: X:","Copied to X:")+Path.GetFileName(to),false);
        }),
        new("drive_get","Copies a file from the CE-140F (X:).",[F("name",Kind.Text,"Its name there",true,1),F("to",Kind.Text,"Where to",true,2)],v=>{
            string from=DriveFile(v.Need("name")),to=v.Need("to");
            if(!File.Exists(from))throw new FileNotFoundException(L.T($"X: に {v.Need("name")} はありません。",$"X: has no {v.Need("name")}."));
            File.Copy(from,to,true);return new(L.T("保存しました: ","Saved ")+to,false);
        }),
        new("drive_trace","Shows the CE-140F's commands and errors.",[],_=>new(Machine.Core.DiskTrace().Replace("\r\n","\n").TrimEnd('\n'),false)),
        // ---- the RAM card ----
        new("card_create","Makes a RAM card image file (empty), for configure or --card.",[F("path",Kind.Text,"The file",true,1),F("kb",Kind.Integer,"Its size in KB: 8, 16, 32, 64, 128 or 256",true,2)],v=>{
            string path=v.Need("path");int kb=v.Integer("kb",0,256);CheckRam(kb);
            if(File.Exists(path))throw new IOException(L.T($"もうあります: {path}",$"It is there already: {path}"));
            Core.CreateCardImage(path,kb);return new(L.T("作りました: ","Made ")+path,false);
        },Plain:true),
        // ---- the debugger ----
        new("registers","Shows the CPU's registers.",[],_=>{
            var c=Machine.Core;int status=Native.e500_status(c.Handle);
            string Reg(string n,string format)=>$"{n}={c.Register(Session.Registers[n]).ToString(format)}";
            uint f=c.Register(Session.Registers["F"]);
            return new(string.Join(" ",Reg("P","X5"),Reg("S","X5"),Reg("U","X5"),Reg("X","X5"),Reg("Y","X5"),Reg("BA","X4"),Reg("I","X4"))+$" F={f:X2} (C={f&1} Z={f>>1&1})"
                +((status&1)!=0?" OFF":"")+((status&2)!=0?" HALT":"")+((status&4)!=0?" FAULT":""),false);
        }),
        new("set_register","Sets a CPU register.",[F("name",Kind.Text,"A IL BA I X Y U S P F",true,1),F("value",Kind.Address,"The value in hex",true,2)],v=>{Machine.SetRegister(v.Need("name"),v.Address("value")!.Value);return new(L.T("書きました。","Set."),false);}),
        new("memory","Shows memory in hex (ROM, RAM, the card).",[F("address",Kind.Address,"Where from, in hex",true,1),F("length",Kind.Integer,"Bytes, up to 4096 (64)",place:2)],v=>{
            int at=v.Address("address")!.Value,length=Math.Min(v.Integer("length",64,4096),0x100000-at);
            if(length<1)throw new ArgumentException(L.T("length は 1 以上です。","length must be at least 1."));
            return new(Hex(Machine.Core.Dump(at,length),at,"X5"),false);
        }),
        new("poke","Writes bytes to RAM: all of them, or none if any is not RAM.",[F("address",Kind.Address,"Where to, in hex",true,1),F("bytes",Kind.Text,"The bytes in hex, e.g. \"08 05 06\"",true,2)],v=>{
            byte[] data=v.Bytes("bytes");int at=v.Address("address")!.Value;Machine.Core.Write(at,data);return new(L.T($"{at:X5} に {data.Length} バイト書きました。",$"Wrote {data.Length} bytes at {at:X5}."),false);
        }),
        new("imem","Shows the CPU's 256 bytes of internal RAM.",[],_=>new(Hex(Machine.Core.InternalMemory(),0,"X2"),false)),
        new("poke_imem","Writes the CPU's internal RAM, 00 to EE.",[F("address",Kind.Address,"Where to, in hex",true,1),F("bytes",Kind.Text,"The bytes in hex",true,2)],v=>{
            Machine.WriteInternal(v.Address("address",0xff)!.Value,v.Bytes("bytes"));return new(L.T("書きました。","Written."),false);
        }),
        new("ram_map","Shows how the RAM (and the card) is laid out: program, variables, free space, stacks, work areas, machine-code area.",[],_=>{
            var c=Machine.Core;string text=Map(RamLayout.Internal(c));
            return new(c.HasCard?text+"\n\n"+L.T("RAM カード","RAM card")+"\n"+Map(RamLayout.Card(c)):text,false);
        }),
        new("load_bin","Writes a file's bytes to RAM at an address (any RAM), or at the one its SAVE M header names.",[F("file",Kind.Text,"The file",true,1),F("address",Kind.Address,"Where to, in hex",place:2)],v=>new(Machine.LoadBin(ReadFile(v.Need("file")),v.Address("address"),true),false)),
        new("save_bin","Saves a range of memory to a file.",[F("file",Kind.Text,"The file",true,1),F("address",Kind.Address,"Where from, in hex",true,2),F("length",Kind.Address,"Bytes in hex",true,3),F("header",Kind.Flag,"With the SAVE M header, to load back to its place")],v=>{
            Save(v.Need("file"),BinaryTransfer.Save(Machine.Core,v.Address("address")!.Value,v.Address("length",0x100000)!.Value,v.Flag("header")==true));
            return new(L.T("保存しました: ","Saved ")+v.Need("file"),false);
        }),
        new("roms","Lists the ROM images found.",[],_=>new(string.Join("\n",Roms.All()) is {Length:>0} t?t:L.T("（ROM はありません）","(no ROMs)"),false),Plain:true),
    ];
    Reply Hold(Values v,bool down) {
        foreach(string k in v.List("keys"))Session.CheckKey(k);
        foreach(string k in v.List("keys"))Machine.Hold(k,down);
        return new();
    }
    static string Since(Session s,long before) {
        int skip=(int)Math.Max(0,before-s.Paper.Dropped);
        var lines=s.Paper.Lines.Skip(skip).Select(l=>new string(l.Select(PrinterPaper.ToChar).ToArray()).TrimEnd()).ToList();
        return lines.Count==0?"":L.T("印字:","Printed:")+"\n"+string.Join("\n",lines);
    }
    internal static void CheckRam(int kb){if(!RamSizes.Sizes.Contains(kb))throw new ArgumentException(L.T("容量は 8・16・32・64・128・256 のどれかです。","The size must be 8, 16, 32, 64, 128 or 256."));}
}
