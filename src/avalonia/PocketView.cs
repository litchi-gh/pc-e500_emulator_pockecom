// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Key=PokeCom.PocketLayout.Key;
namespace PokeCom;

/// <summary>
/// 本体の絵（ケース・キー・LCD・表示シンボル）と、キーのクリック。Windows 版の PocketSurface と同じ絵を描く。
/// 重ねた3枚（ケース、表示シンボル、LCD のドット）からなり、変わった1枚だけを描き直す。
/// </summary>
internal sealed class PocketView : Panel
{
    /// <summary>表示するエミュレーションコア（LCD と表示シンボルを読む）。</summary>
    internal Core? Core;
    /// <summary>キーがクリックされたときに、キーの Id を知らせる。</summary>
    internal event Action<string,KeyModifiers>? KeyClicked;
    /// <summary>キーのない所（画面だけの表示ではどこでも）のダブルクリックと、ドラッグの始まり。</summary>
    internal event Action? BackgroundDoubleClicked;
    internal event Action<PointerPressedEventArgs>? BackgroundDragged;
    /// <summary>枠のない表示で、絵の端をつかんで大きさを変え始めたこと。</summary>
    internal event Action<WindowEdge,PointerPressedEventArgs>? EdgeDragged;
    /// <summary>絵の端をつかんで大きさを変えられるか（枠のない表示のとき true）。</summary>
    internal bool Resizable;
    internal List<Key> Keys {get;}=[];
    readonly HashSet<string> pressed=[];
    readonly LcdMotion motion=new();
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly Layer caseLayer,symbolLayer,dotLayer;
    internal double Zoom=1,Ox,Oy;
    string model="PC-E500";
    bool realLcd,screenOnly;
    int contrast=LcdGlass.DefaultContrast;
    bool IsWide=>model!="PC-E500";
    float CanvasWidth=>PocketLayout.CanvasWidth(IsWide);
    Rect LcdRegion=>Paint.Of(PocketLayout.LcdRegion(IsWide));
    Rect DotRegion=>Paint.Of(PocketLayout.DotRegion(IsWide));
    (Color Lit,Color Unlit,Color Off) Colours {get{var c=LcdGlass.Colors(contrast,realLcd);return (Paint.Of(c.Lit),Paint.Of(c.Unlit),Paint.Of(c.Off));}}
    internal bool RealLcd=>realLcd;
    internal int Contrast=>contrast;
    internal bool Afterimage=>motion.Afterimage;
    internal bool ScreenOnly=>screenOnly;
    /// <summary>表示している部分の大きさ（仮想の座標）と、周りの余白（左右・上下それぞれの合計）。</summary>
    internal Size ShownSize=>screenOnly?LcdRegion.Size:new Size(CanvasWidth,400);
    internal double Border=>screenOnly?0:12;

    /// <summary>絵の1枚：描く処理を受け取り、それだけを描く。</summary>
    sealed class Layer(Action<DrawingContext> draw) : Control {
        public override void Render(DrawingContext context)=>draw(context);
    }
    internal PocketView() {
        Focusable=true;Background=Brushes.Transparent;ClipToBounds=true;
        caseLayer=new(DrawCase){IsHitTestVisible=false};symbolLayer=new(DrawSymbolLayer){IsHitTestVisible=false};dotLayer=new(DrawDots){IsHitTestVisible=false};
        RenderOptions.SetBitmapInterpolationMode(dotLayer,BitmapInterpolationMode.None);
        Children.AddRange([caseLayer,symbolLayer,dotLayer]);
        SetKeys();
    }
    void SetKeys(){Keys.Clear();Keys.AddRange(PocketLayout.KeysOf(model));}
    internal void SetModel(string value){if(model==value)return;model=value;SetKeys();Redraw();}
    internal void SetLcd(bool real,int value){realLcd=real;contrast=Math.Clamp(value,0,100);Redraw();}
    internal void SetAfterimage(bool on)=>motion.SetAfterimage(on);
    internal void SetScreenOnly(bool value){if(screenOnly==value)return;screenOnly=value;Redraw();}
    /// <summary>全部を描き直す。</summary>
    internal void Redraw(){InvalidateArrange();caseLayer.InvalidateVisual();symbolLayer.InvalidateVisual();dotLayer.InvalidateVisual();}
    internal void SetPressed(string id,bool down){if(down?pressed.Add(id):pressed.Remove(id))caseLayer.InvalidateVisual();}
    internal void ReleaseKeys(){if(pressed.Count==0)return;pressed.Clear();caseLayer.InvalidateVisual();}

