// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PokeCom;
using Key=PocketLayout.Key;

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
    /// <summary>今の外観のキーの一覧。</summary>
    internal List<PocketLayout.Key> Keys {get;}=[];
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
    float CanvasWidth=>PocketLayout.CanvasWidth(IsWide);
    // Real: each dot drawn apart from its neighbours, as on the glass, rather than
    // packed edge to edge. Contrast runs 0-100, as the case's dial: 50 is the ink
    // this always had; lower fades the dots, higher darkens them until the dots
    // that are off begin to show.
    bool showDotLines;
    int contrast=DefaultContrast;
    /// <summary>コントラストの既定値（0〜100。今までの表示と同じ濃さ）。</summary>
    internal const int DefaultContrast=LcdGlass.DefaultContrast;
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
    /// <summary>LCD のドットの濃さと動き（残像）。</summary>
    readonly LcdMotion motion=new();
    byte[] shade=>motion.Shade;
    List<(int Index,float X,float Y)> slides=>motion.Slides;
    readonly System.Diagnostics.Stopwatch levelClock=System.Diagnostics.Stopwatch.StartNew();
    /// <summary>LCD の残像（フレーム補完）を使うか。</summary>
    internal bool Afterimage=>motion.Afterimage;
    /// <summary>LCD の残像（フレーム補完）を切り替える。切り替えたら、今の絵から始め直す。</summary>
    internal void SetAfterimage(bool on)=>motion.SetAfterimage(on);
    /// <summary>描いているドットの濃さ（テスト用。0〜255）。</summary>
    internal byte TestShade(int index)=>motion.Shade[index];
    /// <summary>滑っているドットの数（テスト用）。</summary>
    internal int TestSliding=>motion.Slides.Count;
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
    /// <summary>今の外観のキーを並べる。</summary>
    void BuildKeys(){Keys.Clear();Keys.AddRange(PocketLayout.KeysOf(model));}
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
    (int,int,int,int) drawnSymbols;
    /// <summary>画面に出した LCD のドットと表示シンボル（変わったときだけ描き直すため）。</summary>
    readonly byte[] shownPixels=new byte[240*32];
    (int,int,int,int) shownSymbols;
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
    static RectangleF KeyArea(Key k)=>PocketLayout.KeyArea(k);
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
        bool settling=motion.Settle(now,Core.Pixels);
        var symbols=Symbols;
        if(shownAny && symbols==shownSymbols && !motion.OffsetsChanged && shade.AsSpan().SequenceEqual(shownPixels))return settling;
        shade.CopyTo(shownPixels,0);shownSymbols=symbols;shownAny=true;
        Arrange();Invalidate(DeviceArea(LcdRegion()));LcdRepaints++;
        return settling;
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
    (RectangleF Rect,Color Top,Color Bottom) GlassPanel()=>PocketLayout.GlassPanel(IsWide);
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
    static readonly Color Glass=LcdGlass.Glass;
    /// <summary>表示シンボルの点灯ビット（BUSY などの段、2ndF などの段、DEG などの段、BATT の段）。</summary>
    // The ROM writes the BATT flag (BFC97h) as the whole byte of page 6 (ROM 7.2 F30BAh), so BATT is page 6 bit 0.
    // ROM は BATT の作業域（BFC97h）をページ 6 の 1 バイトとして書く（ROM 7.2 の F30BAh）。BATT はページ 6 の bit0。
    (int,int,int,int) Symbols=>Core is null?(0,0,0,0)
        :(Native.e500_symbols(Core.Handle,0),Native.e500_symbols(Core.Handle,4),Native.e500_symbols(Core.Handle,5),Native.e500_symbols(Core.Handle,6));
    // The annunciator row is fifteen pieces of measured, antialiased text, which is
    // far too much to redraw at 62 Hz for a row that changes when a mode does. It
    // rides along in the cached layer and its own state joins the cache key.
    // The legends' ink in LCD dots, measured on a PC-E500's glass: from and to are
    // columns, and the rows count up from the top of the dot matrix, so the upper row
    // sits 7.6 dots and the lower 3.2 dots above it, centre to centre.
    const float LowerRow=LcdGlass.LowerRow,CapDots=LcdGlass.CapDots;
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
        var (s0,s4,s5,s6)=Symbols;
        var symbols=LcdGlass.Legends(s0,s4,s5);
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
        using(var battery=new SolidBrush((s6&1)!=0?lit:off)) {
            g.FillRectangle(battery,battX,battMiddle-3.1f,15.9f*E500Dot,6.2f);
            // Overlapping the body a little, so no antialiased seam shows between them.
            g.FillRectangle(battery,battX-1.3f*E500Dot,battMiddle-1.3f,1.3f*E500Dot+0.6f,2.6f);
        }
        Lettering(g,"BATT",battX+2.4f*E500Dot,battX+14.2f*E500Dot,battMiddle-E500Cap/2,battMiddle+E500Cap/2,Glass);
    }
    /// <summary>コントラストに応じた色（LcdGlass.Colors）。</summary>
    internal (Color Lit,Color Unlit,Color Off) LcdColors()=>LcdGlass.Colors(contrast,showDotLines);
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
        if(showDotLines && PokeCom.RealLcd.Fits(pixelW*scale,pixelH*scale))DrawRealLcd(g,dots,lit,unlit);
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
    // リアル表示の絵（RealLcd が作ったピクセルを入れる）。大きさが変わったときだけ作り直す。
    Bitmap? real;
    readonly PokeCom.RealLcd realLcd=new();
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
        if(realLcd.Update(r.Width,r.Height,lit,unlit,showDotLines,shade,slides)) {
            if(real is null || real.Width!=r.Width || real.Height!=r.Height){real?.Dispose();real=new Bitmap(r.Width,r.Height,PixelFormat.Format32bppPArgb);}
            var bits=real.LockBits(new Rectangle(0,0,r.Width,r.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
            try {Marshal.Copy(realLcd.Argb,0,bits.Scan0,realLcd.Argb.Length);} finally {real.UnlockBits(bits);}
        }
        var saved=g.Transform;g.ResetTransform();g.DrawImageUnscaled(real!,r.X,r.Y);g.Transform=saved;saved.Dispose();
    }
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
    RectangleF LcdRegion()=>PocketLayout.LcdRegion(IsWide);
    RectangleF DotRegion()=>PocketLayout.DotRegion(IsWide);
    protected override void Dispose(bool disposing) {if(disposing){lcdBitmap.Dispose();chrome?.Dispose();real?.Dispose();}base.Dispose(disposing);}
}
