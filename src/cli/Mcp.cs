// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace PokeCom;

// An MCP server on stdin and stdout: one JSON-RPC message a line. One machine,
// started the first time a tool needs it and kept until the client goes.
/// <summary>MCP サーバー（標準入出力、1行に1つの JSON-RPC）。本体は1台で、最初に使うときに起動する。</summary>
internal static class Mcp
{
    sealed record Tool(string Name,string Description,JsonObject Schema,Func<JsonObject,JsonArray> Call);
    static readonly string[] Versions=["2025-06-18","2025-03-26","2024-11-05"];
    static readonly JsonSerializerOptions Plain=new(){Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    static Session? session;
    static string? romAsked;
    static int ramAsked;

    internal static int Serve(string? rom,int ram) {
        romAsked=rom;ramAsked=ram;
        using var input=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false));
        using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true,NewLine="\n"};
        try {
            while(input.ReadLine() is string line) {
                if(line.Length==0)continue;
                JsonNode? message;
                try {message=JsonNode.Parse(line);}
                catch(JsonException e){Send(output,Error(null,-32700,e.Message));continue;}
                if(message is not JsonObject o){Send(output,Error(null,-32600,"Invalid request"));continue;}
                // A notification (no id) is not answered.
                if(!o.TryGetPropertyValue("id",out var id) || id is null)continue;
                Send(output,Answer(o,id.DeepClone()));
            }
        } finally {session?.Dispose();session=null;}
        return 0;
    }
    static void Send(StreamWriter output,JsonObject message)=>output.WriteLine(message.ToJsonString(Plain));
    static JsonObject Error(JsonNode? id,int code,string text)=>new(){["jsonrpc"]="2.0",["id"]=id,["error"]=new JsonObject{["code"]=code,["message"]=text}};
    static JsonObject Answer(JsonObject o,JsonNode id) {
        string method=Text(o["method"]) ?? "";
        var p=o["params"] as JsonObject ?? [];
        JsonObject Result(JsonObject r)=>new(){["jsonrpc"]="2.0",["id"]=id,["result"]=r};
        switch(method) {
        case "initialize":
            string asked=Text(p["protocolVersion"]) ?? "";
            return Result(new JsonObject{
                ["protocolVersion"]=Versions.Contains(asked)?asked:Versions[0],
                ["capabilities"]=new JsonObject{["tools"]=new JsonObject()},
                ["serverInfo"]=new JsonObject{["name"]="pokecom",["version"]=typeof(Mcp).Assembly.GetName().Version?.ToString(3)},
                ["instructions"]="Drives a SHARP PC-E500 pocket computer emulator: a 240x32 dot LCD (40x4 characters), BASIC, and a key matrix. It runs as fast as the PC goes, so seconds are emulated time. run_basic is the usual way to try a program; a program that prints more than four lines scrolls, so use LPRINT for long output. Letters are typed as capitals."
            });
        case "ping":return Result([]);
        case "tools/list":
            var list=new JsonArray();
            foreach(var t in Tools)list.Add(new JsonObject{["name"]=t.Name,["description"]=t.Description,["inputSchema"]=t.Schema.DeepClone()});
            return Result(new JsonObject{["tools"]=list});
        case "tools/call":
            string name=Text(p["name"]) ?? "";
            var tool=Tools.FirstOrDefault(t=>t.Name==name);
            if(tool is null)return Error(id,-32602,$"Unknown tool: {name}");
            try {return Result(new JsonObject{["content"]=tool.Call(p["arguments"] as JsonObject ?? [])});}
            // What a tool could not do is told to the caller, not a failure of the server.
            catch(Exception e) when (e is not OutOfMemoryException) {return Result(new JsonObject{["content"]=Words(e.Message),["isError"]=true});}
        default:return Error(id,-32601,$"Unknown method: {method}");
        }
    }

    // ---- arguments ----
    static string? Text(JsonNode? n)=>n is JsonValue v && v.TryGetValue(out string? s)?s:null;
    static string Str(JsonObject a,string name)=>Text(a[name]) ?? throw new ArgumentException($"{name} is missing (a string)");
    static double Num(JsonObject a,string name,double fallback,double max) {
        if(a[name] is null)return fallback;
        if(a[name] is not JsonValue v || !v.TryGetValue(out double d) || !(d>=0 && d<=max))throw new ArgumentException($"{name} must be a number from 0 to {max}");
        return d;
    }
    static bool Bool(JsonObject a,string name,bool fallback)=>a[name] is JsonValue v && v.TryGetValue(out bool b)?b:fallback;
    static int Address(JsonObject a,string name) {
        if(a[name] is JsonValue v) {
            if(v.TryGetValue(out double d) && d is >=0 and <=0xfffff && d==Math.Floor(d))return (int)d;
            if(v.TryGetValue(out string? s)) {
                s=s.Trim().ToUpperInvariant();
                foreach(string prefix in new[]{"0X","&H","&","$"})if(s.StartsWith(prefix,StringComparison.Ordinal)){s=s[prefix.Length..];break;}
                if(int.TryParse(s,System.Globalization.NumberStyles.HexNumber,null,out int n) && n is >=0 and <=0xfffff)return n;
            }
        }
        throw new ArgumentException($"{name} must be an address from 00000 to FFFFF (a hex string such as \"BE000\", or a number)");
    }
    static string StatePath(JsonObject a) {
        string path=Path.GetFullPath(Str(a,"path"));
        if(!path.EndsWith(".e500state",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("path must end in .e500state");
        return path;
    }
    static Session Machine() {
        if(session is not null)return session;
        var s=new Session(Roms.Find(romAsked),ramAsked);
        try {s.Boot();} catch {s.Dispose();throw;}
        return session=s;
    }

    // ---- results ----
    static JsonArray Words(string text)=>[new JsonObject{["type"]="text",["text"]=text}];
    static JsonArray Show(Session s,string? first=null) {
        var b=new StringBuilder();
        if(first is not null)b.Append(first).Append('\n');
        b.Append("Screen:\n").Append(s.Screen()).Append("\nSymbols: ").Append(string.Join(" ",s.Symbols()));
        return Words(b.ToString());
    }
    static string Ended(bool ended)=>ended?"Ended.":"Not ended in the time given: still running, or waiting for input.";

    // ---- tools ----
    static JsonObject Schema(string[] required,params (string Name,string Type,string About)[] fields) {
        var properties=new JsonObject();
        foreach(var (name,type,about) in fields) {
            var f=new JsonObject{["description"]=about};
            if(type=="strings"){f["type"]="array";f["items"]=new JsonObject{["type"]="string"};}else f["type"]=type;
            properties[name]=f;
        }
        var r=new JsonArray();foreach(string s in required)r.Add(s);
        return new JsonObject{["type"]="object",["properties"]=properties,["required"]=r};
    }
    static readonly Tool[] Tools=[
        new("start","Starts a fresh machine (RAM empty) and brings it to BASIC's RUN prompt. The other tools start one by themselves, so this is only for a clean start or another ROM or RAM size.",
            Schema([],("rom","string","Path of a ROM image (omit for the one found)"),("ram","integer","Internal RAM in KB: 8, 16, 32, 64, 128 or 256")),a=>{
                string? rom=Text(a["rom"]) ?? romAsked;int ram=(int)Num(a,"ram",ramAsked,256);
                var s=new Session(Roms.Find(rom),ram);
                try {s.Boot();} catch {s.Dispose();throw;}
                session?.Dispose();session=s;romAsked=rom;ramAsked=ram;
                return Show(s,$"Started: {Path.GetFileName(s.Core.RomPath)}, ROM version {s.Core.RomVersion}, {s.Core.InternalKB}KB RAM.");
            }),
        new("run_basic","Replaces the BASIC program with this one, runs it, and returns the screen and what was printed (LPRINT). Lines need line numbers.",
            Schema(["program"],("program","string","The program text, one numbered line per line"),("input","string","Typed while it runs, one line per INPUT"),("seconds","number","Emulated seconds to wait for it to end (60)"),("load_only","boolean","Load it without running")),a=>{
                var s=Machine();double seconds=Num(a,"seconds",60,3600);
                s.LoadBasic(Encoding.UTF8.GetBytes(Str(a,"program")));
                if(Bool(a,"load_only",false))return Show(s,"Loaded.");
                int before=s.Printed().Length;
                bool ended=s.RunBasic(Text(a["input"]),seconds);
                string printed=s.Printed();
                return Show(s,Ended(ended)+(printed.Length>before?"\nPrinted:\n"+printed[before..].TrimStart('\n'):""));
            }),
        new("exec_basic","Runs one line of BASIC at the RUN prompt (PRINT 12*34, LIST, RUN...), stopping a program that is running.",
            Schema(["line"],("line","string","The line, without ENTER"),("seconds","number","Emulated seconds to wait for it to end (10)")),a=>{
                var s=Machine();return Show(s,Ended(s.Exec(Str(a,"line"),Num(a,"seconds",10,3600))));
            }),
        new("type_text","Types text on the keyboard as it stands (a newline is ENTER), e.g. the answer to an INPUT. Letters come out as capitals.",
            Schema(["text"],("text","string","The text"),("seconds","number","Emulated seconds to run afterwards (0.5)")),a=>{
                var s=Machine();s.Type(Str(a,"text"));s.Run(Num(a,"seconds",0.5,3600));return Show(s);
            }),
        new("press_keys","Presses keys one after another. Names: letters, digits, ENTER SPACE BS DEL INS LEFT RIGHT UP DOWN SHIFT CTRL BASIC MENU F1-F5 (PF1-PF5) 2ndF C-CE BRK OFF ... SHIFT+Q holds SHIFT with Q.",
            Schema(["keys"],("keys","strings","The keys in order"),("hold","number","Emulated seconds to hold each (0 for a tap)"),("seconds","number","Emulated seconds to run afterwards (0.3)")),a=>{
                var keys=(a["keys"] as JsonArray)?.Select(k=>Text(k) ?? throw new ArgumentException("keys must be strings")).ToArray();
                if(keys is null || keys.Length is 0 or >1000)throw new ArgumentException("keys must be 1 to 1000 strings");
                double hold=Num(a,"hold",0,60);var s=Machine();
                foreach(string k in keys){s.Key(k,hold);s.Run(0.1);}
                s.Run(Num(a,"seconds",0.3,3600));return Show(s);
            }),
        new("wait","Lets the machine run for a while and returns the screen.",
            Schema(["seconds"],("seconds","number","Emulated seconds")),a=>{var s=Machine();s.Run(Num(a,"seconds",1,3600));return Show(s);}),
        new("screen","Returns the LCD: the 40x4 characters read from it, the symbols lit, and the dots as a picture.",
            Schema([],("picture","boolean","Also return a PNG of the dots (true)")),a=>{
                var s=Machine();var content=Show(s);
                if(Bool(a,"picture",true))content.Add(new JsonObject{["type"]="image",["data"]=Convert.ToBase64String(s.Picture()),["mimeType"]="image/png"});
                return content;
            }),
        new("printed","Returns everything printed on the CE-126P printer (LPRINT, LLIST) since the start.",Schema([]),a=>Words(Machine().Printed() is {Length:>0} t?t:"(nothing printed)")),
        new("read_memory","Reads memory as a hex dump.",
            Schema(["address"],("address","string","Start address in hex, e.g. BE000"),("length","integer","Bytes, up to 4096 (64)")),a=>{
                int at=Address(a,"address"),length=(int)Num(a,"length",64,4096);
                length=Math.Min(length,0x100000-at);
                if(length<1)throw new ArgumentException("length must be at least 1");
                var data=Machine().Core.Dump(at,length);var b=new StringBuilder();
                for(int i=0;i<data.Length;i+=16)b.Append($"{at+i:X5}: ").AppendLine(string.Join(" ",data.Skip(i).Take(16).Select(x=>x.ToString("X2"))));
                return Words(b.ToString().TrimEnd());
            }),
        new("write_memory","Writes bytes to RAM (all of them, or none if any is not RAM).",
            Schema(["address","bytes"],("address","string","Start address in hex"),("bytes","string","The bytes in hex, e.g. \"08 05 06\"")),a=>{
                byte[] data;
                try {data=Convert.FromHexString(string.Concat(Str(a,"bytes").Where(c=>!char.IsWhiteSpace(c))));}
                catch(FormatException){throw new ArgumentException("bytes must be pairs of hex digits");}
                int at=Address(a,"address");Machine().Core.Write(at,data);return Words($"Wrote {data.Length} bytes at {at:X5}.");
            }),
        new("save_state","Saves the whole machine to a file (.e500state), to go back to later.",Schema(["path"],("path","string","File name ending in .e500state")),a=>{string f=StatePath(a);Machine().Core.Save(f);return Words($"Saved {f}");}),
        new("load_state","Goes back to a saved state.",Schema(["path"],("path","string","File name ending in .e500state")),a=>{var s=Machine();s.Core.Release();s.Core.Load(StatePath(a));return Show(s,"Loaded.");}),
    ];
}
