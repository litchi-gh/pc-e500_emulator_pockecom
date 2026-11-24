// SPDX-License-Identifier: GPL-2.0-or-later
using System.Globalization;
using System.Text;
namespace PokeCom;

/// <summary>pokecom：エミュレータを、画面なしでコマンドラインか MCP から使う。</summary>
internal static class Program
{
    static int Main(string[] args) {
        Console.OutputEncoding=new UTF8Encoding(false);Console.InputEncoding=new UTF8Encoding(false);
        L.Set(L.Default);
        try {
            var a=new Arguments(args);
            if(a.Option("lang") is string lang)L.Set(lang);
            if(a.Command is "" or "help" || a.Flag("help")){Console.WriteLine(Help());return a.Command=="" && !a.Flag("help")?2:0;}
            if(a.Command=="version"){Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString(3));return 0;}
            if(a.Command=="roms"){foreach(var r in Roms.All())Console.WriteLine(r);return 0;}
            if(a.Command=="mcp"){string? rom=a.Option("rom");int ram=a.Int("ram",32);a.Done();return Mcp.Serve(rom,ram);}
            return Run(a);
        }
        catch(UsageException e){Console.Error.WriteLine(e.Message);return 2;}
        catch(Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or DllNotFoundException or EntryPointNotFoundException or DecoderFallbackException) {Console.Error.WriteLine(e.Message);return 1;}
    }
    static string Help()=>L.T("""
        pokecom: PC-E500 シリーズのエミュレータを、画面なしで動かします。

          pokecom run <prog.bas>      BASIC を読み込んで実行し、画面と印字を表示します
          pokecom exec "<BASIC の1行>" BASIC の1行を実行します（例: pokecom exec "PRINT 12*34"）
          pokecom type "<文字>"       文字を打ちます（\n は ENTER）
          pokecom key <キー>...       キーを押します（例: BASIC、ENTER、SHIFT+Q、LEFT）
          pokecom wait <秒>           本体を進めます（エミュレータの時間）
          pokecom screen              画面を表示します
          pokecom mcp                 MCP サーバーとして動きます（標準入出力）
          pokecom roms                見つかった ROM を表示します

        オプション:
          --rom <ファイル>    ROM（省略時は POKECOM_ROM、または rom フォルダの ROM）
          --ram <KB>          内蔵 RAM の容量（既定 32）
          --state <ファイル>  状態を読み込んで続きから動かし、終わったら書き戻します（なければ作ります）
          --png <ファイル>    終わったときの画面を PNG で保存します
          --input "<文字>"    run で、実行中に打つ文字（\n は ENTER）
          --seconds <秒>      実行を待つ上限（エミュレータの時間、既定 60）
          --lang ja|en        表示の言語

        終了コード: 0 正常、1 エラー、2 使い方の誤り、3 時間内に終わらなかった（実行中か入力待ち）
        ROM は含まれていません。PC の速さいっぱいで動き、普段のエミュレータの RAM や設定には触れません。
        """,
        """
        pokecom: runs a PC-E500 series machine with no window.

          pokecom run <prog.bas>      loads a BASIC program, runs it and shows the screen and printout
          pokecom exec "<BASIC>"      runs one line of BASIC (pokecom exec "PRINT 12*34")
          pokecom type "<text>"       types text (\n is ENTER)
          pokecom key <key>...        presses keys (BASIC, ENTER, SHIFT+Q, LEFT)
          pokecom wait <seconds>      lets the machine run (emulated time)
          pokecom screen              shows the screen
          pokecom mcp                 runs as an MCP server on stdin and stdout
          pokecom roms                lists the ROM images found

        Options:
          --rom <file>      the ROM (else POKECOM_ROM, or one in a rom folder)
          --ram <KB>        internal RAM (32)
          --state <file>    goes on from this state and writes it back at the end (made if missing)
          --png <file>      saves the screen at the end as a PNG
          --input "<text>"  typed while run's program runs (\n is ENTER)
          --seconds <n>     how long to wait for it to end (emulated time, 60)
          --lang ja|en      the language of the messages

        Exit code: 0 fine, 1 an error, 2 wrong usage, 3 not ended in time (running or waiting for input)
        No ROM is included. It runs as fast as the PC goes and leaves the RAM and settings of the windowed emulator alone.
        """);
    static int Run(Arguments a) {
        string? rom=a.Option("rom"),state=a.Option("state"),png=a.Option("png"),input=a.Option("input");
        int ram=a.Int("ram",32);double seconds=a.Number("seconds",60);
        string command=a.Command;var rest=a.Rest;
        a.Done();
        if(command is not ("run" or "exec" or "type" or "key" or "wait" or "screen"))throw new UsageException(L.T($"知らないコマンドです: {command}",$"Unknown command: {command}")+"\n\n"+Help());
        int wanted=command switch{"screen"=>0,"key"=>-1,_=>1};
        if(wanted>=0?rest.Count!=wanted:rest.Count==0)throw new UsageException(Help());
        byte[]? program=command=="run"?ReadProgram(rest[0]):null;
        using var s=new Session(Roms.Find(rom),ram);
        if(state is not null && File.Exists(state))s.Core.Load(state);else s.Boot();
        bool ended=true;
        switch(command) {
        case "run":s.LoadBasic(program!);ended=s.RunBasic(input is null?null:Unescape(input),seconds);break;
        case "exec":ended=s.Exec(rest[0],seconds);break;
        case "type":s.Type(Unescape(rest[0]));s.Run(0.5);break;
        case "key":foreach(string k in rest){s.Key(k);s.Run(0.1);}s.Run(0.3);break;
        case "wait":s.Run(Arguments.ParseNumber(rest[0]));break;
        }
        Console.WriteLine(s.Screen());
        if(s.Printed() is {Length:>0} printed)Console.WriteLine(L.T("--- 印字 ---","--- printed ---")+"\n"+printed);
        if(png is not null)File.WriteAllBytes(png,s.Picture());
        if(state is not null)s.Core.Save(Path.GetFullPath(state));
        if(!ended)Console.Error.WriteLine(L.T("時間内に終わりませんでした（実行中か入力待ち）。","It did not end in time (running, or waiting for input)."));
        return ended?0:3;
    }
    internal static byte[] ReadProgram(string path)=>path=="-"?Encoding.UTF8.GetBytes(Console.In.ReadToEnd()):File.ReadAllBytes(path);
    /// <summary>\n と \\ を、改行と \ にする。</summary>
    internal static string Unescape(string s) {
        var b=new StringBuilder();
        for(int i=0;i<s.Length;i++) {
            if(s[i]=='\\' && i+1<s.Length && s[i+1] is 'n' or '\\'){b.Append(s[++i]=='n'?'\n':'\\');continue;}
            b.Append(s[i]);
        }
        return b.ToString();
    }
}

