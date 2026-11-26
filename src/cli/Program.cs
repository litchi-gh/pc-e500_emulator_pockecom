// SPDX-License-Identifier: GPL-2.0-or-later
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace PokeCom;

/// <summary>pokecom：エミュレータを、画面なしでコマンドラインか MCP から使う。</summary>
internal static class Program
{
    // Options that go with any command: how the machine is made, and what is done with the end of it.
    static readonly Field[] Common=[
        new("rom",Kind.Text,"ROM image (else POKECOM_ROM, or one in a rom folder)"),new("ram",Kind.Integer,"Internal RAM in KB (32)"),
        new("card",Kind.Text,"RAM card image file"),new("disk",Kind.Text,"Folder for the CE-140F drive X: (else a temporary one)"),new("disk_read_only",Kind.Flag,"Write-protect the drive"),
        new("no_printer",Kind.Flag,"Without the CE-126P"),new("state",Kind.Text,"Go on from this state file and write it back at the end (made if missing)"),
        new("png",Kind.Text,"Save the screen at the end as a PNG"),new("json",Kind.Flag,"Answer in JSON"),new("lang",Kind.Text,"Language of the messages: ja or en"),new("help",Kind.Flag,"Show help")];
    // The machine of the script and MCP modes lives as long as they do, so these only make sense there.
    static readonly string[] Lasting=["start","key_down","key_up"];

