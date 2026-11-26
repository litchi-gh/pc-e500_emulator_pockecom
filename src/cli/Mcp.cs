// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace PokeCom;

// An MCP server on stdin and stdout: one JSON-RPC message a line. One machine,
// started the first time a tool needs it and kept until the client goes. Every
// operation of the table is a tool.
/// <summary>MCP サーバー（標準入出力、1行に1つの JSON-RPC）。操作の表の全部がツールになる。</summary>
internal static class Mcp
{
    static readonly string[] Versions=["2025-06-18","2025-03-26","2024-11-05"];
    static readonly JsonSerializerOptions Plain=new(){Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};

    internal static int Serve(Ops ops) {
        using var input=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false));
        using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true,NewLine="\n"};
        while(input.ReadLine() is string line) {
            if(line.Length==0)continue;
            JsonNode? message;
            try {message=JsonNode.Parse(line);}
            catch(JsonException e){Send(output,Error(null,-32700,e.Message));continue;}
            if(message is not JsonObject o){Send(output,Error(null,-32600,"Invalid request"));continue;}
            // A notification (no id) is not answered.
            if(!o.TryGetPropertyValue("id",out var id) || id is null)continue;
            Send(output,Answer(ops,o,id.DeepClone()));
        }
        return 0;
    }
    static void Send(StreamWriter output,JsonObject message)=>output.WriteLine(message.ToJsonString(Plain));
    static JsonObject Error(JsonNode? id,int code,string text)=>new(){["jsonrpc"]="2.0",["id"]=id,["error"]=new JsonObject{["code"]=code,["message"]=text}};
    static string? Text(JsonNode? n)=>n is JsonValue v && v.TryGetValue(out string? s)?s:null;
    static JsonObject Words(string text,bool error=false) {
        var r=new JsonObject{["content"]=new JsonArray(new JsonObject{["type"]="text",["text"]=text})};
        if(error)r["isError"]=true;
        return r;
    }
    static JsonObject Answer(Ops ops,JsonObject o,JsonNode id) {
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
                ["instructions"]="Drives a SHARP PC-E500 pocket computer emulator: a 240x32 dot LCD (40x4 characters), BASIC, a key matrix, a CE-140F drive (X:) and a CE-126P printer. It runs as fast as the PC goes, so seconds are emulated time. run is the usual way to try a program. Only four lines stay on the screen: use LPRINT for long output. Letters are typed as capitals."
            });
        case "ping":return Result([]);
        case "tools/list":
            var list=new JsonArray();
            foreach(var op in ops.All)list.Add(new JsonObject{["name"]=op.Name,["description"]=op.About,["inputSchema"]=Schema(op)});
            return Result(new JsonObject{["tools"]=list});
        case "tools/call":
            string name=Text(p["name"]) ?? "";
            if(ops.All.FirstOrDefault(t=>t.Name==name) is not Op tool)return Error(id,-32602,$"Unknown tool: {name}");
            try {
                // Standard input is the client's own line here.
                if(Text((p["arguments"] as JsonObject)?["file"])=="-")throw new ArgumentException("file cannot be standard input here: give program");
                return Result(Show(ops,ops.Do(tool,Arguments(tool,p["arguments"] as JsonObject ?? []))));
            }
            // What a tool could not do is told to the caller, not a failure of the server.
            catch(Exception e) when (e is not OutOfMemoryException) {return Result(Words(e.Message,true));}
        default:return Error(id,-32601,$"Unknown method: {method}");
        }
    }
    static Values Arguments(Op op,JsonObject given) {
        var list=new List<KeyValuePair<string,object>>();
        foreach(var (name,node) in given) {
            if(node is null)continue;
            object value=node switch{
                JsonArray a=>a.Select(x=>Text(x) ?? throw new ArgumentException($"{name} must be a list of texts.")).ToArray(),
                JsonValue v when v.TryGetValue(out string? s)=>s,
                JsonValue v when v.TryGetValue(out bool b)=>b,
                JsonValue v when v.TryGetValue(out double d)=>d,
                _=>throw new ArgumentException($"{name} is not a value this tool takes.")
            };
            list.Add(new(name,value));
        }
        return new Values(op,list);
    }
    static JsonObject Show(Ops ops,Reply r) {
        var b=new StringBuilder(r.Text);
        if(r.Show) {
            if(b.Length>0)b.Append('\n');
            b.Append("Screen:\n").Append(ops.Machine.Screen()).Append("\nSymbols: ").Append(string.Join(" ",ops.Machine.Symbols()));
        }
        var result=Words(b.ToString());
        if(r.Picture is byte[] png)((JsonArray)result["content"]!).Add(new JsonObject{["type"]="image",["data"]=Convert.ToBase64String(png),["mimeType"]="image/png"});
        return result;
    }
    static JsonObject Schema(Op op) {
        var properties=new JsonObject();var required=new JsonArray();
        foreach(var f in op.Fields) {
            var p=new JsonObject{["description"]=f.About};
            switch(f.Kind) {
            case Kind.List:p["type"]="array";p["items"]=new JsonObject{["type"]="string"};break;
            case Kind.Number:p["type"]="number";break;
            case Kind.Integer:p["type"]="integer";break;
            case Kind.Flag:p["type"]="boolean";break;
            default:p["type"]="string";break;
            }
            properties[f.Name]=p;if(f.Required)required.Add(f.Name);
        }
        return new JsonObject{["type"]="object",["properties"]=properties,["required"]=required};
    }
}