    protected override Size MeasureOverride(Size available) {
        foreach(var c in Children)c.Measure(available);
        return new Size(double.IsInfinity(available.Width)?0:available.Width,double.IsInfinity(available.Height)?0:available.Height);
    }
    protected override Size ArrangeOverride(Size size) {
        var was=(Zoom,Ox,Oy);
        if(screenOnly) {
            var region=LcdRegion;
            Zoom=Math.Max(0.01,Math.Min(size.Width/region.Width,size.Height/region.Height));
            Ox=(size.Width-region.Width*Zoom)/2-region.X*Zoom;Oy=(size.Height-region.Height*Zoom)/2-region.Y*Zoom;
        } else {
            Zoom=Math.Max(0.01,Math.Min((size.Width-12)/CanvasWidth,(size.Height-12)/400));
            Ox=(size.Width-CanvasWidth*Zoom)/2;Oy=(size.Height-400*Zoom)/2;
        }
        foreach(var c in Children)c.Arrange(new Rect(size));
        if(was!=(Zoom,Ox,Oy)){caseLayer.InvalidateVisual();symbolLayer.InvalidateVisual();dotLayer.InvalidateVisual();}
        return size;
    }
    Rect ToView(Rect r)=>new(Ox+r.X*Zoom,Oy+r.Y*Zoom,r.Width*Zoom,r.Height*Zoom);
    /// <summary>LCD と表示シンボルの範囲（コントロールの座標）。</summary>
    internal Rect LcdArea=>ToView(LcdRegion);
    /// <summary>仮想の座標の点を、コントロールの座標にする。</summary>
    internal Point ToView(double x,double y)=>new(Ox+x*Zoom,Oy+y*Zoom);

