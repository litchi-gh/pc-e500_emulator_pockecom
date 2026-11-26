// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing;

namespace PokeCom;

/// <summary>
/// 本体の絵の配置：キーの位置と印字、LCD の範囲。座標は高さ400の仮想の座標で、E500 は幅744、E650/U6000 は幅860。
/// </summary>
internal static class PocketLayout
{
    /// <summary>画面のキー1つ。</summary>
    /// <param name="Id">キーの名前（Keyboard.Codes のキー）</param>
    /// <param name="Label">キートップの文字</param>
    /// <param name="Bounds">位置と大きさ（仮想の座標）</param>
    /// <param name="Shift">キーの上に印字された SHIFT の文字</param>
    /// <param name="Extra">キーの上の別の印字（PF キーの機能名、数字キーの x↔M など）</param>
    /// <param name="Style">形と色の種類（1 PF キー、2 SHIFT・2ndF、3 緑のキー、4 ENTER、5 C・CE、6 数字キー、7 RESET 穴）</param>
    /// <param name="Lower">キーの右下の印字（かなの句読点）</param>
    /// <param name="Minor">キートップの主な文字の右に小さく添える BASIC の文字（÷ の / など）</param>
    internal readonly record struct Key(string Id,string Label,RectangleF Bounds,string Shift="",string Extra="",int Style=0,string Lower="",string Minor="");
    /// <summary>本体の絵の幅（仮想の座標）。</summary>
    internal static float CanvasWidth(bool wide)=>wide?860:744;
    /// <summary>外観のキーの一覧を作る。</summary>
    /// <param name="model">外観（PC-E500 か PC-E650）</param>
    internal static List<Key> KeysOf(string model) {
        var builder=new Builder();
        if(model=="PC-E500")builder.E500();else builder.Wide();
        return builder.Keys;
    }
    /// <summary>キーの絵が届く範囲：上の印字、押したときのずれと影、右下の句読点まで。</summary>
    internal static RectangleF KeyArea(Key k)=>new(k.Bounds.X-12,k.Bounds.Y-17,k.Bounds.Width+24,k.Bounds.Height+23);
    /// <summary>LCD と表示シンボルの範囲（仮想の座標）。</summary>
    internal static RectangleF LcdRegion(bool wide)=>wide?new(70,74,444,106):new(78,86,360,96);
    /// <summary>LCD の 240×32 ドットの範囲（仮想の座標）。</summary>
    internal static RectangleF DotRegion(bool wide)=>wide?new(76,101,432,72):new(84,113,348,62);
    /// <summary>LCD のガラス（緑の面）の範囲と、上端・下端の色。</summary>
    internal static (RectangleF Rect,Color Top,Color Bottom) GlassPanel(bool wide)=>wide
        ?(new(70,76,442,104),Color.FromArgb(188,207,132),Color.FromArgb(151,178,111))
        :(new(79,89,360,94),Color.FromArgb(191,212,126),Color.FromArgb(164,187,116));
    // Printed beside a key the same on both cases, by key: the kana punctuation
    // off the lower right corner, and the BASIC character small beside ÷ and ×.
    /// <summary>両方の外観で同じ、キーに添える印字（右下のかなの句読点と、÷ × に添える BASIC の文字）。</summary>
    static readonly Dictionary<string,(string Lower,string Minor)> Marks=new() {
        ["("]=("「",""),[")"]=("」",""),[","]=("、",""),["."]=("。",""),["+"]=("・",""),
        ["/"]=("","/"),["*"]=("","*")
    };
    /// <summary>キーを並べる役。</summary>
    sealed class Builder {
        internal readonly List<Key> Keys=[];
        /// <summary>キーを1つ一覧に加える（引数は Key と同じ。右下の印字と2文字目は Marks から決まる）。</summary>
        void Add(string id,string label,float x,float y,float w=27,float h=18,string shift="",string extra="",int style=0) {
            var (lower,minor)=Marks.GetValueOrDefault(id,("",""));
            Keys.Add(new(id,label,new(x,y,w,h),shift,extra,style,lower,minor));
        }
        /// <summary>PC-E500 の外観のキーを並べる（位置は実機の写真で測った値）。</summary>
        internal void E500() {
            Add("TEXT","↕",32,207,25,16);
            string[] function=["BASIC","電卓","行列","統計","エンジニアソフトウェア"];
            for(int i=0;i<5;i++)Add("F"+(i+1),"PF"+(i+1),78+i*73,207,61,16,extra:function[i],style:1);
            Add("2ndF","2ndF",444,207,24,17,style:2);
            Add("MENU","MENU",31,239,27,18,shift:"CAL",style:3);
            Add("BASIC","BASIC",31,273,27,18,shift:"AER",style:3);
            Add("RESET","",67,273,13,18,style:7);
            Add("OFF","OFF",31,306,27,18);
            Add("ON/BRK","ON",67,306,29,18,shift:"BREAK");
            Add("SHIFT","SHIFT",30,341,37,19,style:2);
            string[] shifts=["!","\"","#","$","%","&","'","<",">","@"];
            for(int i=0;i<10;i++)Add("QWERTYUIOP"[i].ToString(),"QWERTYUIOP"[i].ToString(),78+i*36.5f,240,26,18,shifts[i]);
            // Measured on a PC-E500: all three letter rows share the 36.5 pitch, and A
            // stands a quarter key right of Q, Z three quarters.
            string[] asdf=["A","S","D","F","G","H","J","K","L"];
            string[] ashift=["[","]","{","}","¥","¦","~","_","^"];
            for(int i=0;i<9;i++)Add(asdf[i],asdf[i],87+i*36.5f,273,26,18,ashift[i]);
            // M carries no shifted legend on the real case, though SHIFT+M types `
            // by the ROM's table, which text input follows (Keyboard.Shifted).
            string[] zrow=["Z","X","C","V","B","N","M",",",";"];
            for(int i=0;i<9;i++)Add(zrow[i],zrow[i],103.5f+i*36.5f,306,27,19,i==7?"?":i==8?":":"");
            Add("STO","STO",444,240,25,18);Add("RCL","RCL",444,273,25,18,shift:"ANS");
            Add("ENTER","↵",437,308,31,50,shift:"P↔NP",style:4);
            Add("CTRL","CTRL",94,342,36,18);Add("CAPS","CAPS",139,342,29,18,shift:"小文字");
            Add("KANA","カナ",176,342,29,18,shift:"ー");Add("SPACE","SPACE",214,340,63,21);
            Add("DOWN","↓",288,340,27,21);Add("UP","↑",325,340,27,21);
            Add("LEFT","◀",362,340,27,21);Add("RIGHT","▶",399,340,27,21);
            string[][] scientific=[
                ["HYP","SIN","COS","TAN","FSE","C-CE"],
                ["HEX","DEG","LN","LOG","1/x","STAT"],
                ["EXP","^","SQRT","x²","(",")"]
            ];
            string[][] labels=[["hyp","sin","cos","tan","FSE","C-CE"],["→HEX","→DEG","ln","log","1/x","↕"],["EXP","yˣ","√","x²","(",")"]];
            string[][] legends=[["archyp","sin⁻¹","cos⁻¹","tan⁻¹","TAB","CA"],["→DEC","→D.MS","eˣ","10ˣ","→rθ","TITLE"],["π","x√y","³√","%","→xy","n!"]];
            for(int r=0;r<3;r++)for(int c=0;c<6;c++)Add(scientific[r][c],labels[r][c],493+c*39.2f,99+r*33,28,19,legends[r][c],style:r==0&&c==5?5:0);
            string[][] numeric=[["7","8","9","/","DEL"],["4","5","6","*","BS"],["1","2","3","-","INS"],["0","+/-",".","+","="]];
            string[][] nlabels=[["7","8","9","÷","DEL"],["4","5","6","×","BS"],["1","2","3","−","INS"],["0","+/−",".","+","="]];
            for(int r=0;r<4;r++)for(int c=0;c<5;c++)Add(numeric[r][c],nlabels[r][c],492+c*48.5f,214+r*40,35,25,r==3&&c==2?"DRG":"",c==4?new[]{"x↔M","RM","M+",""}[r]:"",6);
        }
        /// <summary>PC-E650/U6000 の外観のキーを並べる。</summary>
        internal void Wide() {
            Add("TEXT","↕",20,211,33,18);
            string[] function=["BASIC","電卓","行列","統計","エンジニアソフトウェア"];
            for(int i=0;i<5;i++)Add("F"+(i+1),"PF"+(i+1),75+i*88.5f,211,75,18,extra:function[i],style:1);
            Add("2ndF","2ndF",519,211,30,19,style:2);
            Add("MENU","MENU",20,247,34,20,shift:"CAL",style:3);
            Add("BASIC","BASIC",20,284,34,20,shift:"AER",style:3);
            Add("RESET","",65,284,15,20,style:7);
            Add("OFF","OFF",19,321,35,20);Add("ON/BRK","ON",63,321,36,20,shift:"BREAK");
            Add("SHIFT","SHIFT",18,359,46,21,style:2);
            string[] shifts=["!","\"","#","$","%","&","'","<",">","@"];
            for(int i=0;i<10;i++)Add("QWERTYUIOP"[i].ToString(),"QWERTYUIOP"[i].ToString(),78+i*42,247,31,20,shifts[i]);
            string[] asdf=["A","S","D","F","G","H","J","K","L"];
            string[] ashift=["[","]","{","}","¥","¦","~","_","^"];
            for(int i=0;i<9;i++)Add(asdf[i],asdf[i],90+i*42,284,31,20,ashift[i]);
            string[] zrow=["Z","X","C","V","B","N","M",",",";"];
            for(int i=0;i<9;i++)Add(zrow[i],zrow[i],111+i*42,321,32,21,i==7?"?":i==8?":":"");
            Add("STO","STO",516,247,34,20);Add("RCL","RCL",516,284,34,20,shift:"ANS");
            Add("ENTER","↵",514,320,38,58,shift:"P↔NP",style:4);
            Add("CTRL","CTRL",97,359,42,21);Add("CAPS","CAPS",147,359,37,21,shift:"小文字");
            Add("KANA","カナ",194,359,38,21,shift:"ー");Add("SPACE","SPACE",242,358,70,22);
            Add("DOWN","▼",338,358,35,22);Add("UP","▲",381,358,35,22);
            Add("LEFT","◀",424,358,35,22);Add("RIGHT","▶",467,358,35,22);
            string[][] scientific=[
                ["HYP","SIN","COS","TAN","FSE","C-CE"],
                ["HEX","DEG","LN","LOG","1/x","STAT"],
                ["EXP","^","SQRT","x²","(",")"]
            ];
            string[][] labels=[["hyp","sin","cos","tan","FSE","C-CE"],["→HEX","→DEG","ln","log","1/x","↕"],["EXP","yˣ","√","x²","(",")"]];
            string[][] legends=[["archyp","sin⁻¹","cos⁻¹","tan⁻¹","TAB","CA"],["→DEC","→D.MS","eˣ","10ˣ","→rθ","TITLE"],["π","x√y","³√","%","→xy","n!"]];
            for(int r=0;r<3;r++)for(int c=0;c<6;c++)Add(scientific[r][c],labels[r][c],574+c*43,88+r*36,30,20,legends[r][c],style:r==0&&c==5?5:0);
            string[][] numeric=[["7","8","9","/","DEL"],["4","5","6","*","BS"],["1","2","3","-","INS"],["0","+/-",".","+","="]];
            string[][] nlabels=[["7","8","9","÷","DEL"],["4","5","6","×","BS"],["1","2","3","−","INS"],["0","+/−",".","+","="]];
            for(int r=0;r<4;r++)for(int c=0;c<5;c++)Add(numeric[r][c],nlabels[r][c],580+c*52,214+r*44,38,28,r==3&&c==2?"DRG":"",c==4?new[]{"x↔M","RM","M+",""}[r]:"",6);
        }
    }
}
