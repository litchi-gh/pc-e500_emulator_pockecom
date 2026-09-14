// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

internal static class Keyboard
{
    // Row/bit wiring from the PC-E500 keyboard matrix (PockEmul e500.cpp).
    internal static readonly string[][] Matrix = [
        ["TEXT","Q","MENU","A","BASIC","Z","SHIFT","CTRL"],
        ["W","E","S","D","X","C","CAPS","KANA"],
        ["R","T","F","G","V","B","SPACE","DOWN"],
        ["Y","U","H","J","N","M","UP","LEFT"],
        ["I","O","K","L",",",";","RIGHT","ENTER"],
        ["RCL","HYP","HEX","EXP","7","4","1","0"],
        ["STO","SIN","DEG","^","8","5","2","+/-"],
        ["C-CE","COS","LN","SQRT","9","6","3","."],
        ["STAT","TAN","LOG","x²","/","*","-","+"],
        [")","FSE","1/x","(","DEL","BS","INS","="],
        ["P","2ndF","F5","F4","F3","F2","F1",""],
        ["OFF","ON/BRK","","","","","",""]
    ];
    internal static readonly Dictionary<string,int> Codes=Matrix.SelectMany((r,i)=>r.Select((s,j)=>(s,n:i*8+j))).Where(x=>x.s!="").ToDictionary(x=>x.s,x=>x.n);
    internal static readonly Dictionary<Keys,string> Special=new() {
        [Keys.Enter]="ENTER",[Keys.Back]="BS",[Keys.Delete]="DEL",[Keys.Insert]="INS",
        [Keys.Left]="LEFT",[Keys.Right]="RIGHT",[Keys.Up]="UP",[Keys.Down]="DOWN",
        [Keys.Escape]="ON/BRK",[Keys.F1]="F1",[Keys.F2]="F2",[Keys.F3]="F3",[Keys.F4]="F4",[Keys.F5]="F5",
        [Keys.F6]="MENU",[Keys.F7]="BASIC",[Keys.F8]="2ndF",[Keys.ControlKey]="CTRL"
    };
    // SHIFT conversion table in supplied ROM 7.3, referenced by RAM address BFC33.
    internal static readonly Dictionary<char,string> Shifted=new() {
        ['!']="Q",['\"']="W",['#']="E",['$']="R",['%']="T",['&']="Y",['\'']="U",
        ['<']="I",['>']="O",['@']="P",['[']="A",[']']="S",['{']="D",['}']="F",
        ['\\']="G",['|']="H",['~']="J",['_']="K",['`']="M",['?']=",",[':']=";"
    };
}
