// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text.Json;
namespace PokeCom;

// A binding is the machine's key by its name in Keyboard.Matrix, with SHIFT+ or
// CTRL+ in front for a key pressed with one of them ("UP", "SPACE", "SHIFT+A").
/// <summary>プリセット1つ：名前と、操作（GamePad の操作の名前）ごとの本体のキー（"UP"、"SHIFT+A" など）。</summary>
internal sealed class PadPreset
{
    public string Name {get;set;}="";
    public Dictionary<string,string> Map {get;set;}=new();
    internal PadPreset Copy(string? name=null)=>new(){Name=name??Name,Map=new(Map)};
}

/// <summary>コントローラーの設定（state/controller.json に保存する）。</summary>
internal sealed class PadConfig
{
    /// <summary>コントローラーを使うか。</summary>
    public bool Enabled {get;set;}=true;
    /// <summary>ファイルがあったのに読めず、既定の設定で代わりにしたか（保存しない。ファイルには書かない）。</summary>
    [System.Text.Json.Serialization.JsonIgnore] internal bool Unread {get;set;}
    /// <summary>使うコントローラー（PadDevice.Id。"" なら最初に見つかったもの）。</summary>
    public string Device {get;set;}="";
    /// <summary>使うプリセットの名前。</summary>
    public string Active {get;set;}="";
    /// <summary>プリセットの一覧。</summary>
    public List<PadPreset> Presets {get;set;}=new();

    // A DirectInput pad's buttons are numbers, and which number sits where differs
    // from maker to maker; the window learns it by asking for each place in turn.
    /// <summary>DirectInput のパッドの、絵の位置（PadView.Positions）ごとのボタンの番号。パッドの VID:PID ごと。</summary>
    public Dictionary<string,Dictionary<string,int>> Layouts {get;set;}=new();

    /// <summary>今使うプリセット（名前が見つからなければ最初のもの）。</summary>
    internal PadPreset Current=>Presets.FirstOrDefault(p=>p.Name==Active)??Presets[0];
    internal PadConfig Copy()=>new(){Enabled=Enabled,Device=Device,Active=Active,Presets=Presets.Select(p=>p.Copy()).ToList(),Layouts=Layouts.ToDictionary(x=>x.Key,x=>new Dictionary<string,int>(x.Value))};
    /// <summary>ボタンの位置を覚える名前（joy:VID:PID[:番号] の VID:PID。XInput は null）。</summary>
    internal static string? LayoutKey(PadDevice? d)=>d is {Kind:PadKind.DirectInput} && d.Id.Split(':') is {Length:>=3} p?$"{p[1]}:{p[2]}":null;
    // Most DirectInput pads number the face buttons left, bottom, right, top and the
    // shoulders LB, RB, LT, RT. ELECOM's go X, Y, A, B: left, top, bottom, right.
    /// <summary>よくある並び（1 左・2 下・3 右・4 上、5〜8 肩、9・10 中央、11・12 スティック押し込み、13 中央下）。</summary>
    internal static Dictionary<string,int> StandardLayout()=>new(){["Left"]=1,["Bottom"]=2,["Right"]=3,["Top"]=4,["LB"]=5,["RB"]=6,["LT"]=7,["RT"]=8,["Back"]=9,["Start"]=10,["LS"]=11,["RS"]=12,["Home"]=13};
    /// <summary>パッドのボタンの位置：位置合わせしたもの、なければメーカーの並び、なければよくある並び。</summary>
    internal Dictionary<string,int> LayoutFor(PadDevice? d) {
        if(LayoutKey(d) is not string key)return StandardLayout();
        if(Layouts.TryGetValue(key,out var learned))return learned;
        var layout=StandardLayout();
        if(key.StartsWith("056E:",StringComparison.OrdinalIgnoreCase)){layout["Left"]=1;layout["Top"]=2;layout["Bottom"]=3;layout["Right"]=4;}
        return layout;
    }

