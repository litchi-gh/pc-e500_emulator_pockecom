// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PokeCom;

// Coordinates use a 400-high device space; E500 is 744 wide, E650/U6000 are 860 wide.
/// <summary>
/// 本体の絵（ケース・キー・LCD・表示シンボル）を描き、キーのクリックを知らせるコントロール。
/// 座標は高さ400の仮想の座標で、E500 は幅744、E650/U6000 は幅860。ウィンドウの大きさに合わせて拡大する。
/// ケースとキーは変化したときだけ描き直してビットマップに取っておき、LCD だけを毎回描く。
/// </summary>
internal sealed class PocketSurface : Control
{
    /// <summary>表示するエミュレーションコア（LCD と表示シンボルを読む）。</summary>
    internal Core? Core;
    /// <summary>キーがクリックされたときに、キーの Id を知らせる。</summary>
    internal event Action<string>? KeyClicked;
    // The case around the keys, or anywhere on the glass when only the screen
    // shows: a double-click there changes the window, and a drag moves it.
    /// <summary>キーのない所（画面だけの表示ではどこでも）をダブルクリックしたことを知らせる。</summary>
    internal event Action? BackgroundDoubleClicked;
    /// <summary>キーのない所から、ドラッグを始めたことを知らせる（枠のないウィンドウを動かすため）。</summary>
    internal event Action? BackgroundDragged;
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
    /// <summary>今の外観のキーの一覧。</summary>
    internal List<Key> Keys {get;}=[];
    readonly HashSet<string> pressed=[];
    readonly Bitmap lcdBitmap=new(240,32,PixelFormat.Format32bppArgb);
    readonly int[] lcdArgb=new int[240*32];
    /// <summary>濃さ（0〜255）ごとのドットの色と、それを作ったときの色。</summary>
    readonly int[] shadeArgb=new int[256];
    (int,int) shadeFor=(1,1);
    /// <summary>仮想の座標からコントロールの座標への拡大率と、左上のずれ。</summary>
    float scale=1,ox,oy;
    /// <summary>外観（PC-E500 か PC-E650）。</summary>
    string model="PC-E500";
    bool IsWide=>model!="PC-E500";
    float CanvasWidth=>IsWide?860:744;
    // Real: each dot drawn apart from its neighbours, as on the glass, rather than
    // packed edge to edge. Contrast runs 0-100, as the case's dial: 50 is the ink
    // this always had; lower fades the dots, higher darkens them until the dots
    // that are off begin to show.
    bool showDotLines;
    int contrast=DefaultContrast;
    /// <summary>コントラストの既定値（0〜100。今までの表示と同じ濃さ）。</summary>
    internal const int DefaultContrast=50;
    // The case-only window's see-through colour: dark, so the case's anti-aliased
    // rim blends toward a colour no part of it is drawn in.
    /// <summary>本体だけの表示で透ける色（ケースのどこにも使っていない暗い色）。</summary>
    internal static readonly Color SeeThrough=Color.FromArgb(31,35,37);
    /// <summary>LCD のリアル表示とコントラストを設定する。</summary>
    /// <param name="real">ドットの間をあけるか</param>
    /// <param name="value">コントラスト（0〜100、50 が既定）</param>
    internal void SetLcd(bool real,int value) {showDotLines=real;contrast=Math.Clamp(value,0,100);Invalidate();}
    /// <summary>LCD がリアル表示か。</summary>
    internal bool RealLcd=>showDotLines;
    // The afterimage (frame interpolation): every dot has a darkness of its own
    // that moves towards on or off over a few frames, as the slow liquid crystal
    // of the real glass does, instead of jumping. A program redraws its screen
    // about ten times a second; the frames between then show the dots on their
    // way, so a moving picture runs on instead of stepping. A dot gets most of the
    // way on in RiseSeconds and off in FallSeconds (the real glass is slower to
    // let go), short enough that text stays sharp to read. 25 ms and 50 ms left a
    // dot going off visible for four frames, a trail behind anything that moved;
    // now it is gone in about two, enough to hide a picture caught half redrawn.
    // 残像（フレーム補完）：ドットごとに濃さを持ち、点く・消えるを数コマかけて変える（実機の液晶の遅い反応と同じ）。
    // 以前の 25ms・50ms では消えるドットが4コマほど残り、動くものが尾を引いたので、2コマほどで消えるようにした。
    const double RiseSeconds=0.010,FallSeconds=0.020;
    bool afterimage;
    /// <summary>ドットの濃さ（0〜1）と、それを 0〜255 にしたもの（描くのはこちら）。</summary>
    readonly float[] level=new float[240*32];
    readonly byte[] shade=new byte[240*32];
    double levelAt=-1;
    readonly System.Diagnostics.Stopwatch levelClock=System.Diagnostics.Stopwatch.StartNew();
    /// <summary>LCD の残像（フレーム補完）を使うか。切り替えたら、今の絵から始め直す。</summary>
    internal bool Afterimage=>afterimage;
    /// <summary>LCD の残像（フレーム補完）を切り替える。切り替えたら、今の絵から始め直す。</summary>
    internal void SetAfterimage(bool on){if(afterimage==on)return;afterimage=on;levelAt=-1;}
    /// <summary>描いているドットの濃さ（テスト用。0〜255）。</summary>
    internal byte TestShade(int index)=>shade[index];
    /// <summary>LCD のコントラスト（0〜100）。</summary>
    internal int Contrast=>contrast;
    bool screenOnly;
    // Only the glass, the LCD and its legends, filling the control; no keys.
    /// <summary>LCD の部分だけを表示するか（キーは描かず、クリックも受けない）。</summary>
    internal bool ScreenOnly=>screenOnly;
    /// <summary>LCD の部分だけを表示するかを切り替える。</summary>
    internal void SetScreenOnly(bool value) {if(screenOnly==value)return;screenOnly=value;Arrange();Invalidate();}
    /// <summary>表示している部分の大きさ（仮想の座標）。本体全体か、LCD の部分。</summary>
    internal SizeF ShownSize=>screenOnly?LcdRegion().Size:new SizeF(CanvasWidth,400);
    /// <summary>周りの余白（ピクセル、左右と上下それぞれの合計）。画面だけの表示では 0。</summary>
    internal int Border=>screenOnly?0:12;
    /// <summary>今の拡大率（仮想の座標1あたりのピクセル）。</summary>
    internal float Zoom=>scale;
    /// <summary>外観を切り替え、キーを作り直す。</summary>
    /// <param name="value">HardwareSettings.Appearances のどれか</param>
    internal void SetModel(string value) {
        if(!HardwareSettings.Appearances.Contains(value))throw new ArgumentException("Unknown appearance");
        if(model==value)return;model=value;BuildKeys();Invalidate();
    }
    readonly Color gold=Color.FromArgb(238,204,121),green=Color.FromArgb(155,223,185);
    internal PocketSurface() {
        Dock=DockStyle.Fill;DoubleBuffered=true;BackColor=SeeThrough;TabStop=true;
        // A frame repaints only the LCD, so a new size must ask for all of it.
        ResizeRedraw=true;
        BuildKeys();
        MouseDown+=(_,e)=>{
            if(e.Button!=MouseButtons.Left)return;
            var k=KeyAt(e.Location);
            if(k is null){dragFrom=e.Location;return;}
            dragFrom=null;KeyClicked?.Invoke(k.Value.Id);Focus();
        };
        MouseUp+=(_,_)=>dragFrom=null;
        MouseMove+=(_,e)=>{
            Cursor=KeyAt(e.Location) is null?Cursors.Default:Cursors.Hand;
            // Past the system's drag distance, so that a double-click still reads as one.
            if(dragFrom is Point from && (e.Button&MouseButtons.Left)!=0 &&
               (Math.Abs(e.X-from.X)>SystemInformation.DragSize.Width/2 || Math.Abs(e.Y-from.Y)>SystemInformation.DragSize.Height/2)) {
                dragFrom=null;BackgroundDragged?.Invoke();
            }
        };
        MouseDoubleClick+=(_,e)=>{if(e.Button==MouseButtons.Left)DoubleClickAt(e.Location);};
    }
    // Without a frame the window is resized by the edges of the picture itself:
    // the case's outline, or the glass's. The surface covers the whole window,
    // so it hands those hits on to the window (HTTRANSPARENT), which answers
    // with the edge and lets Windows do the sizing.
    /// <summary>絵の端をつかんで大きさを変えられるか（枠のない表示のとき true）。</summary>
    internal bool Resizable;
    /// <summary>表示している絵の範囲（コントロールの座標）。本体だけならケースの外形、画面だけなら全体。</summary>
    internal Rectangle PictureArea() {
        if(screenOnly)return ClientRectangle;
        var r=IsWide?new RectangleF(2,2,856,394):new RectangleF(8,13,728,374);
        return Rectangle.Round(new RectangleF(ox+r.X*scale,oy+r.Y*scale,r.Width*scale,r.Height*scale));
    }
    /// <summary>点が絵のどの端にあるか（WM_NCHITTEST の HTLEFT などの値。端でなければ 0）。</summary>
    /// <param name="p">コントロールの座標</param>
    internal int EdgeAt(Point p) {
        if(!Resizable)return 0;
        var r=PictureArea();if(!r.Contains(p) || KeyAt(p) is not null)return 0;
        // A key is a key, never an edge; and the grip grows with the DPI only as far
        // as the picture's own scale allows, or at a small zoom it reached the keys.
        // キーの上は端にしない。つかめる幅は DPI で広がるが、絵の拡大率を超えない（小さく表示するとキーまで届いた）。
        int g=Math.Max(3,Math.Min(8*DeviceDpi/96,(int)(6*scale))),c=g*2;
        bool l=p.X<r.Left+g,rt=p.X>=r.Right-g,t=p.Y<r.Top+g,b=p.Y>=r.Bottom-g;
        bool lc=p.X<r.Left+c,rc=p.X>=r.Right-c,tc=p.Y<r.Top+c,bc=p.Y>=r.Bottom-c;
        if((t&&lc)||(l&&tc))return 13;
        if((t&&rc)||(rt&&tc))return 14;
        if((b&&lc)||(l&&bc))return 16;
        if((b&&rc)||(rt&&bc))return 17;
        return l?10:rt?11:t?12:b?15:0;
    }
    protected override void WndProc(ref Message m) {
        const int WM_NCHITTEST=0x84,HTTRANSPARENT=-1;
        if(m.Msg==WM_NCHITTEST && Resizable) {
            var p=PointToClient(new Point(unchecked((short)(long)m.LParam),unchecked((short)((long)m.LParam>>16))));
            if(EdgeAt(p)!=0){m.Result=HTTRANSPARENT;return;}
        }
        base.WndProc(ref m);
    }
    /// <summary>ダブルクリック：キーのない所なら BackgroundDoubleClicked を知らせる（キーの上は2回の入力のまま）。</summary>
    internal void DoubleClickAt(Point point) {if(KeyAt(point) is null)BackgroundDoubleClicked?.Invoke();}
    /// <summary>テスト用：仮想の座標の点を、コントロールの座標にする。</summary>
    internal Point TestPoint(float x,float y)=>Point.Round(new PointF(ox+x*scale,oy+y*scale));
    /// <summary>テスト用：LCD のドットの範囲（コントロールの座標）。</summary>
    internal RectangleF TestDotArea() {var r=DotRegion();return new RectangleF(ox+r.X*scale,oy+r.Y*scale,r.Width*scale,r.Height*scale);}
    /// <summary>左ボタンを押した所（キーのない所のときだけ。ドラッグの始まりを見るため）。</summary>
    Point? dragFrom;
    /// <summary>コントロールの点にあるキー（なければ null。画面だけの表示ではいつも null）。</summary>
    Key? KeyAt(Point point) {
        if(screenOnly)return null;
        var p=new PointF((point.X-ox)/scale,(point.Y-oy)/scale);
        foreach(var k in Keys)if(k.Bounds.Contains(p))return k;
        return null;
    }
    /// <summary>キーを押した見た目にする・戻す。</summary>
    internal void SetPressed(string id,bool down) {
        if(!(down?pressed.Add(id):pressed.Remove(id)))return;
        InvalidateKey(id);
    }
    /// <summary>すべてのキーを戻した見た目にする。</summary>
    internal void ReleaseKeys() {
        var was=pressed.ToArray();pressed.Clear();
        foreach(var id in was)InvalidateKey(id);
    }
    /// <summary>キーの範囲だけを描き直させる（画面だけの表示ではキーを描かないので何もしない）。</summary>
    void InvalidateKey(string id) {
        if(screenOnly)return;
        foreach(var k in Keys)if(k.Id==id){Arrange();Invalidate(DeviceArea(KeyArea(k)));return;}
    }
    /// <summary>テスト用：キーをクリックしたことにする。</summary>
    internal void TriggerForTest(string id) {if(!Keys.Any(k=>k.Id==id))throw new ArgumentException("Unknown surface key");KeyClicked?.Invoke(id);}
    /// <summary>キーを1つ一覧に加える（引数は Key と同じ。右下の印字と2文字目は Marks から決まる）。</summary>
    void Add(string id,string label,float x,float y,float w=27,float h=18,string shift="",string extra="",int style=0) {
        var (lower,minor)=Marks.GetValueOrDefault(id,("",""));
        Keys.Add(new(id,label,new(x,y,w,h),shift,extra,style,lower,minor));
    }
    // Printed beside a key the same on both cases, by key: the kana punctuation
    // off the lower right corner, and the BASIC character small beside ÷ and ×.
    /// <summary>両方の外観で同じ、キーに添える印字（右下のかなの句読点と、÷ × に添える BASIC の文字）。</summary>
    static readonly Dictionary<string,(string Lower,string Minor)> Marks=new() {
        ["("]=("「",""),[")"]=("」",""),[","]=("、",""),["."]=("。",""),["+"]=("・",""),
        ["/"]=("","/"),["*"]=("","*")
    };
    /// <summary>PC-E500 の外観のキーを並べる（位置は実機の写真で測った値）。</summary>
    void BuildKeys() {
        Keys.Clear();
        if(IsWide){BuildWideKeys();return;}
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
    void BuildWideKeys() {
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
    /// <summary>角の丸い四角形の形を作る。</summary>
    static GraphicsPath Round(RectangleF r,float radius) {
        var p=new GraphicsPath();float d=radius*2;
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    /// <summary>文字を描く。width を指定すると、その幅の中央に置き、入らなければ縮める。</summary>
    /// <param name="g">描画先</param>
    /// <param name="text">文字</param>
    /// <param name="x">左端（width 指定時は範囲の左端）</param>
    /// <param name="y">上端</param>
    /// <param name="size">文字の大きさ（ピクセル）</param>
    /// <param name="color">色</param>
    /// <param name="bold">太字にするか</param>
    /// <param name="width">範囲の幅（0 なら x から左寄せ）</param>
    // Fonts, measured widths and formats kept: a whole case made several hundred
    // fonts and measured every label each time, in virtual units that never change.
    // フォント・測った幅・書式は使い回す（ケースを描くたびに数百のフォントを作り、毎回測っていた）。
    static readonly Dictionary<(string Family,float Size,bool Bold),Font> fonts=[];
    static readonly Dictionary<(string Text,string Family,float Size,bool Bold),float> widths=[];
    static readonly StringFormat Centred=new(StringFormat.GenericTypographic){Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap};
    static Font FontOf(string family,float size,bool bold) {
        if(!fonts.TryGetValue((family,size,bold),out var font))fonts[(family,size,bold)]=font=new Font(family,size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
        return font;
    }
    static void TextAt(Graphics g,string text,float x,float y,float size,Color color,bool bold=false,float width=0) {
        string family=text.Any(c=>c>255)?"Yu Gothic UI":"Arial";
        if(!widths.TryGetValue((text,family,size,bold),out float measured))
            widths[(text,family,size,bold)]=measured=g.MeasureString(text,FontOf(family,size,bold),1000,StringFormat.GenericTypographic).Width;
        float actual=width>0&&measured>width-3?size*(width-3)/measured:size;
        var f=FontOf(family,Math.Max(4,actual),bold);
        using var b=new SolidBrush(color);
        if(width>0)g.DrawString(text,f,b,new RectangleF(x,y,width,size*1.5f),Centred);else g.DrawString(text,f,b,new PointF(x,y));
    }
    // Lettering measured off the real case and glass: the ink spans left..right and the
    // capitals top..bottom. The printing is wider than any installed face, so the text
    // is stretched to fit rather than placed by a font size; lower case and kana keep
    // their place against the capitals.
    /// <summary>実機で測った範囲に、文字の外形がぴったり収まるように描く（横に伸ばして合わせる）。</summary>
    /// <param name="g">描画先</param>
    /// <param name="text">文字</param>
    /// <param name="left">インクの左端</param>
    /// <param name="right">インクの右端</param>
    /// <param name="top">大文字の上端</param>
    /// <param name="bottom">大文字の下端（ベースライン）</param>
    /// <param name="color">色</param>
    /// <param name="bold">太字にするか</param>
    internal static void Lettering(Graphics g,string text,float left,float right,float top,float bottom,Color color,bool bold=true) {
        var key=(text,bold,left,right,top,bottom);
        if(!placed.TryGetValue(key,out var path)) {
            var (outline,ink,cap)=Outline(text,bold);
            // A face without these glyphs outlines nothing, and nothing cannot be
            // stretched to a span; GDI's own font fallback draws such text instead.
            if(ink.Width>0 && ink.Height>0 && cap.Height>0) {
                path=(GraphicsPath)outline.Clone();using var m=new Matrix();
                m.Translate(left,top);m.Scale((right-left)/ink.Width,(bottom-top)/cap.Height);m.Translate(-ink.Left,-cap.Top);
                path.Transform(m);
            }
            placed[key]=path;
        }
        if(path is null) {
            float size=(bottom-top)/0.72f;
            TextAt(g,text,left,(top+bottom)/2-size*0.75f,size,color,bold,right-left);
            return;
        }
        using var brush=new SolidBrush(color);g.FillPath(brush,path);
    }
    // Lettering where it was last placed: the same strings at the same spans on
    // every redraw of the case layer, so each is shaped and moved there once.
    static readonly Dictionary<(string,bool,float,float,float,float),GraphicsPath?> placed=[];
    // The faces, with stand-ins where Windows lacks them: a FontFamily, unlike a
    // Font, throws for a missing face, and the whole case would fail to draw.
    static readonly FontFamily Latin=Face("Arial"),Kana=Face("Yu Gothic UI","Meiryo","MS Gothic");
    /// <summary>書体を探す。どれもなければ既定の書体。</summary>
    /// <param name="names">候補の書体名（先にあるものを優先）</param>
    static FontFamily Face(params string[] names) {
        foreach(var name in names)try {return new FontFamily(name);} catch(ArgumentException) {}
        return FontFamily.GenericSansSerif;
    }
    // The case layer is redrawn on every key press, and the lettering is the same
    // few dozen strings each time, so each is outlined once, with the capitals of
    // its face, at a size from which any span scales.
    static readonly Dictionary<(string,bool),(GraphicsPath Path,RectangleF Ink,RectangleF Cap)> outlines=[];
    /// <summary>文字の外形と、インクの範囲と、その書体の大文字 H の範囲を返す（一度作ったら使い回す）。</summary>
    /// <param name="text">文字</param>
    /// <param name="bold">太字か</param>
    static (GraphicsPath Path,RectangleF Ink,RectangleF Cap) Outline(string text,bool bold) {
        if(outlines.TryGetValue((text,bold),out var known))return known;
        var family=text.Any(c=>c>255)?Kana:Latin;
        var style=bold&&family.IsStyleAvailable(FontStyle.Bold)?FontStyle.Bold:FontStyle.Regular;
        var path=new GraphicsPath();path.AddString(text,family,(int)style,100,PointF.Empty,StringFormat.GenericTypographic);
        using var capital=new GraphicsPath();capital.AddString("H",family,(int)style,100,PointF.Empty,StringFormat.GenericTypographic);
        return outlines[(text,bold)]=(path,path.GetBounds(),capital.GetBounds());
    }
    // The ink width of text against other text in the same face, for a model name
    // printed in the style of the one that was measured.
    /// <summary>text のインクの幅が、measured の何倍か（別の型番を同じ字幅で描くため）。</summary>
    static float InkRatio(string text,string measured,bool bold)=>Outline(text,bold).Ink.Width/Outline(measured,bold).Ink.Width;
    // The case and its seventy keys cost 5.4 ms to draw and only change when a key
    // goes down or the window is resized, while the LCD repaints 62 times a second.
    // Redrawing all of it every frame spent a third of the frame budget on plastic,
    // and WM_PAINT outranks the WM_TIMER that runs the machine, so a repaint landing
    // across a tick made that tick late and the audio it produced arrive in a burst.
    // Cache the chrome and blit it; only the LCD is actually drawn per frame.
    // A key going down or a legend changing patches just its own area of the
    // cache, and only the areas that changed go back to the screen: at a large
    // window size blitting the whole case took 20 ms a frame, and rebuilding it
    // for a keystroke 50 ms, longer than the tick that runs the machine.
    // ケースの絵はキャッシュし、キーや表示シンボルが変わったらその範囲だけ描き直す。画面へも変わった範囲だけを写す。
    Bitmap? chrome;
    (int Width,int Height,string Model,bool ScreenOnly) chromeFor;
    /// <summary>キャッシュの絵の表示シンボルの色（コントラストが変わったら、LCD の範囲だけ描き直す）。</summary>
    (Color Lit,Color Off) drawnColors;
    /// <summary>描き直す範囲（毎コマ作らずに使い回す）。</summary>
    readonly List<RectangleF> areas=[];
    /// <summary>キャッシュの絵に描いてある、押したキーと表示シンボルの状態。</summary>
    readonly HashSet<string> drawnPressed=[];
    (int,int,int) drawnSymbols;
    /// <summary>画面に出した LCD のドットと表示シンボル（変わったときだけ描き直すため）。</summary>
    readonly byte[] shownPixels=new byte[240*32];
    (int,int,int) shownSymbols;
    bool shownAny;
    /// <summary>ケースの絵を全部作り直した回数（テスト用）。</summary>
    internal int ChromeBuilds {get;private set;}
    /// <summary>LCD の範囲を描き直させた回数（テスト用）。</summary>
    internal int LcdRepaints {get;private set;}
    /// <summary>テスト用：次の描画でケースの絵を全部作り直させる。</summary>
    internal void TestRebuild()=>chromeFor=default;
    // The backdrop is in the cached picture: a new one (the case-only view's see-through colour) redraws it.
    protected override void OnBackColorChanged(EventArgs e){base.OnBackColorChanged(e);chromeFor=default;Invalidate();}
    /// <summary>ケースの絵の一部（キー・表示シンボル）だけを描き直した回数（テスト用）。</summary>
    internal int ChromePatches {get;private set;}
    /// <summary>キーの絵が届く範囲（仮想の座標）：上の印字、押したときのずれと影、右下の句読点まで。</summary>
    static RectangleF KeyArea(Key k)=>new(k.Bounds.X-12,k.Bounds.Y-17,k.Bounds.Width+24,k.Bounds.Height+23);
    /// <summary>仮想の座標の範囲を、それを覆うコントロールの座標の範囲にする（にじみの分を足す）。</summary>
    Rectangle DeviceArea(RectangleF r)=>Rectangle.Inflate(Rectangle.FromLTRB((int)MathF.Floor(ox+r.Left*scale),(int)MathF.Floor(oy+r.Top*scale),(int)MathF.Ceiling(ox+r.Right*scale),(int)MathF.Ceiling(oy+r.Bottom*scale)),2,2);
    /// <summary>
    /// LCD を読み直したあとに呼ぶ：ドットか表示シンボルが変わっていれば、LCD の範囲だけを描き直させる。
    /// </summary>
    /// <returns>残像のドットがまだ変わっている途中か（なら、次のコマも呼ぶ）</returns>
    internal bool UpdateLcd()=>UpdateLcd(levelClock.Elapsed.TotalSeconds);
    /// <summary>UpdateLcd の、時刻（秒）を渡す形（テストで時間を進めるため）。</summary>
    /// <param name="now">今の時刻（秒）</param>
    internal bool UpdateLcd(double now) {
        if(Core is null){Invalidate();return false;}
        bool settling=Settle(now);
        var symbols=Symbols;
        if(shownAny && symbols==shownSymbols && !offsetsChanged && shade.AsSpan().SequenceEqual(shownPixels))return settling;
        shade.CopyTo(shownPixels,0);shownSymbols=symbols;shownAny=true;
        Arrange();Invalidate(DeviceArea(LcdRegion()));LcdRepaints++;
        return settling;
    }
    /// <summary>ドットの濃さを、LCD の今のドットに向けて now までの時間だけ進める（残像を使わなければ、そのまま写す）。</summary>
    /// <returns>まだ変わっている途中のドットがあるか</returns>
    bool Settle(double now) {
        var pixels=Core!.Pixels;
        if(!afterimage || levelAt<0 || now<levelAt) {
            for(int i=0;i<pixels.Length;i++){bool on=pixels[i]!=0;level[i]=on?1:0;shade[i]=on?(byte)255:(byte)0;}
            levelAt=now;ResetMotion(pixels);return false;
        }
        double dt=now-levelAt;levelAt=now;
        float rise=(float)(1-Math.Exp(-dt/RiseSeconds)),fall=(float)(1-Math.Exp(-dt/FallSeconds));
        bool settling=Slide(now,pixels);
        for(int i=0;i<pixels.Length;i++) {
            float target=pixels[i]!=0?1:0,l=level[i];
            // A dot that moved, and the one it left, change at once: the slide carries the change.
            // A block caught half redrawn at the start of a step keeps its last picture until the rest comes.
            if(holding[i/240/8*BlockCols+i%240/8]){float was=motionBase[i];level[i]=was;shade[i]=(byte)(was*255);settling=true;continue;}
            if(snap[i]){level[i]=target;shade[i]=(byte)(target*255);continue;}
            if(l==target)continue;
            l+=(target-l)*(target>l?rise:fall);
            // Within a step of the last shade, it is there.
            if(MathF.Abs(target-l)<0.5f/255)l=target;else settling=true;
            level[i]=l;shade[i]=(byte)(l*255+0.5f);
        }
        return settling;
    }
    // Motion: a game moves a picture a dot at a time, a few times a second, and
    // the afterimage alone cannot carry it from one place to the next (a row of
    // every other dot, moved by one, would only fade to grey and back). A dot
    // that moved one up, down, left or right is drawn where it was and slid to
    // where it is over as long as its last step took, arriving as the next step
    // comes, so things moving at different speeds each keep their own. A move is
    // found two ways: an 8x8 block whose new picture is its old one moved a dot
    // (a scrolling band, where every other dot fits either way), and a dot that
    // came on beside one that went off (a star passing letters that stay put).
    // A change after a long still, or not such a move (text, a scroll of a whole
    // line), is shown as before. A redraw caught half done (the rest a frame
    // later) is one step.
    // 動きの補完：ゲームは絵を1ドットずつ、1秒に数回動かす。残像だけでは次の位置までつながらない
    // （1つおきのドットの列を1ドット動かすと、灰色になって戻るだけ）。上下左右に1ドット動いたドットは、
    // 前の位置に描いて、そのドットの前の1歩にかかった時間で新しい位置まで滑らせる（次の1歩が来るころに着く。
    // 速さの違うものもそれぞれの速さになる）。動きは2通りで見つける：8×8 の区画の新しい絵が前の絵を1ドット
    // 動かしたもの（流れる帯。1つおきのドットはどちら向きにも合う）と、消えたドットのとなりに点いたドット
    // （止まった文字の間を流れる星）。長く止まっていたあとの変化や、そうでない変化（文字、1行分のスクロール）は
    // 今までどおり。途中まで描かれ、残りが次のコマに描かれたものは1歩と数える。
    const int BlockCols=30,BlockRows=4,Dots=240*32;
    /// <summary>この時間より近い変化は同じ1歩（描き直しの途中）、この時間より長く止まったあとは動きと見ない。</summary>
    const double JoinSeconds=0.05,MaxStepSeconds=0.6;
    static readonly (sbyte X,sbyte Y)[] Moves=[(-1,0),(1,0),(0,-1),(0,1)];
    readonly byte[] motionPrev=new byte[Dots],motionBase=new byte[Dots];
    readonly double[] blockStart=new double[BlockCols*BlockRows],blockStep=new double[BlockCols*BlockRows];
    readonly sbyte[] lastSx=new sbyte[BlockCols*BlockRows],lastSy=new sbyte[BlockCols*BlockRows];
    /// <summary>ドットが点いた（動いて来た）時刻、消えた時刻、消えたドットが動きの元に使われたときの消えた時刻。</summary>
    readonly double[] dotOn=new double[Dots],dotOff=new double[Dots],usedOff=new double[Dots];
    /// <summary>点いているドットが、どちらから動いて来たか（滑り終わっても残す。次の1歩の向きの手がかり）。</summary>
    readonly sbyte[] cameX=new sbyte[Dots],cameY=new sbyte[Dots];
    /// <summary>滑らせている1歩の始まりと長さ（長さ 0 なら滑っていない）。</summary>
    readonly double[] slideStart=new double[Dots],slideStep=new double[Dots];
    // A step that comes a little early or late is not a jerk: the time a step takes is reckoned
    // from the last guess and the last step together, and what was still to go when the next
    // step came is carried into it (up to a dot), so the speed runs on.
    // 1歩が少し早く・遅く来てもガクッとさせない：1歩の時間は前の見込みと今の1歩の平均で見込み、次の1歩が来たときに
    // 残っていた分（1ドットまで）を次の1歩に足して、同じ速さで滑り続ける。
    /// <summary>ドットの1歩の見込みの時間と、滑らせる長さ（ドット。1＋持ち越し）。</summary>
    readonly double[] dotPace=new double[Dots];
    readonly float[] slideDist=new float[Dots],remain=new float[Dots];
    readonly double[] blockPace=new double[BlockCols*BlockRows];
    /// <summary>このコマで変わった・動いた・ドットの組が見つかった区画（Slide の中で使う）。</summary>
    readonly bool[] blockChanged=new bool[BlockCols*BlockRows],blockMoved=new bool[BlockCols*BlockRows],blockPaired=new bool[BlockCols*BlockRows];
    /// <summary>このコマで、濃さを残像なしで今のドットにするか。</summary>
    readonly bool[] snap=new bool[Dots];
    // A game redraws a moved thing over a frame or two; caught half done, the block is no move yet
    // and its new dots would show at once, then slide back from where they were once the rest came.
    // So a block that steps in rhythm but is not (yet) a move keeps its last picture until the step
    // is a move or JoinSeconds have gone.
    // ゲームは動かしたものを1〜2コマかけて描く。途中のコマでは区画が動きと認められず、新しいドットがすぐ出て、残りが
    // 描かれると前の位置から滑り直していた。リズムよく変わる区画が動きと認められないうちは、動きになるか JoinSeconds が
    // 過ぎるまで前の絵のままにする。
    /// <summary>区画を前の絵のままにしているか。</summary>
    readonly bool[] holding=new bool[BlockCols*BlockRows];
    /// <summary>滑っているドットと、描く位置のずれ（ドット）。</summary>
    readonly List<(int Index,float X,float Y)> slides=[];
    /// <summary>ずれが前の呼び出しから変わったか（なら描き直す）。</summary>
    bool offsetsChanged;
    /// <summary>滑っているドットがあるか。</summary>
    bool Moving=>slides.Count>0;
    /// <summary>滑っているドットの数（テスト用）。</summary>
    internal int TestSliding=>slides.Count;
    /// <summary>今の絵から、動きなしで始め直す。</summary>
    void ResetMotion(byte[] pixels) {
        for(int i=0;i<pixels.Length;i++)motionPrev[i]=pixels[i]!=0?(byte)1:(byte)0;
        offsetsChanged=slides.Count>0;slides.Clear();
        Array.Fill(blockStart,double.MinValue);Array.Clear(blockStep);Array.Clear(blockPace);Array.Clear(dotPace);Array.Clear(remain);
        Array.Fill(dotOn,double.MinValue);Array.Fill(dotOff,double.MinValue);Array.Fill(usedOff,double.NaN);
        Array.Clear(cameX);Array.Clear(cameY);Array.Clear(slideStep);Array.Clear(snap);Array.Clear(holding);
    }
    /// <summary>ドット i を、向き (x, y) の1歩として start から step 秒かけて滑らせる。</summary>
    /// <param name="from">前にいたドット（その残りを持ち越す。なければ -1）</param>
    void StartSlide(int i,sbyte x,sbyte y,double start,double step,int from) {
        float carry=from>=0 && remainX[from]==x && remainY[from]==y?remain[from]:0;
        cameX[i]=x;cameY[i]=y;slideStart[i]=start;slideStep[i]=step;dotPace[i]=step;slideDist[i]=1+Math.Min(1,carry);dotOn[i]=start;snap[i]=true;
    }
    /// <summary>新しい1歩の前に、まだ滑っていたドットの残り（ドット）と向き。</summary>
    readonly sbyte[] remainX=new sbyte[Dots],remainY=new sbyte[Dots];
    /// <summary>前の見込みと今の1歩から、次の1歩の時間を見込む（大きく違えば今の1歩）。</summary>
    static double Pace(double guess,double gap)=>guess>0 && gap>guess*0.6 && gap<guess*1.6?(guess+gap)/2:gap;
    /// <summary>新しい1歩を見つけ、滑らせているドットのずれを now まで進める。</summary>
    /// <returns>まだ滑っているドットがあるか</returns>
    bool Slide(double now,byte[] pixels) {
        Array.Clear(snap);
        bool[] changed=blockChanged,moved=blockMoved,paired=blockPaired;Array.Clear(changed);Array.Clear(moved);Array.Clear(paired);bool any=false;
        for(int b=0;b<changed.Length;b++) {
            int x0=b%BlockCols*8,y0=b/BlockCols*8;
            for(int y=y0;y<y0+8 && !changed[b];y++)for(int x=x0;x<x0+8;x++)if((pixels[y*240+x]!=0?1:0)!=motionPrev[y*240+x]){changed[b]=true;break;}
            // A block held for the rest of its step is looked at again, changed or not:
            // a neighbour may have become a move it goes along with.
            // 前の絵のまま待っている区画は、変わっていなくても見直す（となりが動きになり、一緒に動けることがある）。
            if(!changed[b]){if(holding[b] && now-blockStart[b]<JoinSeconds){changed[b]=true;any=true;}continue;}
            any=true;
            // A new step: the picture before it is what the move is found from.
            if(now-blockStart[b]>=JoinSeconds) {
                double step=now-blockStart[b];
                blockStep[b]=step<=MaxStepSeconds?step:0;blockStart[b]=now;blockPace[b]=blockStep[b]>0?Pace(blockPace[b],step):0;
                for(int y=y0;y<y0+8;y++)Array.Copy(motionPrev,y*240+x0,motionBase,y*240+x0,8);
            }
        }
        if(any) {
            // What each sliding dot still had to go, before this step ends the slides.
            // Taken when a block's step starts; the frames that finish a half-drawn step keep it.
            // 区画の1歩が始まるコマで取る（描きかけの1歩を描き終えるコマでは、取ったものを使う）。
            for(int i=0;i<pixels.Length;i++) {
                if(blockStart[i/240/8*BlockCols+i%240/8]!=now)continue;
                remain[i]=0;remainX[i]=cameX[i];remainY[i]=cameY[i];
                if(slideStep[i]>0)remain[i]=slideDist[i]*(float)Math.Clamp(1-(now-slideStart[i])/slideStep[i],0,1);
            }
            for(int i=0;i<pixels.Length;i++) {
                int v=pixels[i]!=0?1:0;if(v==motionPrev[i])continue;
                if(v!=0){dotOn[i]=now;cameX[i]=0;cameY[i]=0;}else dotOff[i]=now;
                slideStep[i]=0;
            }
            // The picture before this step at a dot: the step's start for a block in one, else the last frame.
            int Old(int x,int y) {
                if(x<0 || x>=240 || y<0 || y>=32)return 0;
                int b=y/8*BlockCols+x/8;return now-blockStart[b]<JoinSeconds?motionBase[y*240+x]:motionPrev[y*240+x];
            }
            Span<int> errors=stackalloc int[4];
            for(int b=0;b<changed.Length;b++) {
                if(!changed[b] || blockStep[b]<=0)continue;
                int x0=b%BlockCols*8,y0=b/BlockCols*8,differ=0;errors.Clear();
                for(int y=y0;y<y0+8;y++)for(int x=x0;x<x0+8;x++) {
                    int v=pixels[y*240+x]!=0?1:0;
                    if(v!=motionBase[y*240+x])differ++;
                    for(int m=0;m<4;m++)if(v!=Old(x-Moves[m].X,y-Moves[m].Y))errors[m]++;
                }
                // Most of the change must be the move. A row of every other dot fits a move either way: the block's last direction wins a tie.
                int best=-1;
                for(int m=0;m<4;m++)if(best<0 || errors[m]<errors[best] || errors[m]==errors[best] && Moves[m]==(lastSx[b],lastSy[b]))best=m;
                if(differ<2 || errors[best]*4>differ)continue;
                MoveBlock(b,Moves[best].X,Moves[best].Y,blockStart[b],blockPace[b]);
            }
            // A block the moving thing has only just reached was still before, so it has no
            // step of its own yet: it moves with a neighbour that moved this way, if that
            // move is what changed in it (else the thing's front would stand while the rest slid).
            // 動くものが入ったばかりの区画は、それまで止まっていたので自分の1歩がない。となりの区画が動いた向きで
            // 変化が説明できれば、となりと一緒に滑らせる（そうしないと、先頭だけが止まって残りが滑る）。
            for(int b=0;b<changed.Length;b++) {
                if(!changed[b] || moved[b])continue;
                int x0=b%BlockCols*8,y0=b/BlockCols*8,col=b%BlockCols;
                foreach(int nb in (int[])[col>0?b-1:-1,col<BlockCols-1?b+1:-1,b-BlockCols,b+BlockCols]) {
                    if(nb<0 || nb>=changed.Length || !moved[nb] || nb==b)continue;
                    sbyte sx=lastSx[nb],sy=lastSy[nb];int differ=0,wrong=0;
                    for(int y=y0;y<y0+8;y++)for(int x=x0;x<x0+8;x++) {
                        int v=pixels[y*240+x]!=0?1:0;
                        if(v!=motionBase[y*240+x])differ++;
                        if(v!=Old(x-sx,y-sy))wrong++;
                    }
                    if(differ<1 || wrong*4>differ)continue;
                    MoveBlock(b,sx,sy,blockStart[nb],blockPace[nb]);break;
                }
            }
            /// <summary>区画 b の1歩を、向き (sx, sy) の動きとして滑らせる。</summary>
            void MoveBlock(int b,sbyte sx,sbyte sy,double start,double pace) {
                int x0=b%BlockCols*8,y0=b/BlockCols*8;
                lastSx[b]=sx;lastSy[b]=sy;moved[b]=true;
                for(int y=y0;y<y0+8;y++)for(int x=x0;x<x0+8;x++) {
                    int i=y*240+x;snap[i]=true;
                    // A dot that came along with the move slides; so does one that is new in
                    // this step (the band's newest dot, entering at its end), so it does not
                    // stand at its place beside its neighbour still on the way. A dot that
                    // stays (lit before and now, with nothing behind it) does not move.
                    // 動きと一緒に来たドットは滑らせる。この1歩で新しく点いたドット（帯の端に入ってくる新しいドット）も
                    // 同じく滑らせる（途中のとなりのドットの横に、先に着いて並ばないように）。前から点いていて、
                    // 後ろに何もないドット（止まったもの）は動かさない。
                    if(pixels[i]==0 || Old(x-sx,y-sy)==0 && motionBase[i]!=0)continue;
                    int behind=x-sx>=0 && x-sx<240 && y-sy>=0 && y-sy<32?(y-sy)*240+x-sx:-1;
                    StartSlide(i,sx,sy,start,pace,behind);
                    int qx=x-sx,qy=y-sy;
                    if(qx>=0 && qx<240 && qy>=0 && qy<32 && pixels[qy*240+qx]==0)usedOff[qy*240+qx]=dotOff[qy*240+qx];
                }
            }
            // Single dots: one that came on lately beside one that went off lately, not yet taken.
            for(int i=0;i<pixels.Length;i++) {
                if(pixels[i]==0 || now-dotOn[i]>=JoinSeconds || slideStep[i]>0 || cameX[i]!=0 || cameY[i]!=0)continue;
                int x=i%240,y=i/240;
                if(moved[y/8*BlockCols+x/8])continue;
                int found=-1,count=0;double start=0,step=0;bool agrees=false;
                for(int m=0;m<4;m++) {
                    int qx=x-Moves[m].X,qy=y-Moves[m].Y;
                    if(qx<0 || qx>=240 || qy<0 || qy>=32)continue;
                    int q=qy*240+qx;
                    if(pixels[q]!=0 || now-dotOff[q]>=JoinSeconds || usedOff[q]==dotOff[q])continue;
                    // The step is from when the dot it left got there.
                    double s=Math.Min(dotOn[i],dotOff[q]),gap=s-dotOn[q];
                    if(gap<JoinSeconds || gap>MaxStepSeconds)continue;
                    count++;
                    // Two ways to have come: the way that dot came before.
                    bool same=Moves[m]==(cameX[q],cameY[q]);
                    if(found<0 || same){found=m;start=s;step=Pace(dotPace[q],gap);agrees=same;}
                }
                if(found<0 || count>1 && !agrees)continue;
                int from=(y-Moves[found].Y)*240+x-Moves[found].X;
                StartSlide(i,Moves[found].X,Moves[found].Y,start,step,from);usedOff[from]=dotOff[from];snap[from]=true;paired[y/8*BlockCols+x/8]=true;
            }
            for(int b=0;b<changed.Length;b++)if(changed[b] && !moved[b] && !paired[b] && blockStep[b]>0 && now-blockStart[b]<JoinSeconds)holding[b]=true;
            // A block still before, beside one held or moving (a moving thing's front reaching
            // it in a half-drawn frame), waits with it rather than showing its part at once.
            // 止まっていた区画も、となりが待っているか動いているなら一緒に待つ（描きかけのコマで、動くものの先頭が入ってきたとき）。
            for(int b=0;b<changed.Length;b++) {
                if(!changed[b] || moved[b] || paired[b] || holding[b] || now-blockStart[b]>=JoinSeconds)continue;
                int col=b%BlockCols;
                foreach(int nb in (int[])[col>0?b-1:-1,col<BlockCols-1?b+1:-1,b-BlockCols,b+BlockCols])
                    if(nb>=0 && nb<holding.Length && (holding[nb] || moved[nb])){holding[b]=true;break;}
            }
            for(int i=0;i<pixels.Length;i++)motionPrev[i]=pixels[i]!=0?(byte)1:(byte)0;
        }
        for(int b=0;b<holding.Length;b++)if(holding[b] && (moved[b] || paired[b] || now-blockStart[b]>=JoinSeconds))holding[b]=false;
        bool was=slides.Count>0;slides.Clear();
        for(int i=0;i<pixels.Length;i++) {
            if(slideStep[i]<=0)continue;
            double f=(now-slideStart[i])/slideStep[i];
            if(pixels[i]==0 || f>=1){slideStep[i]=0;continue;}
            float left=(float)(1-Math.Max(0,f));
            slides.Add((i,-cameX[i]*left*slideDist[i],-cameY[i]*left*slideDist[i]));snap[i]=true;
        }
        offsetsChanged=was || slides.Count>0;
        return slides.Count>0;
    }
    /// <summary>今の大きさに合わせて、拡大率と左上のずれを決める（描画の前と、大きさが変わったとき）。</summary>
    internal void Arrange() {
        if(screenOnly) {
            // The glass fills the control, the case around it falling outside.
            var region=LcdRegion();
            scale=Math.Max(0.01f,Math.Min(Width/region.Width,Height/region.Height));
            ox=(Width-region.Width*scale)/2-region.X*scale;oy=(Height-region.Height*scale)/2-region.Y*scale;
        } else {
            scale=Math.Max(0.01f,Math.Min((Width-12)/CanvasWidth,(Height-12)/400f));ox=(Width-CanvasWidth*scale)/2;oy=(Height-400*scale)/2;
        }
    }
    protected override void OnResize(EventArgs e) {base.OnResize(e);Arrange();}
    /// <summary>描画：拡大率を決め、ケース・キー・表示シンボルの絵が古ければ作り直し、その上に LCD を描く。</summary>
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);var g=e.Graphics;
        Arrange();
        var colors=LcdColors();
        var symbols=Symbols;
        // The colours are not part of the layout: a contrast step changed them, and
        // rebuilt the whole case for each step of the slider. They only colour the
        // legends, so a new contrast patches the LCD's area as a legend does.
        // 色はレイアウトに含めない（コントラストを動かすたびにケース全体を作り直していた）。色は表示シンボルにしか使わないので、LCD の範囲だけ直す。
        var want=(Width,Height,model,screenOnly);
        if(chrome is null || chromeFor!=want) {
            // Only a resize needs a new bitmap. A new look redraws into the one
            // already here, so the collector is not handed a 2 MB surface each time.
            if(chrome is null || chrome.Width!=Math.Max(1,Width) || chrome.Height!=Math.Max(1,Height)) {
                chrome?.Dispose();
                chrome=new Bitmap(Math.Max(1,Width),Math.Max(1,Height),PixelFormat.Format32bppPArgb);
            }
            using(var c=Graphics.FromImage(chrome))DrawChrome(c,null,colors.Lit,colors.Off);
            chromeFor=want;ChromeBuilds++;
            drawnPressed.Clear();drawnPressed.UnionWith(pressed);drawnSymbols=symbols;drawnColors=(colors.Lit,colors.Off);
        } else {
            // A patch is drawn with everything that reaches into it, clipped to it,
            // so it comes out exactly as a full redraw would.
            // 変わった範囲だけを、そこに届くものをすべて描いて直す（全部描き直したのと同じ絵になる）。
            areas.Clear();
            if(symbols!=drawnSymbols || (colors.Lit,colors.Off)!=drawnColors)areas.Add(LcdRegion());
            if(!screenOnly)foreach(var k in Keys)if(pressed.Contains(k.Id)!=drawnPressed.Contains(k.Id))areas.Add(KeyArea(k));
            if(areas.Count>0) {
                using(var c=Graphics.FromImage(chrome))foreach(var area in areas)DrawChrome(c,area,colors.Lit,colors.Off);
                ChromePatches++;
                drawnPressed.Clear();drawnPressed.UnionWith(pressed);drawnSymbols=symbols;drawnColors=(colors.Lit,colors.Off);
            }
        }
        // Only the part to repaint is copied: an LCD frame touches a fraction of the case.
        var clip=Rectangle.Intersect(e.ClipRectangle,new Rectangle(0,0,chrome.Width,chrome.Height));
        if(clip.Width<=0 || clip.Height<=0)return;
        var mode=g.CompositingMode;g.CompositingMode=CompositingMode.SourceCopy;
        g.DrawImage(chrome,clip,clip,GraphicsUnit.Pixel);
        g.CompositingMode=mode;
        if(!clip.IntersectsWith(DeviceArea(DotRegion())))return;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TranslateTransform(ox,oy);g.ScaleTransform(scale,scale);
        DrawLcd(g,colors.Lit,colors.Unlit);
    }
    /// <summary>ケースの絵（ケース・枠・表示シンボル・キー）を描く。area を渡せば、その範囲だけを描き直す。</summary>
    /// <param name="c">描画先（キャッシュの絵）</param>
    /// <param name="area">描き直す範囲（仮想の座標）。null なら全部</param>
    /// <param name="lit">点灯した表示シンボルの色</param>
    /// <param name="off">消えた表示シンボルの色</param>
    void DrawChrome(Graphics c,RectangleF? area,Color lit,Color off) {
        c.ResetTransform();c.ResetClip();
        // Compared in screen pixels: the patch is the area grown by the bleed, so a
        // neighbour that only comes within that margin is in it too.
        Rectangle? patch=area is RectangleF part?DeviceArea(part):null;
        bool Reaches(RectangleF r)=>patch is null || DeviceArea(r).IntersectsWith(patch.Value);
        if(patch is Rectangle clip)c.SetClip(clip);
        c.SmoothingMode=SmoothingMode.AntiAlias;c.Clear(BackColor);
        c.TranslateTransform(ox,oy);c.ScaleTransform(scale,scale);
        if(screenOnly)DrawGlass(c,LcdRegion());else if(IsWide)DrawWideBody(c);else DrawE500Body(c);
        // Nothing here overlaps the LCD window, so drawing it last is the same
        // picture and keeps the whole of the chrome in one cached layer.
        if(!screenOnly)DrawTransportFrame(c);
        if(Reaches(LcdRegion()))DrawSymbols(c,lit,off);
        if(!screenOnly)foreach(var k in Keys)if(Reaches(KeyArea(k)))DrawKey(c,k);
        c.ResetClip();
    }
    /// <summary>PC-E500 のケース（本体・ロゴと型番・LCD の枠）を描く。</summary>
    void DrawE500Body(Graphics g) {
        using var body=Round(new(8,13,728,374),12);
        using var shell=new LinearGradientBrush(new RectangleF(8,13,728,374),Color.FromArgb(170,161,139),Color.FromArgb(109,108,96),90);
        g.FillPath(shell,body);using var edge=new Pen(Color.FromArgb(203,197,177),2);g.DrawPath(edge,body);
        using var top=Round(new(14,19,716,42),6);
        using var topFill=new LinearGradientBrush(new RectangleF(14,19,716,42),Color.FromArgb(194,185,162),Color.FromArgb(140,138,123),90);g.FillPath(topFill,top);
        using var hairline=new Pen(Color.FromArgb(92,94,86),1);g.DrawLine(hairline,14,64,730,64);g.DrawLine(hairline,14,74,730,74);
        // Spans measured on a PC-E500: the logo and the lettering are wide and low.
        Lettering(g,"SHARP",42,133,29.8f,43.2f,Color.WhiteSmoke);
        Lettering(g,"POCKET COMPUTER",150.6f,243.4f,35.4f,42.1f,Color.WhiteSmoke,false);Lettering(g,"PC-E500",254.5f,296.7f,35.4f,42.1f,Color.WhiteSmoke,false);
        Lettering(g,"ENGINEER SOFTWARE",517.1f,695,27.1f,37.2f,gold,false);Lettering(g,"SCIENTIFIC CONSTANTS AND FORMULAS",517.6f,697.6f,38.2f,44.4f,gold,false);
        using var bezel=Round(new(40,77,433,115),4);using var bezelFill=new LinearGradientBrush(new RectangleF(40,77,433,115),Color.FromArgb(33,38,35),Color.FromArgb(80,82,69),0f);g.FillPath(bezelFill,bezel);
        using var trim=new Pen(Color.FromArgb(207,202,183),1.5f);g.DrawPath(trim,bezel);
        for(int y=91;y<189;y+=10)g.DrawLine(hairline,43,y,469,y);
        var (glass,glassTop,glassBottom)=GlassPanel();
        using var panel=Round(glass,4);using var greenFill=new LinearGradientBrush(glass,glassTop,glassBottom,90);g.FillPath(greenFill,panel);
    }
    /// <summary>LCD のガラス（緑の面）の範囲と、上端・下端の色。</summary>
    (RectangleF Rect,Color Top,Color Bottom) GlassPanel()=>IsWide
        ?(new(70,76,442,104),Color.FromArgb(188,207,132),Color.FromArgb(151,178,111))
        :(new(79,89,360,94),Color.FromArgb(191,212,126),Color.FromArgb(164,187,116));
    // Screen only: the glass alone, square and out to the edges, with no bezel
    // showing round its rounded corners.
    /// <summary>画面だけの表示で、範囲 area を LCD のガラスの色で埋める（枠は描かない）。</summary>
    void DrawGlass(Graphics g,RectangleF area) {
        var (glass,top,bottom)=GlassPanel();
        using var fill=new LinearGradientBrush(glass,top,bottom,90){WrapMode=WrapMode.TileFlipXY};g.FillRectangle(fill,area);
    }
    /// <summary>PC-E650/U6000 のケースを描く。</summary>
    void DrawWideBody(Graphics g) {
        using var body=Round(new(2,4,856,392),18);
        using var shell=new LinearGradientBrush(new RectangleF(2,4,856,392),Color.FromArgb(112,104,94),Color.FromArgb(57,52,47),90);
        g.FillPath(shell,body);using var edge=new Pen(Color.FromArgb(139,130,116),2);g.DrawPath(edge,body);
        using var top=Round(new(3,2,854,38),9);using var topFill=new LinearGradientBrush(new RectangleF(3,2,854,38),Color.FromArgb(186,181,170),Color.FromArgb(118,113,104),90);g.FillPath(topFill,top);
        using var darkLine=new Pen(Color.FromArgb(42,39,36),2);g.DrawLine(darkLine,9,43,850,43);
        // Spans measured on a PC-U6000, which shares this case; another model name is
        // set in the same lettering.
        Lettering(g,"SHARP",73,174.4f,14.3f,28.2f,Color.WhiteSmoke);
        Lettering(g,"POCKET COMPUTER",192.8f,294.6f,20.3f,27.6f,Color.WhiteSmoke,false);
        Lettering(g,model,312.4f,312.4f+49.2f*InkRatio(model,"PC-U6000",false),20.3f,27.6f,Color.WhiteSmoke,false);
        TextAt(g,"64KB",746,4,25,Color.WhiteSmoke,true,88);
        using var bezel=Round(new(28,60,521,132),8);using var bezelFill=new LinearGradientBrush(new RectangleF(28,60,521,132),Color.FromArgb(72,67,79),Color.FromArgb(172,167,158),0f);g.FillPath(bezelFill,bezel);
        using var trim=new Pen(Color.FromArgb(211,205,191),2.5f);g.DrawPath(trim,bezel);
        using var inner=Round(new(65,73,450,109),3);using var innerFill=new SolidBrush(Color.FromArgb(66,65,59));g.FillPath(innerFill,inner);
        var (glass,glassTop,glassBottom)=GlassPanel();
        using var panel=Round(glass,2);using var greenFill=new LinearGradientBrush(glass,glassTop,glassBottom,90);g.FillPath(greenFill,panel);
        using var seam=new Pen(Color.FromArgb(43,40,36),1);g.DrawLine(seam,556,61,556,389);
    }
    // Glass is the bare LCD green, for legends cut out of a segment.
    static readonly Color Ink=Color.FromArgb(53,75,48),Ghost=Color.FromArgb(143,169,105),Glass=Color.FromArgb(181,202,125);
    /// <summary>表示シンボルの点灯ビット（BUSY などの段、2ndF などの段、DEG などの段）。</summary>
    (int,int,int) Symbols=>Core is null?(0,0,0)
        :(Native.e500_symbols(Core.Handle,0),Native.e500_symbols(Core.Handle,4),Native.e500_symbols(Core.Handle,5));
    // The annunciator row is fifteen pieces of measured, antialiased text, which is
    // far too much to redraw at 62 Hz for a row that changes when a mode does. It
    // rides along in the cached layer and its own state joins the cache key.
    // The legends' ink in LCD dots, measured on a PC-E500's glass: from and to are
    // columns, and the rows count up from the top of the dot matrix, so the upper row
    // sits 7.6 dots and the lower 3.2 dots above it, centre to centre.
    /// <summary>表示シンボルの上下の段の中心（ドットの行、LCD の上端から上向きが負）と、大文字の高さ（ドット）。</summary>
    const float UpperRow=-7.6f,LowerRow=-3.2f,CapDots=2.35f;
    /// <summary>LCD の表示シンボル（BUSY・RUN・DEG・CAPS・カナ・BATT など）を、点灯していれば濃く、消えていれば薄く描く。</summary>
    /// <param name="g">描画先</param>
    /// <param name="lit">点灯したシンボルの色</param>
    /// <param name="off">消えたシンボルの色</param>
    void DrawSymbols(Graphics g,Color lit,Color off) {
        float pixelX=IsWide?76:84,pixelY=IsWide?101:113,dotW=IsWide?1.8f:1.45f,dotH=IsWide?2.25f:1.9375f;
        // The wide glass spreads the legends out, but the lettering keeps the E500's
        // size. A Joined legend is a segment adjoining the one before (DE + G + RAD read
        // DEG / GRAD / RAD), so it keeps the E500 distance from it. The ROM lights DE and
        // G for DEGREE (s5 bits 16 and 8), G and RAD for GRAD (8 and 4), RAD alone for
        // RADIAN (4); test_angle_mode_symbols holds it to that.
        // 角度の表示は DE・G・RAD の3区画。ROM は DEGREE で DE と G（s5 のビット16と8）、
        // GRAD で G と RAD（8と4）、RADIAN で RAD（4）だけを点ける（test_angle_mode_symbols で確認）。
        const float E500Dot=1.45f,E500Cap=CapDots*1.9375f;
        var (s0,s4,s5)=Symbols;
        (string Name,float From,float To,float Row,int Value,int Mask,bool Joined)[] symbols=[
            ("BUSY",4.2f,17.4f,LowerRow,s0,1,false),("DBL",24.3f,33.9f,LowerRow,s0,8,false),
            ("RUN",39.3f,49.2f,LowerRow,s0,2,false),("PRO",54.3f,64.5f,LowerRow,s0,4,false),
            ("DE",114.2f,120.6f,UpperRow,s5,16,false),("G",121.2f,124.8f,UpperRow,s5,8,true),("RAD",125.7f,135.3f,UpperRow,s5,4,true),
            ("HYP",140.7f,150.6f,UpperRow,s4,4,false),("2ndF",119.4f,131.4f,LowerRow,s4,16,false),("CAPS",136.5f,150.3f,LowerRow,s4,8,false),
            ("PRINT",186f,201.3f,UpperRow,s5,1,false),
            ("カナ",181.2f,188.7f,LowerRow,s4,2,false),("小文字",192.3f,205.8f,LowerRow,s4,1,false),("E",208.2f,211.8f,LowerRow,s5,2,false)
        ];
        float previousFrom=0,previousAt=0;
        foreach(var s in symbols) {
            float at=s.Joined?previousAt+(s.From-previousFrom)*E500Dot:pixelX+s.From*dotW;
            float middle=pixelY+s.Row*dotH;
            Lettering(g,s.Name,at,at+(s.To-s.From)*E500Dot,middle-E500Cap/2,middle+E500Cap/2,(s.Value&s.Mask)!=0?lit:off);
            previousFrom=s.From;previousAt=at;
        }
        // The glass has a battery with BATT cut out of it: a solid body, and the
        // terminal a small stub off its left end, about half the body's height.
        // 電池の形：塗りつぶした本体に BATT の文字を抜き、左端に本体の半分弱の高さの小さな端子が出る。
        float battX=pixelX+219.7f*dotW,battMiddle=pixelY+LowerRow*dotH;
        using(var battery=new SolidBrush((s5&128)!=0?lit:off)) {
            g.FillRectangle(battery,battX,battMiddle-3.1f,15.9f*E500Dot,6.2f);
            // Overlapping the body a little, so no antialiased seam shows between them.
            g.FillRectangle(battery,battX-1.3f*E500Dot,battMiddle-1.3f,1.3f*E500Dot+0.6f,2.6f);
        }
        Lettering(g,"BATT",battX+2.4f*E500Dot,battX+14.2f*E500Dot,battMiddle-E500Cap/2,battMiddle+E500Cap/2,Glass);
    }
    // The bare glass the ink fades into at the lowest contrast, and the black
    // it all turns at the highest.
    static readonly Color Bare=Color.FromArgb(178,200,121),Darkest=Color.FromArgb(6,9,6);
    // One knob for the whole glass, as on the real unit: what is on, the dots
    // that are off and the legends that are off all darken and fade together.
    // Below 50 the off legends fade into the glass, gone by 10; from 60 the
    // dots and legends that are off darken towards what is on, until at 100
    // the whole glass is black.
    // 実機と同じく、1つのつまみでガラス全体の濃さが変わる。点灯した所、消えたドット、消えた表示シンボルが一緒に動く。
    // 50 より下では消えた表示シンボルが薄くなり、10 で見えなくなる。60 より上では消えたドットとシンボルが
    // 点灯した色に近づき、100 でガラス全体が黒くなる。
    /// <summary>コントラストに応じた、点灯した所の色、消えたドットの色（透明度つき）、消えた表示シンボルの色。</summary>
    internal (Color Lit,Color Unlit,Color Off) LcdColors() {
        static Color Mix(Color a,Color b,float t)=>Color.FromArgb((int)MathF.Round(a.R+(b.R-a.R)*t),(int)MathF.Round(a.G+(b.G-a.G)*t),(int)MathF.Round(a.B+(b.B-a.B)*t));
        float c=contrast/(float)DefaultContrast;
        Color lit=c<=1?Mix(Bare,Ink,0.25f+0.75f*c):Mix(Ink,Darkest,c-1);
        float over=Math.Clamp((contrast-60)/40f,0,1);over*=over;
        // The real display shows the dots that are off faintly always, as the glass does.
        float unlit=(showDotLines?0.07f:0)*(1-over)+over;
        Color off=contrast<=DefaultContrast?Mix(Bare,Ghost,Math.Clamp((contrast-10)/40f,0,1)):Mix(Ghost,lit,over);
        return (lit,Color.FromArgb((int)MathF.Round(unlit*255),lit),off);
    }
    /// <summary>LCD の 240×32 ドットを描く（詰めた表示か、ドットの間をあけたリアル表示）。</summary>
    /// <param name="g">描画先</param>
    /// <param name="lit">点灯したドットの色</param>
    /// <param name="unlit">消えたドットの色（透明度つき）</param>
    void DrawLcd(Graphics g,Color lit,Color unlit) {
        var dots=DotRegion();float pixelX=dots.X,pixelY=dots.Y,pixelW=dots.Width,pixelH=dots.Height;
        if(Core is null){TextAt(g,L.T("ROMを開いてください","Open a ROM"),pixelX+6,pixelY+10,12,lit);return;}
        g.SmoothingMode=SmoothingMode.None;
        // The real look needs a screen pixel or more a dot: smaller, a pixel would
        // span three dots and the share of the third was lost (thin strokes dropped).
        // リアル表示は1ドットに1ピクセル以上要る（それより小さいと、3つ目のドットの分が抜けて細い線が消えた）。
        if(showDotLines && pixelW*scale>=240 && pixelH*scale>=32)DrawRealLcd(g,dots,lit,unlit);
        else {
            // The colour of a dot on its way between off and on: the ink at an alpha
            // between that of a dot off and an opaque one.
            // 途中の濃さのドットは、消えたドットの透明度から不透明までの間の透明度にする。
            if(shadeFor!=(lit.ToArgb(),unlit.ToArgb())) {
                shadeFor=(lit.ToArgb(),unlit.ToArgb());
                int rgb=lit.ToArgb()&0xffffff;
                for(int s=0;s<256;s++) {int a=unlit.A+((255-unlit.A)*s+127)/255;shadeArgb[s]=a==0?0:a<<24|rgb;}
            }
            for(int i=0;i<lcdArgb.Length;i++)lcdArgb[i]=shadeArgb[shade[i]];
            // A sliding dot leaves its cell off and is laid on top where it is on its way.
            // 滑っているドットは、セルを消えた色にして、途中の位置に上から描く。
            foreach(var s in slides)lcdArgb[s.Index]=shadeArgb[0];
            var bits=lcdBitmap.LockBits(new Rectangle(0,0,240,32),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try {Marshal.Copy(lcdArgb,0,bits.Scan0,lcdArgb.Length);} finally {lcdBitmap.UnlockBits(bits);}
            var interpolation=g.InterpolationMode;var pixelOffset=g.PixelOffsetMode;
            g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
            var to=new Rectangle((int)pixelX,(int)pixelY,(int)pixelW,(int)pixelH);
            g.DrawImage(lcdBitmap,to,0,0,240,32,GraphicsUnit.Pixel);
            if(slides.Count>0) {
                float w=to.Width/240f,h=to.Height/32f;
                using var ink=new SolidBrush(Color.FromArgb(shadeArgb[255]));
                g.SmoothingMode=SmoothingMode.AntiAlias;
                foreach(var (index,dx,dy) in slides)g.FillRectangle(ink,to.X+(index%240+dx)*w,to.Y+(index/240+dy)*h,w,h);
            }
            g.InterpolationMode=interpolation;g.PixelOffsetMode=pixelOffset;
        }
        g.SmoothingMode=SmoothingMode.AntiAlias;
    }
    // The real display, worked out per screen pixel: each dot fills 82% of its
    // cell across and 86% down, and a pixel takes the share of every dot it
    // covers, so the gaps come out even at any size instead of rounding to a
    // whole pixel here and none there. Built only when the dots, the colours or
    // the size change.
    // リアル表示：ドットはセルの横 82%・縦 86% を占め、画面の1ピクセルは覆うドットの割合で色を混ぜる
    // （どの大きさでも隙間が均一になる）。ドット・色・大きさが変わったときだけ作り直す。
    const float DotFillX=0.82f,DotFillY=0.86f;
    Bitmap? real;
    int[] realArgb=[];
    readonly byte[] realPixels=new byte[240*32];
    (int Width,int Height,int Lit,int Unlit,bool Gaps) realFor;
    /// <summary>描いた絵で滑っていたドット（その行を描き直すため）と、滑っているドットか。</summary>
    readonly List<(int Index,float X,float Y)> realSlides=[];
    readonly bool[] realSliding=new bool[240*32];
    /// <summary>画面のピクセルごとの、滑っているドットが覆う割合と、計算し直す画面の行。</summary>
    float[] realMotion=[];
    bool[] realRedo=[];
    (int Cell0,float Weight0,int Cell1,float Weight1)[] realColumns=[],realRows=[];
    /// <summary>画面のピクセルごとに、覆うドット（最大2つ）とその割合を求める。</summary>
    /// <param name="size">ピクセル数</param>
    /// <param name="cells">ドット数</param>
    /// <param name="fill">ドットがセルを占める割合</param>
    static (int,float,int,float)[] Spans(int size,int cells,float fill) {
        static double Overlap(double a,double b,double c,double d)=>Math.Max(0,Math.Min(b,d)-Math.Max(a,c));
        var spans=new (int,float,int,float)[size];double step=(double)cells/size;
        for(int i=0;i<size;i++) {
            double a=i*step,b=a+step;int k=Math.Min(cells-1,(int)a);
            double w0=Overlap(a,b,k,k+fill),w1=k+1<cells?Overlap(a,b,k+1,k+1+fill):0;
            spans[i]=(k,(float)(w0/step),Math.Min(k+1,cells-1),(float)(w1/step));
        }
        return spans;
    }
    /// <summary>リアル表示の LCD を描く。</summary>
    /// <param name="g">描画先（仮想の座標）</param>
    /// <param name="area">ドットの範囲（仮想の座標）</param>
    /// <param name="lit">点灯したドットの色</param>
    /// <param name="unlit">消えたドットの色（透明度つき）</param>
    void DrawRealLcd(Graphics g,RectangleF area,Color lit,Color unlit) {
        PointF[] corners=[new(area.Left,area.Top),new(area.Right,area.Bottom)];
        using(var transform=g.Transform)transform.TransformPoints(corners);
        var r=Rectangle.Round(RectangleF.FromLTRB(corners[0].X,corners[0].Y,corners[1].X,corners[1].Y));
        if(r.Width<1 || r.Height<1)return;
        var want=(r.Width,r.Height,lit.ToArgb(),unlit.ToArgb(),showDotLines);
        float fillX=showDotLines?DotFillX:1,fillY=showDotLines?DotFillY:1;
        // Only the screen rows over dot rows that changed are worked out again: a
        // line of text changes eight of the 32, and the whole glass at 4K was most
        // of a frame.
        // 変わったドットの行にかかる画面の行だけを計算し直す（4K では、全体の計算に1コマの大半がかかっていた）。
        bool whole=real is null || realFor!=want;
        Span<bool> changed=stackalloc bool[32];bool any=whole;
        if(!whole)for(int k=0;k<32;k++)if(!shade.AsSpan(k*240,240).SequenceEqual(realPixels.AsSpan(k*240,240))){changed[k]=true;any=true;}
        // The dot rows a sliding dot reaches, now or when last drawn, are worked out again too.
        foreach(var list in (List<(int Index,float X,float Y)>[])[slides,realSlides])
            foreach(var s in list){int row=s.Index/240;for(int k=Math.Max(0,row-1);k<=Math.Min(31,row+1);k++)changed[k]=true;any=true;}
        if(any) {
            if(real is null || real.Width!=r.Width || real.Height!=r.Height || realFor.Gaps!=showDotLines) {
                if(real is null || real.Width!=r.Width || real.Height!=r.Height) {
                    real?.Dispose();real=new Bitmap(r.Width,r.Height,PixelFormat.Format32bppPArgb);
                    realArgb=new int[r.Width*r.Height];realMotion=new float[r.Width*r.Height];realRedo=new bool[r.Height];
                }
                realColumns=Spans(r.Width,240,fillX);realRows=Spans(r.Height,32,fillY);
            }
            realFor=want;shade.CopyTo(realPixels,0);
            Array.Clear(realSliding);foreach(var s in slides)realSliding[s.Index]=true;
            realSlides.Clear();realSlides.AddRange(slides);
            // Premultiplied, so the shares of an opaque dot and a faint one simply add.
            float ua=unlit.A/255f;
            float[] on=[255,lit.R,lit.G,lit.B],off=[unlit.A,unlit.R*ua,unlit.G*ua,unlit.B*ua];
            for(int y=0;y<r.Height;y++) {
                var (r0,v0,r1,v1)=realRows[y];
                realRedo[y]=whole || (v0>0 && changed[r0]) || (v1>0 && changed[r1]);
                if(realRedo[y])Array.Clear(realMotion,y*r.Width,r.Width);
            }
            // A sliding dot is a whole cell's square laid on the screen where it is
            // on its way; the gaps between the cells stay where they are, and it
            // shows through the dot areas it passes, as a picture behind a mesh.
            // 滑っているドットは、セル1つ分の四角を途中の位置に置く。ドットの間の線は動かさず、通り過ぎる
            // ドットの部分にだけ見える（網の向こうの絵のように）。
            float stepX=240f/r.Width,stepY=32f/r.Height;
            foreach(var (index,dx,dy) in slides) {
                float left=index%240+dx,top=index/240+dy;
                int x0=Math.Max(0,(int)(left/stepX)),x1=Math.Min(r.Width-1,(int)MathF.Ceiling((left+1)/stepX)-1);
                int y0=Math.Max(0,(int)(top/stepY)),y1=Math.Min(r.Height-1,(int)MathF.Ceiling((top+1)/stepY)-1);
                for(int y=y0;y<=y1;y++) {
                    if(!realRedo[y])continue;
                    float cy=(Math.Min((y+1)*stepY,top+1)-Math.Max(y*stepY,top))/stepY;
                    if(cy<=0)continue;
                    for(int x=x0;x<=x1;x++) {
                        float cx=(Math.Min((x+1)*stepX,left+1)-Math.Max(x*stepX,left))/stepX;
                        if(cx>0)realMotion[y*r.Width+x]+=cx*cy;
                    }
                }
            }
            for(int y=0;y<r.Height;y++) {
                if(!realRedo[y])continue;
                var (r0,v0,r1,v1)=realRows[y];int row0=r0*240,row1=r1*240,line=y*r.Width;
                for(int x=0;x<r.Width;x++) {
                    var (c0,u0,c1,u1)=realColumns[x];
                    float a=0,red=0,green=0,blue=0,moving=Math.Min(1,realMotion[line+x]);
                    // A dot on its way between off and on mixes the two (the afterimage).
                    // A sliding dot is not in its cell but where it is on its way.
                    void Add(int index,float weight) {
                        if(weight<=0)return;
                        float s=Math.Max(realSliding[index]?0:realPixels[index]/255f,moving);
                        a+=(off[0]+(on[0]-off[0])*s)*weight;red+=(off[1]+(on[1]-off[1])*s)*weight;
                        green+=(off[2]+(on[2]-off[2])*s)*weight;blue+=(off[3]+(on[3]-off[3])*s)*weight;
                    }
                    Add(row0+c0,u0*v0);Add(row0+c1,u1*v0);Add(row1+c0,u0*v1);Add(row1+c1,u1*v1);
                    realArgb[line+x]=Channel(a)<<24|Channel(red)<<16|Channel(green)<<8|Channel(blue);
                }
            }
            var bits=real.LockBits(new Rectangle(0,0,r.Width,r.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
            try {Marshal.Copy(realArgb,0,bits.Scan0,realArgb.Length);} finally {real.UnlockBits(bits);}
        }
        var saved=g.Transform;g.ResetTransform();g.DrawImageUnscaled(real!,r.X,r.Y);g.Transform=saved;saved.Dispose();
    }
    /// <summary>0〜255 に丸めた色の成分。</summary>
    static int Channel(float v)=>Math.Min(255,(int)(v+0.5f));
    /// <summary>◀ ▶ キーを囲む「PLAY BACK」の枠を描く。</summary>
    void DrawTransportFrame(Graphics g) {
        float x=IsWide?418:357,y=IsWide?343:325,w=IsWide?90:74,h=IsWide?42:40;
        using var pen=new Pen(Color.FromArgb(237,234,220),1.1f);
        float top=y+8,bottom=y+h;
        g.DrawLine(pen,x,top,x,bottom);g.DrawLine(pen,x,bottom,x+w,bottom);g.DrawLine(pen,x+w,bottom,x+w,top);
        // "-PLAY BACK-" is printed in the top edge itself, which shows only at the ends.
        // The words' spans are fractions of the frame, measured on each case.
        float[] words=IsWide?[0.159f,0.455f,0.514f,0.827f]:[0.106f,0.437f,0.519f,0.888f];
        g.DrawLine(pen,x,top,x+w*words[0]-2,top);g.DrawLine(pen,x+w*words[3]+2,top,x+w,top);
        Lettering(g,"PLAY",x+w*words[0],x+w*words[1],top-3.15f,top+3.15f,Color.WhiteSmoke,false);
        Lettering(g,"BACK",x+w*words[2],x+w*words[3],top-3.15f,top+3.15f,Color.WhiteSmoke,false);
    }
    /// <summary>キーを1つ描く（キーの上・右下の印字を含む）。</summary>
    /// <param name="g">描画先</param>
    /// <param name="k">キー</param>
    void DrawKey(Graphics g,Key k) {
        // The key goes down when pressed; what is printed on the case around it does not.
        // 押したときに下がるのはキーだけ。ケースの印字（キーの上の文字、RESET、右下の句読点）は動かない。
        var at=k.Bounds;var r=at;bool down=pressed.Contains(k.Id);if(down)r.Y+=1.2f;
        if(k.Style==7) {
            TextAt(g,"RESET",at.X-8,at.Y-13,6.5f,Color.WhiteSmoke,false,at.Width+16);
            float cx=r.X+r.Width/2,cy=r.Y+r.Height/2;
            using var resetRim=new Pen(Color.FromArgb(193,187,164),1.2f);using var hole=new SolidBrush(Color.FromArgb(24,27,24));
            g.FillEllipse(hole,cx-2.7f,cy-2.7f,5.4f,5.4f);g.DrawEllipse(resetRim,cx-3.2f,cy-3.2f,6.4f,6.4f);
            return;
        }
        if(k.Shift.Length>0)TextAt(g,k.Shift,at.X-6,at.Y-13,8,k.Id is "ON/BRK" or "CAPS"?Color.WhiteSmoke:gold,false,at.Width+12);
        if(k.Extra.Length>0)TextAt(g,k.Extra,at.X-9,at.Y-14,8,k.Style==6?Color.FromArgb(169,188,226):green,false,at.Width+18);
        using var shadow=Round(new(r.X-1,r.Y+1,r.Width+2,r.Height+2),2);using var dark=new SolidBrush(Color.FromArgb(57,60,51));g.FillPath(dark,shadow);
        Color upper=IsWide?k.Style switch{2=>Color.FromArgb(133,112,78),3=>Color.FromArgb(73,137,96),5=>Color.FromArgb(155,83,75),1 or 4=>Color.FromArgb(111,101,89),_=>Color.FromArgb(91,80,68)}:k.Style switch{2=>Color.FromArgb(247,220,144),3=>Color.FromArgb(115,188,155),5=>Color.FromArgb(236,135,107),1 or 4=>Color.FromArgb(126,129,115),_=>Color.FromArgb(78,80,67)};
        Color lower=IsWide?k.Style switch{2=>Color.FromArgb(78,65,48),3=>Color.FromArgb(34,87,57),5=>Color.FromArgb(102,43,40),1 or 4=>Color.FromArgb(65,58,51),_=>Color.FromArgb(43,37,32)}:k.Style switch{2=>Color.FromArgb(214,180,102),3=>Color.FromArgb(50,117,87),5=>Color.FromArgb(188,62,39),1 or 4=>Color.FromArgb(91,96,83),_=>Color.FromArgb(24,30,25)};
        if(down){upper=Color.FromArgb(104,147,128);lower=Color.FromArgb(47,81,66);}
        using var face=Round(r,k.Style==6?3:2);using var gradient=new LinearGradientBrush(r,upper,lower,90);g.FillPath(gradient,face);
        using var rim=new Pen(IsWide?Color.FromArgb(137,124,105):Color.FromArgb(175,172,150),0.7f);g.DrawPath(rim,face);
        if(k.Style==4) {
            using var mark=new Pen(Color.FromArgb(238,234,208),1.0f);
            float x=r.X+19,y=r.Y+8;
            g.DrawLines(mark,[new PointF(x,y),new PointF(x,y+25),new PointF(x-8,y+25),new PointF(x-8,y+20),new PointF(x-14,y+28),new PointF(x-8,y+35),new PointF(x-8,y+30),new PointF(x+5,y+30),new PointF(x+5,y)]);
            return;
        }
        float fontSize=k.Style==6?(k.Id is "DEL" or "BS" or "INS"?12:21):k.Style==1?14:k.Label.Length>3?9:12;
        Color lettering=k.Style==2&&!IsWide?Color.FromArgb(60,54,33):Color.FromArgb(245,240,220);
        if(k.Minor.Length>0) {
            // The operator, then its BASIC character small and low beside it.
            TextAt(g,k.Label,r.X+r.Width*0.08f,r.Y+(r.Height-fontSize*1.5f)/2,fontSize,lettering,true,r.Width*0.6f);
            TextAt(g,k.Minor,r.X+r.Width*0.6f,r.Y+(r.Height-fontSize*0.9f)/2+2,fontSize*0.6f,lettering,true,r.Width*0.3f);
        } else TextAt(g,k.Label,r.X,r.Y+(r.Height-fontSize*1.5f)/2,fontSize,lettering,k.Style is not 4,r.Width);
        if(k.Lower.Length>0)DrawKanaMark(g,k.Lower,at);
    }
    // The kana-mode punctuation is printed off the key's lower right corner: the
    // brackets bare, the others in a small box.
    /// <summary>キーの右下に、かなの句読点（「 」 、 。 ・）を描く。</summary>
    /// <param name="g">描画先</param>
    /// <param name="mark">句読点</param>
    /// <param name="r">キーの範囲</param>
    static void DrawKanaMark(Graphics g,string mark,RectangleF r) {
        using var pen=new Pen(Color.WhiteSmoke,0.8f);using var brush=new SolidBrush(Color.WhiteSmoke);
        if(mark is "「" or "」") {
            float left=r.Right+0.5f,right=r.Right+4,top=r.Bottom-6.5f,bottom=r.Bottom+1;
            if(mark=="「")g.DrawLines(pen,[new PointF(right,top),new PointF(left,top),new PointF(left,bottom)]);
            else g.DrawLines(pen,[new PointF(right,top),new PointF(right,bottom),new PointF(left,bottom)]);
            return;
        }
        var box=new RectangleF(r.Right+3,r.Bottom-5,6,6.5f);
        g.DrawRectangle(pen,box.X,box.Y,box.Width,box.Height);
        if(mark=="、")g.DrawLine(pen,box.X+1.2f,box.Bottom-2.6f,box.X+2.4f,box.Bottom-1.2f);
        else if(mark=="。")g.DrawEllipse(pen,box.X+0.9f,box.Bottom-2.9f,2,2);
        else g.FillEllipse(brush,box.X+box.Width/2-0.9f,box.Y+box.Height/2-0.9f,1.8f,1.8f);
    }
    /// <summary>テスト用：g に描かせる。</summary>
    internal void TestPaint(Graphics g)=>OnPaint(new PaintEventArgs(g,new Rectangle(0,0,Width,Height)));
    /// <summary>テスト用：clip の範囲だけを描き直すときと同じように、g に描かせる。</summary>
    internal void TestPaint(Graphics g,Rectangle clip) {g.SetClip(clip);OnPaint(new PaintEventArgs(g,clip));g.ResetClip();}
    /// <summary>テスト用：キーを押したときに描き直す範囲（コントロールの座標）。</summary>
    internal Rectangle TestKeyArea(string id) {Arrange();return DeviceArea(KeyArea(Keys.First(k=>k.Id==id)));}
    /// <summary>テスト用：LCD を描き直す範囲（コントロールの座標）。</summary>
    internal Rectangle TestLcdArea() {Arrange();return DeviceArea(LcdRegion());}
    // The LCD with its symbol row, in control pixels, for the manual's close-ups.
    /// <summary>LCD と表示シンボルの範囲（コントロールの座標、マニュアルの画像を切り出すため）。</summary>
    internal Rectangle LcdArea() {
        var r=LcdRegion();
        return Rectangle.Round(new RectangleF(ox+r.X*scale,oy+r.Y*scale,r.Width*scale,r.Height*scale));
    }
    /// <summary>LCD と表示シンボルの範囲（仮想の座標）。</summary>
    RectangleF LcdRegion()=>IsWide?new(70,74,444,106):new(78,86,360,96);
    /// <summary>LCD の 240×32 ドットの範囲（仮想の座標）。</summary>
    RectangleF DotRegion()=>IsWide?new(76,101,432,72):new(84,113,348,62);
    protected override void Dispose(bool disposing) {if(disposing){lcdBitmap.Dispose();chrome?.Dispose();real?.Dispose();}base.Dispose(disposing);}
}