/// <summary>コマンドの使い方の誤り。</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>コマンドラインの引数（コマンド、残り、--名前 値）。</summary>
internal sealed class Arguments
{
    readonly Dictionary<string,string> options=[];
    readonly HashSet<string> used=[];
    internal string Command {get;}="";
    internal List<string> Rest {get;}=[];
    internal Arguments(string[] args) {
        bool raw=false;
        for(int i=0;i<args.Length;i++) {
            string a=args[i];
            if(!raw && a=="--"){raw=true;continue;}
            if(!raw && a.StartsWith("--",StringComparison.Ordinal)) {
                string name=a[2..];
                if(name=="help"){options[name]="";continue;}
                if(i+1>=args.Length)throw new UsageException(L.T($"{a} に値がありません。",$"{a} needs a value."));
                options[name]=args[++i];continue;
            }
            if(Command.Length==0)Command=a;else Rest.Add(a);
        }
    }
    internal bool Flag(string name){used.Add(name);return options.ContainsKey(name);}
    internal string? Option(string name){used.Add(name);return options.GetValueOrDefault(name);}
    internal int Int(string name,int fallback)=>Option(name) is not string s?fallback:int.TryParse(s,CultureInfo.InvariantCulture,out int n)?n:throw new UsageException(L.T($"--{name} は数で指定してください: {s}",$"--{name} must be a number: {s}"));
    internal double Number(string name,double fallback)=>Option(name) is string s?ParseNumber(s):fallback;
    internal static double ParseNumber(string s)=>double.TryParse(s,CultureInfo.InvariantCulture,out double n) && n is >=0 and <=36000?n:throw new UsageException(L.T($"秒は 0〜36000 の数で指定してください: {s}",$"Seconds must be 0 to 36000: {s}"));
    /// <summary>知らないオプションがあれば断る。</summary>
    internal void Done() {
        used.Add("help");used.Add("lang");
        if(options.Keys.FirstOrDefault(k=>!used.Contains(k)) is string unknown)throw new UsageException(L.T($"知らないオプションです: --{unknown}",$"Unknown option: --{unknown}"));
    }
}

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
    /// <summary>見つかった ROM（256KB・384KB のイメージ）。</summary>
    internal static List<string> All() {
        var found=new List<string>();
        foreach(string folder in Folders().Distinct().Where(Directory.Exists))
            foreach(string f in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase)) {
                long size=new FileInfo(f).Length;
                if(size is 256*1024 or 256*1024+16 or 384*1024 && !found.Contains(f))found.Add(f);
            }
        return found;
    }
    /// <summary>使う ROM（指定、環境変数 POKECOM_ROM、見つかったものの順）。</summary>
    internal static string Find(string? asked) {
        asked??=Environment.GetEnvironmentVariable("POKECOM_ROM") is {Length:>0} e?e:null;
        if(asked is not null)return File.Exists(asked)?asked:throw new FileNotFoundException(L.T($"ROM が見つかりません: {asked}",$"No ROM here: {asked}"));
        var all=All();
        // The PC-E500 of the highest version, else whatever there is.
        return all.Where(f=>Path.GetFileName(f).StartsWith("PC-E500(",StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).LastOrDefault() ?? all.FirstOrDefault()
            ?? throw new FileNotFoundException(L.T("ROM が見つかりません。--rom か環境変数 POKECOM_ROM で指定するか、次のどこかに置いてください。","No ROM was found. Give it with --rom or POKECOM_ROM, or put one in one of these folders.")+"\n"+string.Join("\n",Folders()));
    }
}