    /// <summary>割り当ての文字（"SHIFT+A" など）を、本体のキーコードと一緒に押す SHIFT・CTRL（なければ -1）にする。</summary>
    /// <returns>読めたか（知らないキーなら false）</returns>
    internal static bool Parse(string binding,out int code,out int modifier) {
        modifier=-1;code=-1;string key=binding;
        if(key.StartsWith("SHIFT+",StringComparison.Ordinal) && key.Length>6){modifier=Keyboard.Codes["SHIFT"];key=key[6..];}
        else if(key.StartsWith("CTRL+",StringComparison.Ordinal) && key.Length>5){modifier=Keyboard.Codes["CTRL"];key=key[5..];}
        return Keyboard.Codes.TryGetValue(key,out code);
    }
    /// <summary>割り当てを作る（modifier は "" か SHIFT か CTRL）。</summary>
    internal static string Binding(string modifier,string key)=>modifier.Length>0?modifier+"+"+key:key;

    // Two to start from: games on the machine are driven by the cursor keys or by
    // 2/4/6/8. A DirectInput pad's buttons have no fixed layout; the first two
    // get SPACE and ENTER, as A and B do.
    /// <summary>最初に用意するプリセット（テンキー・カーソルキー。最初のものが既定）。</summary>
    internal static List<PadPreset> Builtin() {
        static PadPreset Make(string name,string up,string down,string left,string right,params (string,string)[] rest) {
            var map=new Dictionary<string,string>{
                ["Up"]=up,["Down"]=down,["Left"]=left,["Right"]=right,["LStickUp"]=up,["LStickDown"]=down,["LStickLeft"]=left,["LStickRight"]=right,
                ["A"]="SPACE",["B"]="ENTER",["Start"]="ENTER",["Back"]="ON/BRK",["Button1"]="SPACE",["Button2"]="ENTER"};
            foreach(var (k,v) in rest)map[k]=v;
            return new(){Name=name,Map=map};
        }
        // The number keys first: they are the default.
        return [
            Make(L.T("テンキー 2・4・6・8","Number keys 2/4/6/8"),"8","2","4","6",("X","5"),("Y","0"),("Button3","5"),("Button4","0")),
            Make(L.T("カーソルキー","Cursor keys"),"UP","DOWN","LEFT","RIGHT",("X","SHIFT"),("Y","2ndF")),
        ];
    }
    internal static PadConfig Defaults() {var list=Builtin();return new(){Presets=list,Active=list[0].Name};}

    /// <summary>読み込んだ値を直す：プリセットがなければ既定、名前の重なりや空の名前、知らないキーの割り当てを除く。</summary>
    internal PadConfig Repair() {
        Presets??=new();Device??="";Active??="";
        Layouts=(Layouts??new()).Where(x=>x.Key is not null && x.Value is not null).ToDictionary(x=>x.Key,x=>x.Value.Where(p=>p.Key is not null && p.Value is >=1 and <=32).ToDictionary(p=>p.Key,p=>p.Value));
        var names=new HashSet<string>();var kept=new List<PadPreset>();
        foreach(var p in Presets) {
            if(p is null || string.IsNullOrWhiteSpace(p.Name) || !names.Add(p.Name.Trim()))continue;
            p.Name=p.Name.Trim();
            p.Map=(p.Map??new()).Where(x=>x.Key is not null && x.Value is not null && Parse(x.Value,out _,out _)).ToDictionary(x=>x.Key,x=>x.Value);
            kept.Add(p);
        }
        Presets=kept.Count>0?kept:Builtin();
        if(!Presets.Any(p=>p.Name==Active))Active=Presets[0].Name;
        return this;
    }
    /// <summary>設定ファイルを読む（なければ既定。壊れていれば別名で残して既定）。</summary>
    internal static PadConfig Load(string path) {
        if(!File.Exists(path))return Defaults();
        try {return (JsonSerializer.Deserialize<PadConfig>(File.ReadAllText(path))??Defaults()).Repair();}
        catch(JsonException) {
            try {File.Move(path,path+".bad-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),true);} catch(Exception) {}
            return Defaults();
        }
        // A file that cannot be read now (locked, not synced yet) is left as it is; the emulator still starts.
        catch(Exception e) when (e is IOException or UnauthorizedAccessException) {var d=Defaults();d.Unread=true;return d;}
    }
    static readonly JsonSerializerOptions Indented=new(){WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    internal void Save(string path)=>AtomicFile.Write(path,JsonSerializer.SerializeToUtf8Bytes(this,Indented));
    internal string Json=>JsonSerializer.Serialize(this,Indented);
}