    static int Main(string[] args) {
        Console.OutputEncoding=new UTF8Encoding(false);Console.InputEncoding=new UTF8Encoding(false);
        L.Set(L.Default);
        try {return Run(args);}
        catch(UsageException e){Console.Error.WriteLine(e.Message);return 2;}
        catch(Exception e) when (Expected(e)) {Console.Error.WriteLine(e.Message);return 1;}
    }
    static bool Expected(Exception e)=>e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or JsonException or DllNotFoundException or EntryPointNotFoundException;
    static int Run(string[] args) {
        int at=Array.FindIndex(args,a=>!a.StartsWith("--",StringComparison.Ordinal));
        // The language first, so that a complaint about the rest is in it.
        int lang=Array.IndexOf(args,"--lang");if(lang>=0 && lang+1<args.Length)L.Set(args[lang+1]);
        // An option before the command takes the word after it with it.
        while(at>0 && Common.FirstOrDefault(f=>"--"+f.Name.Replace('_','-')==args[at-1]) is {Kind:not Kind.Flag})at=Array.FindIndex(args,at+1,a=>!a.StartsWith("--",StringComparison.Ordinal));
        if(at<0){bool asked=args.Contains("--help");Console.WriteLine(Help());return asked?0:2;}
        string command=args[at];string[] rest=[..args[..at],..args[(at+1)..]];
        using var none=new Ops(new Setup(""));
        if(command=="help"){Console.WriteLine(rest.Length>0 && none.All.FirstOrDefault(o=>o.Name==rest[0].Replace('-','_')) is Op about?Help(about):Help());return 0;}
        if(command=="version"){Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3));return 0;}
        if(command is "mcp" or "script") {
            var run=new Op(command,"",command=="script"?[new("file",Kind.Text,"",true,1)]:[],_=>new());
            var (own,common)=Parse(run,rest);
            if(common.Has("help")){Console.WriteLine(Help());return 0;}
            using var ops=new Ops(Make(common));
            if(common.Text("state") is string s0 && File.Exists(s0))ops.StartFrom(s0);
            int code=command=="mcp"?Mcp.Serve(ops):Script(ops,own.Need("file"),common.Flag("json")==true);
            Finish(ops,common);
            return code;
        }
        Op op;
        try {op=none.Find(command);} catch(KeyNotFoundException){throw new UsageException(L.T($"知らないコマンドです: {command}",$"Unknown command: {command}")+"\n\n"+Help());}
        if(Lasting.Contains(op.Name))throw new UsageException(L.T($"{command} は script か mcp の中で使います。",$"{command} is for script and mcp."));
        var (values,shared)=Parse(op,rest);
        if(shared.Has("help")){Console.WriteLine(Help(op));return 0;}
        using var one=new Ops(op.Plain?new Setup(""):Make(shared));
        op=one.Find(command);
        if(!op.Plain && shared.Text("state") is string s && File.Exists(s))one.StartFrom(s);
        var reply=one.Do(op,values);
        Print(one,reply,shared.Flag("json")==true);
        Finish(one,shared);
        return reply.Code;
    }
    static Setup Make(Values v) {
        int ram=v.Integer("ram",32,256);Ops.CheckRam(ram);
        return new Setup(Roms.Find(v.Text("rom")),ram,v.Text("card") is string c?Path.GetFullPath(c):"",v.Text("disk") is string d?Path.GetFullPath(d):"",v.Flag("disk_read_only")==true,Printer:v.Flag("no_printer")!=true);
    }
    /// <summary>終わりの画面と状態を、頼まれていれば残す。</summary>
    static void Finish(Ops ops,Values common) {
        string? png=common.Text("png"),state=common.Text("state");
        if(png is not null)File.WriteAllBytes(png,ops.Machine.Picture());
        if(state is not null)ops.Machine.Core.Save(Path.GetFullPath(state));
    }
    static void Print(Ops ops,Reply r,bool json) {
        if(json) {
            var o=new JsonObject{["code"]=r.Code,["text"]=r.Text};
            if(r.Show){o["screen"]=new JsonArray(ops.Machine.Screen().Split('\n').Select(l=>(JsonNode?)l).ToArray());o["symbols"]=new JsonArray(ops.Machine.Symbols().Select(l=>(JsonNode?)l).ToArray());}
            Console.WriteLine(o.ToJsonString(new JsonSerializerOptions{Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
            return;
        }
        // What went wrong goes to the error output; the screen and the result do not mix with it.
        if(r.Show)Console.WriteLine(ops.Machine.Screen());
        if(r.Text.Length>0)(r.Code!=0?Console.Error:Console.Out).WriteLine(r.Text);
    }
    // A command a line, on one machine from the first to the last.
    static int Script(Ops ops,string file,bool json) {
        using var reader=file=="-"?new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false)):new StreamReader(file,new UTF8Encoding(false));
        int n=0;
        while(reader.ReadLine() is string line) {
            n++;
            try {
                var words=Words(line);
                if(words.Count==0)continue;
                Op op;
                try {op=ops.Find(words[0]);} catch(KeyNotFoundException){throw new UsageException(L.T($"知らないコマンドです: {words[0]}",$"Unknown command: {words[0]}"));}
                var (values,_)=Parse(op,[..words.Skip(1)],false);
                var reply=ops.Do(op,values);
                Print(ops,reply,json);
                if(reply.Code!=0)return reply.Code;
            }
            catch(Exception e) when (e is UsageException || Expected(e)) {throw new InvalidOperationException(L.T($"{n} 行目: ",$"Line {n}: ")+e.Message);}
        }
        return 0;
    }
    /// <summary>1行を語に分ける（空白区切り、"..." でまとめる、その中の "" は " 1つ、# から後は読み飛ばす）。</summary>
    internal static List<string> Words(string line) {
        var words=new List<string>();var b=new StringBuilder();bool quoted=false,any=false;
        for(int i=0;i<line.Length;i++) {
            char c=line[i];
            // "" inside quotation marks is one of them, as in BASIC files and CSV.
            if(quoted && c=='"' && i+1<line.Length && line[i+1]=='"'){b.Append('"');i++;continue;}
            if(c=='"'){quoted=!quoted;any=true;continue;}
            if(!quoted && c=='#' && !any)break;
            if(!quoted && char.IsWhiteSpace(c)){if(any){words.Add(b.ToString());b.Clear();any=false;}continue;}
            b.Append(c);any=true;
        }
        if(quoted)throw new UsageException(L.T("引用符が閉じていません。","A quotation mark is not closed."));
        if(any)words.Add(b.ToString());
        return words;
    }
    /// <summary>コマンドの引数を、操作の値と共通のオプションに分ける。</summary>
    static (Values Own,Values Common) Parse(Op op,string[] args,bool common=true) {
        var own=new List<KeyValuePair<string,object>>();var shared=new List<KeyValuePair<string,object>>();
        var places=op.Fields.Where(f=>f.Place>0).OrderBy(f=>f.Place).ToList();int place=0;var many=new List<string>();
        bool raw=false;
        for(int i=0;i<args.Length;i++) {
            string a=args[i];
            if(!raw && a=="--"){raw=true;continue;}
            if(!raw && a.StartsWith("--",StringComparison.Ordinal)) {
                string name=a[2..].Replace('-','_');
                // A command's own option wins over the common one of the same name (screen --png).
                var f=op.Fields.FirstOrDefault(x=>x.Name==name);bool mine=f is not null;
                f??=common?Common.FirstOrDefault(x=>x.Name==name):null;
                if(f is null)throw new UsageException(L.T($"知らないオプションです: {a}",$"Unknown option: {a}"));
                object value;
                if(f.Kind==Kind.Flag){value=true;if(i+1<args.Length && args[i+1] is "true" or "false" or "on" or "off" or "yes" or "no" && mine)value=args[++i];}
                else if(i+1>=args.Length)throw new UsageException(L.T($"{a} に値がありません。",$"{a} needs a value."));
                else value=f.Kind==Kind.List?args[++i].Split(' ',StringSplitOptions.RemoveEmptyEntries):Unescape(op,f,args[++i]);
                (mine?own:shared).Add(new(f.Name,value));
                continue;
            }
            if(place>=places.Count)throw new UsageException(L.T($"余分な引数です: {a}",$"One argument too many: {a}")+"\n\n"+Help(op));
            var p=places[place];
            if(p.Kind==Kind.List){many.Add(a);continue;}
            own.Add(new(p.Name,Unescape(op,p,a)));place++;
        }
        if(many.Count>0)own.Add(new(places[place].Name,many.ToArray()));
        try {
            return (new Values(op,own),new Values(new Op("pokecom","",Common,_=>new()),shared));
        } catch(ArgumentException e){throw new UsageException(e.Message+"\n\n"+Help(op));}
    }
    // Text to be typed and programs given on the command line have no other way to a new line.
    static string Unescape(Op op,Field f,string s)=>f.Name is "text" or "input" or "program" && op.Name is "type" or "run" or "load"?Unescape(s):s;
    /// <summary>\n と \\ を、改行と \ にする。</summary>
    internal static string Unescape(string s) {
        var b=new StringBuilder();
        for(int i=0;i<s.Length;i++) {
            if(s[i]=='\\' && i+1<s.Length && s[i+1] is 'n' or '\\'){b.Append(s[++i]=='n'?'\n':'\\');continue;}
            b.Append(s[i]);
        }
        return b.ToString();
    }

    // ---- help ----
    static string Usage(Op op)=>op.Name.Replace('_','-')+string.Concat(op.Fields.Where(f=>f.Place>0).OrderBy(f=>f.Place).Select(f=>{string n=f.Name+(f.Kind==Kind.List?"...":"");return f.Required?$" <{n}>":$" [{n}]";}));
    static string Help(Op op) {
        var b=new StringBuilder($"pokecom {Usage(op)}\n\n{(L.En?op.About:About(op)+"\n"+op.About)}\n");
        if(op.Fields.Length>0)b.Append('\n');
        foreach(var f in op.Fields)b.Append($"  {(f.Place>0?f.Name:"--"+f.Name.Replace('_','-')),-18} {f.About}\n");
        return b.ToString().TrimEnd('\n');
    }
    static string Help() {
        using var ops=new Ops(new Setup(""));
        var b=new StringBuilder();
        b.Append(L.T("pokecom: PC-E500 シリーズのエミュレータを、画面なしで動かします。\n\n  pokecom <コマンド> [引数] [オプション]\n","pokecom: runs a PC-E500 series machine with no window.\n\n  pokecom <command> [arguments] [options]\n"));
        b.Append(L.T("\nコマンド（くわしくは pokecom help <コマンド>）:\n","\nCommands (more with pokecom help <command>):\n"));
        foreach(var op in ops.All.Where(o=>!Lasting.Contains(o.Name)))b.Append($"  {Usage(op),-38} {About(op)}\n");
        b.Append(L.T("\n続けて動かす:\n  script <file>                          1行に1つのコマンドを、同じ本体で順に実行（- は標準入力）\n  mcp                                    MCP サーバー（標準入出力）\n","\nOn one machine throughout:\n  script <file>                          One command a line (- is standard input)\n  mcp                                    An MCP server on standard input and output\n"));
        b.Append(L.T("  これらの中では、","  In these, also: ")+string.Join(L.T("、",", "),Lasting.Select(n=>n.Replace('_','-')))+L.T(" も使えます。\n","\n"));
        b.Append(L.T("\nどのコマンドにも付けられるオプション:\n","\nOptions of every command:\n"));
        foreach(var f in Common)b.Append($"  --{f.Name.Replace('_','-'),-18} {f.About}\n");
        b.Append(L.T("\n終了コード: 0 正常、1 エラー、2 使い方の誤り、3 時間内に終わらなかった（実行中か入力待ち）、4 BASIC のエラー\nROM は含まれていません。PC の速さいっぱいで動き、普段のエミュレータの RAM や設定には触れません。\n",
            "\nExit code: 0 fine, 1 an error, 2 wrong usage, 3 not ended in time (running, or waiting for input), 4 a BASIC error\nNo ROM is included. It runs as fast as the PC goes and leaves the RAM and settings of the windowed emulator alone.\n"));
        return b.ToString().TrimEnd('\n');
    }
    /// <summary>一覧に出す1行の説明（日本語、英語）。</summary>
    static readonly Dictionary<string,(string Ja,string En)> Summary=new(){
        ["start"]=("まっさらな本体を起動し直す","a fresh machine"),
        ["info"]=("ROM・RAM・周辺機器・空きなどを表示","ROM, RAM, peripherals, free memory"),
        ["configure"]=("内蔵 RAM・カード・CE-140F・CE-126P などの設定を変える","change RAM, card, drive, printer"),
        ["reset"]=("RESET 穴（データは残る）","the RESET hole (the data stays)"),
        ["all_reset"]=("オールリセット（yes で全消去、no で残す）","ON+RESET (yes erases, no keeps)"),
        ["save_state"]=("状態を保存","save the machine"),
        ["load_state"]=("状態を読み込み","go back to a saved machine"),
        ["run"]=("プログラムを読み込んで実行（BAS のテキスト・本体の形、BIN）","load a program and run it"),
        ["load"]=("プログラム読込（実行はしない）","load a program"),
        ["program"]=("今の BASIC のプログラムを取り出す","read the BASIC program"),
        ["exec"]=("BASIC の1行を実行","run one line of BASIC"),
        ["reserve"]=("機械語エリアの大きさを決める（16進、0 で返す）","size the machine-code area (hex)"),
        ["type"]=("文字を打つ（\\n は ENTER）","type text (\\n is ENTER)"),
        ["key"]=("キーを順に押す（SHIFT+Q のように同時押しも）","press keys (SHIFT+Q together)"),
        ["key_down"]=("キーを押したままにする","hold keys down"),
        ["key_up"]=("キーを離す（省略で全部）","let go of keys (all if none)"),
        ["wait"]=("本体を進める（秒）","let it run (seconds)"),
        ["wait_text"]=("画面に文字が出るまで進める","run until a text shows"),
        ["screen"]=("液晶を表示（--png で絵を保存）","show the LCD (--png saves it)"),
        ["record"]=("音を WAV に録音","record the sound to a WAV"),
        ["printed"]=("CE-126P の紙を表示（--to・--png で保存）","show the printer paper"),
        ["paper_cut"]=("CE-126P の紙を切る","cut the paper"),
        ["drive_files"]=("CE-140F（X:）のファイル一覧","files on the drive X:"),
        ["drive_put"]=("X: へファイルを置く","copy a file to X:"),
        ["drive_get"]=("X: からファイルを取り出す","copy a file from X:"),
        ["drive_trace"]=("CE-140F の通信履歴","the drive's commands and errors"),
        ["card_create"]=("RAM カードのイメージを作る","make a RAM card image"),
        ["registers"]=("CPU のレジスタ","the CPU registers"),
        ["set_register"]=("レジスタに書く","set a register"),
        ["memory"]=("メモリを表示（16進）","show memory"),
        ["poke"]=("メモリに書く（RAM だけ）","write memory (RAM only)"),
        ["imem"]=("CPU の内部 RAM を表示","show internal RAM"),
        ["poke_imem"]=("CPU の内部 RAM に書く","write internal RAM"),
        ["ram_map"]=("RAM の並び（プログラム・変数・空きなど）","how the RAM is laid out"),
        ["load_bin"]=("BIN を番地に書く","write a BIN to memory"),
        ["save_bin"]=("メモリをファイルに書き出す","save memory to a file"),
        ["roms"]=("見つかった ROM の一覧","the ROMs found")
    };
    internal static string About(Op op){var (ja,en)=Summary[op.Name];return L.T(ja,en);}
}

