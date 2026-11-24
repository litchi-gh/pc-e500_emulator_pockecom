// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>本体のキーボードの配線と、PC のキー・文字との対応。</summary>
internal static partial class Keyboard
{
    // Row/bit wiring from the PC-E500 keyboard matrix (PockEmul e500.cpp).
    /// <summary>キーの行列（12行×8ビット）。キーの名前で並べる。</summary>
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
    /// <summary>キーの名前からキーコード（行×8＋ビット）を引く表。</summary>
    internal static readonly Dictionary<string,int> Codes=Matrix.SelectMany((r,i)=>r.Select((s,j)=>(s,n:i*8+j))).Where(x=>x.s!="").ToDictionary(x=>x.s,x=>x.n);
    // SHIFT conversion table in supplied ROM 7.3, referenced by RAM address BFC33.
    /// <summary>SHIFT を押して入力する文字と、その元のキーの対応。</summary>
    internal static readonly Dictionary<char,string> Shifted=new() {
        ['!']="Q",['\"']="W",['#']="E",['$']="R",['%']="T",['&']="Y",['\'']="U",
        ['<']="I",['>']="O",['@']="P",['[']="A",[']']="S",['{']="D",['}']="F",
        // SHIFT+M gives ` here, though no keycap shows it (the case prints none).
        ['\\']="G",['|']="H",['~']="J",['_']="K",['`']="M",['?']=",",[':']=";",
        // 5Ch and 7Ch show as ¥ and ¦ in the ROM font and on the keycaps, so those
        // type them too. Screen text read back uses \ and |, the Unicode for the
        // same bytes (as CP932 does), so it round-trips through any PC tool; the
        // printer's paper, text as printed, keeps ¥ and ¦.
        ['¥']="G",['¦']="H"
    };
}