    // --- The mouse -------------------------------------------------------------
    Key? KeyAt(Point at) {
        if(screenOnly)return null;
        var p=new System.Drawing.PointF((float)((at.X-Ox)/Zoom),(float)((at.Y-Oy)/Zoom));
        foreach(var k in Keys)if(k.Bounds.Contains(p))return k;
        return null;
    }
    /// <summary>表示している絵の範囲。本体だけならケースの外形、画面だけなら全体。</summary>
    Rect PictureArea=>screenOnly?new Rect(Bounds.Size):ToView(IsWide?new Rect(2,2,856,394):new Rect(8,13,728,374));
    /// <summary>点が絵のどの端にあるか（端でなければ null）。キーの上は端にしない。</summary>
    internal WindowEdge? EdgeAt(Point p) {
        if(!Resizable)return null;
        var r=PictureArea;if(!r.Contains(p) || KeyAt(p) is not null)return null;
        double g=Math.Max(3,Math.Min(8,6*Zoom)),c=g*2;
        bool l=p.X<r.Left+g,rt=p.X>=r.Right-g,t=p.Y<r.Top+g,b=p.Y>=r.Bottom-g;
        bool lc=p.X<r.Left+c,rc=p.X>=r.Right-c,tc=p.Y<r.Top+c,bc=p.Y>=r.Bottom-c;
        if((t&&lc)||(l&&tc))return WindowEdge.NorthWest;
        if((t&&rc)||(rt&&tc))return WindowEdge.NorthEast;
        if((b&&lc)||(l&&bc))return WindowEdge.SouthWest;
        if((b&&rc)||(rt&&bc))return WindowEdge.SouthEast;
        return l?WindowEdge.West:rt?WindowEdge.East:t?WindowEdge.North:b?WindowEdge.South:null;
    }
    static readonly Cursor Hand=new(StandardCursorType.Hand);
    static readonly Dictionary<WindowEdge,Cursor> Edges=new() {
        [WindowEdge.West]=new(StandardCursorType.LeftSide),[WindowEdge.East]=new(StandardCursorType.RightSide),[WindowEdge.North]=new(StandardCursorType.TopSide),[WindowEdge.South]=new(StandardCursorType.BottomSide),
        [WindowEdge.NorthWest]=new(StandardCursorType.TopLeftCorner),[WindowEdge.NorthEast]=new(StandardCursorType.TopRightCorner),[WindowEdge.SouthWest]=new(StandardCursorType.BottomLeftCorner),[WindowEdge.SouthEast]=new(StandardCursorType.BottomRightCorner)
    };
    (PointerPressedEventArgs Args,Point At)? dragFrom;
    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        base.OnPointerPressed(e);
        Focus();
        if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        var at=e.GetPosition(this);
        if(KeyAt(at) is Key k){dragFrom=null;KeyClicked?.Invoke(k.Id,e.KeyModifiers);e.Handled=true;return;}
        if(e.ClickCount%2==0){dragFrom=null;BackgroundDoubleClicked?.Invoke();return;}
        if(EdgeAt(at) is WindowEdge edge){EdgeDragged?.Invoke(edge,e);return;}
        dragFrom=(e,at);
    }
    protected override void OnPointerMoved(PointerEventArgs e) {
        base.OnPointerMoved(e);
        var at=e.GetPosition(this);
        Cursor=KeyAt(at) is not null?Hand:EdgeAt(at) is WindowEdge edge?Edges[edge]:null;
        // Past a small distance, so that a double-click still reads as one.
        if(dragFrom is {} from && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && (Math.Abs(at.X-from.At.X)>4 || Math.Abs(at.Y-from.At.Y)>4)) {
            dragFrom=null;BackgroundDragged?.Invoke(from.Args);
        }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);dragFrom=null;}

    // --- The LCD ---------------------------------------------------------------
    (int,int,int,int) Symbols=>Core is null?(0,0,0,0):(Native.e500_symbols(Core.Handle,0),Native.e500_symbols(Core.Handle,4),Native.e500_symbols(Core.Handle,5),Native.e500_symbols(Core.Handle,6));
    readonly byte[] shown=new byte[240*32];
    (int,int,int,int) shownSymbols;
    bool shownAny;
    /// <summary>LCD を描き直させた回数（テスト用）。</summary>
    internal int LcdRepaints;
    /// <summary>LCD を読み直したあとに呼ぶ：ドットか表示シンボルが変わっていれば、それだけを描き直させる。</summary>
    /// <returns>残像のドットがまだ変わっている途中か（なら、次のコマも呼ぶ）</returns>
    internal bool UpdateLcd() {
        if(Core is null)return false;
        bool settling=motion.Settle(clock.Elapsed.TotalSeconds,Core.Pixels);
        var symbols=Symbols;
        if(!shownAny || symbols!=shownSymbols){shownSymbols=symbols;symbolLayer.InvalidateVisual();}
        if(shownAny && !motion.OffsetsChanged && motion.Shade.AsSpan().SequenceEqual(shown))return settling;
        motion.Shade.CopyTo(shown,0);shownAny=true;LcdRepaints++;
        dotLayer.InvalidateVisual();
        return settling;
    }
    WriteableBitmap? picture;
    readonly RealLcd real=new();
    readonly int[] packed=new int[240*32];
    // The pixels are worked out at the screen's own resolution and drawn one to
    // one: scaled by the system, the gaps between the dots would come out uneven.
    /// <summary>LCD の 240×32 ドットを、画面の実際のピクセルで作って等倍で描く。</summary>
    void DrawDots(DrawingContext g) {
        var (lit,unlit,_)=Colours;
        if(Core is null) {
            using(g.PushTransform(Transform))Paint.TextAt(g,L.T("ROMを開いてください","Open a ROM"),DotRegion.X+6,DotRegion.Y+10,12,lit);
            return;
        }
        double scaling=TopLevel.GetTopLevel(this)?.RenderScaling??1;
        var area=ToView(DotRegion);
        var r=new PixelRect((int)Math.Round(area.X*scaling),(int)Math.Round(area.Y*scaling),Math.Max(1,(int)Math.Round(area.Width*scaling)),Math.Max(1,(int)Math.Round(area.Height*scaling)));
        bool spaced=realLcd && PokeCom.RealLcd.Fits(r.Width,r.Height);
        var drawLit=System.Drawing.Color.FromArgb(lit.R,lit.G,lit.B);var drawUnlit=System.Drawing.Color.FromArgb(unlit.A,unlit.R,unlit.G,unlit.B);
        bool changed=real.Update(r.Width,r.Height,drawLit,drawUnlit,spaced,motion.Shade,motion.Slides);
        if(picture is null || picture.PixelSize!=r.Size){picture?.Dispose();picture=new WriteableBitmap(r.Size,new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Premul);changed=true;}
        if(changed) {
            using var frame=picture.Lock();
            for(int y=0;y<r.Height;y++)Marshal.Copy(real.Argb,y*r.Width,frame.Address+y*frame.RowBytes,r.Width);
        }
        g.DrawImage(picture,new Rect(0,0,r.Width,r.Height),new Rect(r.X/scaling,r.Y/scaling,r.Width/scaling,r.Height/scaling));
    }
    Matrix Transform=>Matrix.CreateScale(Zoom,Zoom)*Matrix.CreateTranslation(Ox,Oy);
    /// <summary>LCD の表示シンボル（BUSY・RUN・DEG・CAPS・カナ・BATT など）を、点灯していれば濃く、消えていれば薄く描く。</summary>
    void DrawSymbolLayer(DrawingContext g) {
        var (lit,_,off)=Colours;var (s0,s4,s5,s6)=Symbols;
        using var _t=g.PushTransform(Transform);
        double pixelX=IsWide?76:84,pixelY=IsWide?101:113,dotW=IsWide?1.8:1.45,dotH=IsWide?2.25:1.9375;
        const double E500Dot=1.45,E500Cap=LcdGlass.CapDots*1.9375;
        double previousFrom=0,previousAt=0;
        foreach(var s in LcdGlass.Legends(s0,s4,s5)) {
            double at=s.Joined?previousAt+(s.From-previousFrom)*E500Dot:pixelX+s.From*dotW,middle=pixelY+s.Row*dotH;
            Paint.Lettering(g,s.Name,at,at+(s.To-s.From)*E500Dot,middle-E500Cap/2,middle+E500Cap/2,(s.Value&s.Mask)!=0?lit:off);
            previousFrom=s.From;previousAt=at;
        }
        double battX=pixelX+219.7*dotW,battMiddle=pixelY+LcdGlass.LowerRow*dotH;
        var battery=Paint.Solid((s6&1)!=0?lit:off);
        g.FillRectangle(battery,new Rect(battX,battMiddle-3.1,15.9*E500Dot,6.2));
        g.FillRectangle(battery,new Rect(battX-1.3*E500Dot,battMiddle-1.3,1.3*E500Dot+0.6,2.6));
        Paint.Lettering(g,"BATT",battX+2.4*E500Dot,battX+14.2*E500Dot,battMiddle-E500Cap/2,battMiddle+E500Cap/2,Paint.Of(LcdGlass.Glass));
    }

    // --- The case ----------------------------------------------------------------
    static readonly Color White=Color.FromRgb(245,245,245),Gold=Color.FromRgb(238,204,121),Green=Color.FromRgb(155,223,185);
    static Color Rgb(byte r,byte g,byte b)=>Color.FromRgb(r,g,b);
    (Rect Rect,Color Top,Color Bottom) GlassPanel {get{var p=PocketLayout.GlassPanel(IsWide);return (Paint.Of(p.Rect),Paint.Of(p.Top),Paint.Of(p.Bottom));}}
    void DrawCase(DrawingContext g) {
        using var _t=g.PushTransform(Transform);
        if(screenOnly) {
            // The glass alone, square and out to the edges.
            var (glass,top,bottom)=GlassPanel;
            g.FillRectangle(Paint.Gradient(glass,top,bottom),new Rect(-Ox/Zoom,-Oy/Zoom,Bounds.Width/Zoom,Bounds.Height/Zoom));
            return;
        }
        if(IsWide)DrawWideBody(g);else DrawE500Body(g);
        DrawTransportFrame(g);
        foreach(var k in Keys)DrawKey(g,k);
    }
    static void Box(DrawingContext g,Rect r,double radius,Color from,Color to,bool across=false,IPen? pen=null)=>Paint.Rounded(g,Paint.Gradient(r,from,to,across),pen,r,radius);
    void DrawE500Body(DrawingContext g) {
        Box(g,new(8,13,728,374),12,Rgb(170,161,139),Rgb(109,108,96),false,Paint.Pen(203,197,177,2));
        Box(g,new(14,19,716,42),6,Rgb(194,185,162),Rgb(140,138,123));
        var hairline=Paint.Pen(92,94,86,1);g.DrawLine(hairline,new(14,64),new(730,64));g.DrawLine(hairline,new(14,74),new(730,74));
        // Spans measured on a PC-E500: the logo and the lettering are wide and low.
        Paint.Lettering(g,"SHARP",42,133,29.8,43.2,White);
        Paint.Lettering(g,"POCKET COMPUTER",150.6,243.4,35.4,42.1,White,false);Paint.Lettering(g,"PC-E500",254.5,296.7,35.4,42.1,White,false);
        Paint.Lettering(g,"ENGINEER SOFTWARE",517.1,695,27.1,37.2,Gold,false);Paint.Lettering(g,"SCIENTIFIC CONSTANTS AND FORMULAS",517.6,697.6,38.2,44.4,Gold,false);
        Box(g,new(40,77,433,115),4,Rgb(33,38,35),Rgb(80,82,69),true,Paint.Pen(207,202,183,1.5));
        for(int y=91;y<189;y+=10)g.DrawLine(hairline,new(43,y),new(469,y));
        var (glass,glassTop,glassBottom)=GlassPanel;Box(g,glass,4,glassTop,glassBottom);
    }
    void DrawWideBody(DrawingContext g) {
        Box(g,new(2,4,856,392),18,Rgb(112,104,94),Rgb(57,52,47),false,Paint.Pen(139,130,116,2));
        Box(g,new(3,2,854,38),9,Rgb(186,181,170),Rgb(118,113,104));
        g.DrawLine(Paint.Pen(42,39,36,2),new(9,43),new(850,43));
        // Spans measured on a PC-U6000, which shares this case; another model name is
        // set in the same lettering.
        Paint.Lettering(g,"SHARP",73,174.4,14.3,28.2,White);
        Paint.Lettering(g,"POCKET COMPUTER",192.8,294.6,20.3,27.6,White,false);
        Paint.Lettering(g,model,312.4,312.4+49.2*Paint.InkRatio(model,"PC-U6000",false),20.3,27.6,White,false);
        Paint.TextAt(g,"64KB",746,4,25,White,true,88);
        Box(g,new(28,60,521,132),8,Rgb(72,67,79),Rgb(172,167,158),true,Paint.Pen(211,205,191,2.5));
        Paint.Rounded(g,Paint.Solid(66,65,59),null,new(65,73,450,109),3);
        var (glass,glassTop,glassBottom)=GlassPanel;Box(g,glass,2,glassTop,glassBottom);
        g.DrawLine(Paint.Pen(43,40,36,1),new(556,61),new(556,389));
    }
    /// <summary>◀ ▶ キーを囲む「PLAY BACK」の枠を描く。</summary>
    void DrawTransportFrame(DrawingContext g) {
        double x=IsWide?418:357,y=IsWide?343:325,w=IsWide?90:74,h=IsWide?42:40,top=y+8,bottom=y+h;
        var pen=Paint.Pen(237,234,220,1.1);
        g.DrawLine(pen,new(x,top),new(x,bottom));g.DrawLine(pen,new(x,bottom),new(x+w,bottom));g.DrawLine(pen,new(x+w,bottom),new(x+w,top));
        double[] words=IsWide?[0.159,0.455,0.514,0.827]:[0.106,0.437,0.519,0.888];
        g.DrawLine(pen,new(x,top),new(x+w*words[0]-2,top));g.DrawLine(pen,new(x+w*words[3]+2,top),new(x+w,top));
        Paint.Lettering(g,"PLAY",x+w*words[0],x+w*words[1],top-3.15,top+3.15,White,false);
        Paint.Lettering(g,"BACK",x+w*words[2],x+w*words[3],top-3.15,top+3.15,White,false);
    }
    static void Lines(DrawingContext g,IPen pen,params Point[] points){for(int i=1;i<points.Length;i++)g.DrawLine(pen,points[i-1],points[i]);}
    /// <summary>塗った三角を描く。</summary>
    static void Triangle(DrawingContext g,IBrush brush,Point a,Point b,Point c) {
        var shape=new StreamGeometry();
        using(var s=shape.Open()){s.BeginFigure(a,true);s.LineTo(b);s.LineTo(c);s.EndFigure(true);}
        g.DrawGeometry(brush,null,shape);
    }
    /// <summary>キーを1つ描く（キーの上・右下の印字を含む）。押したときに下がるのはキーだけ。</summary>
    void DrawKey(DrawingContext g,Key k) {
        var at=Paint.Of(k.Bounds);bool down=pressed.Contains(k.Id);var r=down?at.Translate(new Vector(0,1.2)):at;
        if(k.Style==7) {
            Paint.TextAt(g,"RESET",at.X-8,at.Y-13,6.5,White,false,at.Width+16);
            g.DrawEllipse(Paint.Solid(24,27,24),null,r.Center,2.7,2.7);g.DrawEllipse(null,Paint.Pen(193,187,164,1.2),r.Center,3.2,3.2);
            return;
        }
        if(k.Shift.Length>0)Paint.TextAt(g,k.Shift,at.X-6,at.Y-13,8,k.Id is "ON/BRK" or "CAPS"?White:Gold,false,at.Width+12);
        if(k.Extra.Length>0)Paint.TextAt(g,k.Extra,at.X-9,at.Y-14,8,k.Style==6?Rgb(169,188,226):Green,false,at.Width+18);
        Paint.Rounded(g,Paint.Solid(57,60,51),null,new(r.X-1,r.Y+1,r.Width+2,r.Height+2),2);
        Color upper=IsWide?k.Style switch{2=>Rgb(133,112,78),3=>Rgb(73,137,96),5=>Rgb(155,83,75),1 or 4=>Rgb(111,101,89),_=>Rgb(91,80,68)}:k.Style switch{2=>Rgb(247,220,144),3=>Rgb(115,188,155),5=>Rgb(236,135,107),1 or 4=>Rgb(126,129,115),_=>Rgb(78,80,67)};
        Color lower=IsWide?k.Style switch{2=>Rgb(78,65,48),3=>Rgb(34,87,57),5=>Rgb(102,43,40),1 or 4=>Rgb(65,58,51),_=>Rgb(43,37,32)}:k.Style switch{2=>Rgb(214,180,102),3=>Rgb(50,117,87),5=>Rgb(188,62,39),1 or 4=>Rgb(91,96,83),_=>Rgb(24,30,25)};
        if(down){upper=Rgb(104,147,128);lower=Rgb(47,81,66);}
        Box(g,r,k.Style==6?3:2,upper,lower,false,IsWide?Paint.Pen(137,124,105,0.7):Paint.Pen(175,172,150,0.7));
        if(k.Style==4) {
            double x=r.X+19,y=r.Y+8;
            Lines(g,Paint.Pen(238,234,208,1),new(x,y),new(x,y+25),new(x-8,y+25),new(x-8,y+20),new(x-14,y+28),new(x-8,y+35),new(x-8,y+30),new(x+5,y+30),new(x+5,y));
            return;
        }
        Color lettering=k.Style==2 && !IsWide?Rgb(60,54,33):Rgb(245,240,220);
        if(k.Id=="TEXT") {
            // 実機の TEXT キーは、上向きと下向きの塗った三角を上下にほぼ接して並べた印字。
            double cx=r.X+r.Width/2,cy=r.Y+r.Height/2,half=r.Width*0.17,tall=r.Height*0.27,gap=r.Height*0.03;
            Triangle(g,Paint.Solid(lettering),new(cx,cy-gap-tall),new(cx+half,cy-gap),new(cx-half,cy-gap));
            Triangle(g,Paint.Solid(lettering),new(cx-half,cy+gap),new(cx+half,cy+gap),new(cx,cy+gap+tall));
            return;
        }
        double size=k.Style==6?(k.Id is "DEL" or "BS" or "INS"?12:21):k.Style==1?14:k.Label.Length>3?9:12;
        if(k.Minor.Length>0) {
            Paint.TextAt(g,k.Label,r.X+r.Width*0.08,r.Y+(r.Height-size*1.5)/2,size,lettering,true,r.Width*0.6);
            Paint.TextAt(g,k.Minor,r.X+r.Width*0.6,r.Y+(r.Height-size*0.9)/2+2,size*0.6,lettering,true,r.Width*0.3);
        } else Paint.TextAt(g,k.Label,r.X,r.Y+(r.Height-size*1.5)/2,size,lettering,true,r.Width);
        if(k.Lower.Length>0)DrawKanaMark(g,k.Lower,at);
    }
    /// <summary>キーの右下に、かなの句読点（「 」 、 。 ・）を描く。</summary>
    static void DrawKanaMark(DrawingContext g,string mark,Rect r) {
        var pen=Paint.Pen(White,0.8);
        if(mark is "「" or "」") {
            double left=r.Right+0.5,right=r.Right+4,top=r.Bottom-6.5,bottom=r.Bottom+1;
            if(mark=="「")Lines(g,pen,new(right,top),new(left,top),new(left,bottom));
            else Lines(g,pen,new(right,top),new(right,bottom),new(left,bottom));
            return;
        }
        var box=new Rect(r.Right+3,r.Bottom-5,6,6.5);
        g.DrawRectangle(null,pen,box);
        if(mark=="、")g.DrawLine(pen,new(box.X+1.2,box.Bottom-2.6),new(box.X+2.4,box.Bottom-1.2));
        else if(mark=="。")g.DrawEllipse(null,pen,new Point(box.X+1.9,box.Bottom-1.9),1,1);
        else g.DrawEllipse(Paint.Solid(White),null,box.Center,0.9,0.9);
    }
}