/// <summary>コマンドの使い方の誤り。</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>ROM を探す場所と選び方。</summary>
internal static class Roms
{
    /// <summary>rom フォルダの候補（カレント、実行ファイルの横、利用者のデータフォルダ、開発中の app）。</summary>
    static IEnumerable<string> Folders() {
        yield return Path.Combine(Environment.CurrentDirectory,"rom");
        string here=AppContext.BaseDirectory;
        yield return Path.Combine(here,"rom");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),AppFolders.Name,"rom");
        char s=Path.DirectorySeparatorChar;
        if(here.Contains($"{s}src{s}cli{s}bin{s}",StringComparison.Ordinal))
            for(var d=new DirectoryInfo(here);d is not null;d=d.Parent)if(Directory.Exists(Path.Combine(d.FullName,"app","rom")))yield return Path.Combine(d.FullName,"app","rom");
    }
    /// <summary>フォルダの中の ROM（256KB・384KB のイメージ）。</summary>
    static List<string> In(string folder)=>[..Directory.EnumerateFiles(folder).Where(f=>new FileInfo(f).Length is 256*1024 or 256*1024+16 or 384*1024).Order(StringComparer.OrdinalIgnoreCase)];
    /// <summary>見つかった ROM。</summary>
    internal static List<string> All()=>[..Folders().Distinct().Where(Directory.Exists).SelectMany(In)];
    /// <summary>名前「PC-E500(7.2).ROM」の版（その形でなければ null）。</summary>
    static Version? E500Version(string path) {
        var m=System.Text.RegularExpressions.Regex.Match(Path.GetFileName(path),@"^PC-E500\((\d+\.\d+)\)",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success && Version.TryParse(m.Groups[1].Value,out var v)?v:null;
    }
    /// <summary>使う ROM（指定、環境変数 POKECOM_ROM、見つかったものの順）。</summary>
    internal static string Find(string? asked) {
        asked??=Environment.GetEnvironmentVariable("POKECOM_ROM") is {Length:>0} e?e:null;
        if(asked is not null)return File.Exists(asked)?asked:throw new FileNotFoundException(L.T($"ROM が見つかりません: {asked}",$"No ROM here: {asked}"));
        // The first folder with a ROM: there, the PC-E500 of the highest version, else whatever comes first.
        var roms=Folders().Where(Directory.Exists).Select(In).FirstOrDefault(r=>r.Count>0);
        return roms?.Where(f=>E500Version(f) is not null).MaxBy(E500Version) ?? roms?[0]
            ?? throw new FileNotFoundException(L.T("ROM が見つかりません。--rom か環境変数 POKECOM_ROM で指定するか、次のどこかに置いてください。","No ROM was found. Give it with --rom or POKECOM_ROM, or put one in one of these folders.")+"\n"+string.Join("\n",Folders()));
    }
}
