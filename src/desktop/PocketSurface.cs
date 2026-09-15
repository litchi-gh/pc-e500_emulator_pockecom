// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PokeCom;

// Coordinates use a 400-high device space; E500 is 744 wide, E650/U6000 are 860 wide.
internal sealed class PocketSurface : Control
{
    internal Core? Core;
    internal event Action<string>? KeyClicked;
    internal readonly record struct Key(string Id,string Label,RectangleF Bounds,string Shift="",string Extra="",int Style=0,string Lower="");
    internal List<Key> Keys {get;}=[];
    readonly HashSet<string> pressed=[];
    readonly Bitmap lcdBitmap=new(240,32,PixelFormat.Format32bppArgb);
    readonly int[] lcdArgb=new int[240*32];
    float scale=1,ox,oy;
    string model="PC-E500";
    bool IsWide=>model!="PC-E500";
    float CanvasWidth=>IsWide?860:744;
    bool showDotLines;
    internal void SetDotLines(bool value) {showDotLines=value;Invalidate();}
    internal void SetModel(string value) {
        if(!HardwareSettings.Appearances.Contains(value))throw new ArgumentException("Unknown appearance");
        if(model==value)return;model=value;BuildKeys();Invalidate();
    }
    readonly Color gold=Color.FromArgb(238,204,121),green=Color.FromArgb(155,223,185);
    internal PocketSurface() {
        Dock=DockStyle.Fill;DoubleBuffered=true;BackColor=Color.FromArgb(31,35,37);TabStop=true;
        BuildKeys();
        MouseDown+=(_,e)=>{
            if(e.Button!=MouseButtons.Left)return;
            var p=new PointF((e.X-ox)/scale,(e.Y-oy)/scale);
            foreach(var k in Keys)if(k.Bounds.Contains(p)){KeyClicked?.Invoke(k.Id);Focus();break;}
        };
        MouseMove+=(_,e)=>{var p=new PointF((e.X-ox)/scale,(e.Y-oy)/scale);Cursor=Keys.Any(k=>k.Bounds.Contains(p))?Cursors.Hand:Cursors.Default;};
    }
    internal void SetPressed(string id,bool down) {if(down)pressed.Add(id);else pressed.Remove(id);Invalidate();}
    internal void ReleaseKeys() {pressed.Clear();Invalidate();}
    internal void TriggerForTest(string id) {if(!Keys.Any(k=>k.Id==id))throw new ArgumentException("Unknown surface key");KeyClicked?.Invoke(id);}
    void Add(string id,string label,float x,float y,float w=27,float h=18,string shift="",string extra="",int style=0,string lower="")=>Keys.Add(new(id,label,new(x,y,w,h),shift,extra,style,lower));
    void BuildKeys() {
        Keys.Clear();
        if(IsWide){BuildWideKeys();return;}
        Add("TEXT","↕",32,207,25,16);
        string[] function=["BASIC","電卓","行列","統計","エンジニアソフトウェア"];
        for(int i=0;i<5;i++)Add("F"+(i+1),"PF"+(i+1),78+i*73,207,61,16,extra:function[i],style:1);
        Add("2ndF","2ndF",444,207,24,17,style:2);
        Add("MENU","MENU",31,239,27,18,shift:"CAL",style:3);
        Add("BASIC","BASIC",31,273,27,18,shift:"AER",style:3);
        Add("RESET","",62,273,13,18,style:7);
        Add("OFF","OFF",31,306,27,18);
        Add("ON/BRK","ON",73,306,29,18,shift:"BREAK");
        Add("SHIFT","SHIFT",30,341,37,19,style:2);
        string[] shifts=["!","\"","#","$","%","&","'","<",">","@"];
        for(int i=0;i<10;i++)Add("QWERTYUIOP"[i].ToString(),"QWERTYUIOP"[i].ToString(),78+i*36.5f,240,26,18,shifts[i]);
        string[] asdf=["A","S","D","F","G","H","J","K","L"];
        string[] ashift=["[","]","{","}","\\","|","~","_","^"];
        for(int i=0;i<9;i++)Add(asdf[i],asdf[i],78+i*36.5f,273,26,18,ashift[i]);
        string[] zrow=["Z","X","C","V","B","N","M",",",";"];
        for(int i=0;i<9;i++)Add(zrow[i],zrow[i],110+i*37,306,27,19,i==6?"`":i==7?"?":i==8?":":"");
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
        string[][] legends=[["archyp","sin⁻¹","cos⁻¹","tan⁻¹","TAB","CA"],["→DEC","→D.MS","eˣ","10ˣ","→rθ","TITLE"],["π","xʸ","³√","%","→xy","n!"]];
        for(int r=0;r<3;r++)for(int c=0;c<6;c++)Add(scientific[r][c],labels[r][c],493+c*39.2f,99+r*33,28,19,legends[r][c],style:r==0&&c==5?5:0,lower:scientific[r][c]=="("?"「":scientific[r][c]==")"?"」":"");
        string[][] numeric=[["7","8","9","/","DEL"],["4","5","6","*","BS"],["1","2","3","-","INS"],["0","+/-",".","+","="]];
        string[][] nlabels=[["7","8","9","÷","DEL"],["4","5","6","×","BS"],["1","2","3","−","INS"],["0","+/−",".","+","="]];
        for(int r=0;r<4;r++)for(int c=0;c<5;c++)Add(numeric[r][c],nlabels[r][c],492+c*48.5f,214+r*40,35,25,r==3&&c==2?"DRG":"",c==4?new[]{"x↔M","RM","M+",""}[r]:"",6);
    }
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
        string[] ashift=["[","]","{","}","\\","|","~","_","^"];
        for(int i=0;i<9;i++)Add(asdf[i],asdf[i],90+i*42,284,31,20,ashift[i]);
        string[] zrow=["Z","X","C","V","B","N","M",",",";"];
        for(int i=0;i<9;i++)Add(zrow[i],zrow[i],111+i*42,321,32,21,i==6?"`":i==7?"?":i==8?":":"");
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
        string[][] legends=[["archyp","sin⁻¹","cos⁻¹","tan⁻¹","TAB","CA"],["→DEC","→D.MS","eˣ","10ˣ","→rθ","TITLE"],["π","xʸ","³√","%","→xy","n!"]];
        for(int r=0;r<3;r++)for(int c=0;c<6;c++)Add(scientific[r][c],labels[r][c],574+c*43,88+r*36,30,20,legends[r][c],style:r==0&&c==5?5:0,lower:scientific[r][c]=="("?"「":scientific[r][c]==")"?"」":"");
        string[][] numeric=[["7","8","9","/","DEL"],["4","5","6","*","BS"],["1","2","3","-","INS"],["0","+/-",".","+","="]];
        string[][] nlabels=[["7","8","9","÷","DEL"],["4","5","6","×","BS"],["1","2","3","−","INS"],["0","+/−",".","+","="]];
        for(int r=0;r<4;r++)for(int c=0;c<5;c++)Add(numeric[r][c],nlabels[r][c],580+c*52,214+r*44,38,28,r==3&&c==2?"DRG":"",c==4?new[]{"x↔M","RM","M+",""}[r]:"",6);
    }
    static GraphicsPath Round(RectangleF r,float radius) {
        var p=new GraphicsPath();float d=radius*2;
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    static void TextAt(Graphics g,string text,float x,float y,float size,Color color,bool bold=false,float width=0) {
        string family=text.Any(c=>c>255)?"Yu Gothic UI":"Arial";
        using var measure=new Font(family,size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
        float measured=g.MeasureString(text,measure,1000,StringFormat.GenericTypographic).Width;
        float actual=width>0&&measured>width-3?size*(width-3)/measured:size;
        using var f=new Font(family,Math.Max(4,actual),bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel);
        using var b=new SolidBrush(color);using var format=new StringFormat(StringFormat.GenericTypographic){Alignment=width>0?StringAlignment.Center:StringAlignment.Near,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap};
        if(width>0)g.DrawString(text,f,b,new RectangleF(x,y,width,size*1.5f),format);else g.DrawString(text,f,b,new PointF(x,y));
    }
    // The case and its seventy keys cost 5.4 ms to draw and only change when a key
    // goes down or the window is resized, while the LCD repaints 62 times a second.
    // Redrawing all of it every frame spent a third of the frame budget on plastic,
    // and WM_PAINT outranks the WM_TIMER that runs the machine, so a repaint landing
    // across a tick made that tick late and the audio it produced arrive in a burst.
    // Cache the chrome and blit it; only the LCD is actually drawn per frame.
    Bitmap? chrome;
    string chromeFor="";
    internal int ChromeBuilds {get;private set;}
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);var g=e.Graphics;
        scale=Math.Min((Width-12)/CanvasWidth,(Height-12)/400f);ox=(Width-CanvasWidth*scale)/2;oy=(Height-400*scale)/2;
        string want=$"{Width}x{Height}|{model}|{string.Join(",",pressed.Order())}|{Symbols}";
        if(chrome is null || chromeFor!=want) {
            // Only a resize needs a new bitmap. A key going down or an annunciator
            // changing redraws into the one already here, so ordinary typing does not
            // hand the collector a 2 MB surface per keystroke.
            if(chrome is null || chrome.Width!=Math.Max(1,Width) || chrome.Height!=Math.Max(1,Height)) {
                chrome?.Dispose();
                chrome=new Bitmap(Math.Max(1,Width),Math.Max(1,Height),PixelFormat.Format32bppPArgb);
            }
            using(var c=Graphics.FromImage(chrome)) {
                c.SmoothingMode=SmoothingMode.AntiAlias;c.Clear(BackColor);
                c.TranslateTransform(ox,oy);c.ScaleTransform(scale,scale);
                if(IsWide)DrawWideBody(c);else DrawE500Body(c);
                // Nothing here overlaps the LCD window, so drawing it last is the same
                // picture and keeps the whole of the chrome in one cached layer.
                DrawTransportFrame(c);
                DrawSymbols(c);
                foreach(var k in Keys)DrawKey(c,k);
            }
            chromeFor=want;ChromeBuilds++;
        }
        g.DrawImageUnscaled(chrome,0,0);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TranslateTransform(ox,oy);g.ScaleTransform(scale,scale);
        DrawLcd(g);
    }
    void DrawE500Body(Graphics g) {
        using var body=Round(new(8,13,728,374),12);
        using var shell=new LinearGradientBrush(new RectangleF(8,13,728,374),Color.FromArgb(170,161,139),Color.FromArgb(109,108,96),90);
        g.FillPath(shell,body);using var edge=new Pen(Color.FromArgb(203,197,177),2);g.DrawPath(edge,body);
        using var top=Round(new(14,19,716,42),6);
        using var topFill=new LinearGradientBrush(new RectangleF(14,19,716,42),Color.FromArgb(194,185,162),Color.FromArgb(140,138,123),90);g.FillPath(topFill,top);
        using var hairline=new Pen(Color.FromArgb(92,94,86),1);g.DrawLine(hairline,14,64,730,64);g.DrawLine(hairline,14,74,730,74);
        TextAt(g,"SHARP",39,22,25,Color.WhiteSmoke,true);TextAt(g,"POCKET COMPUTER   PC-E500",149,33,8,Color.WhiteSmoke);
        TextAt(g,"ENGINEER SOFTWARE",518,23,13,gold);TextAt(g,"SCIENTIFIC CONSTANTS AND FORMULAS",518,37,7.5f,gold);
        using var bezel=Round(new(40,77,433,115),4);using var bezelFill=new LinearGradientBrush(new RectangleF(40,77,433,115),Color.FromArgb(33,38,35),Color.FromArgb(80,82,69),0f);g.FillPath(bezelFill,bezel);
        using var trim=new Pen(Color.FromArgb(207,202,183),1.5f);g.DrawPath(trim,bezel);
        for(int y=91;y<189;y+=10)g.DrawLine(hairline,43,y,469,y);
        using var panel=Round(new(79,89,360,94),4);using var greenFill=new LinearGradientBrush(new RectangleF(79,89,360,94),Color.FromArgb(191,212,126),Color.FromArgb(164,187,116),90);g.FillPath(greenFill,panel);
    }
    void DrawWideBody(Graphics g) {
        using var body=Round(new(2,4,856,392),18);
        using var shell=new LinearGradientBrush(new RectangleF(2,4,856,392),Color.FromArgb(112,104,94),Color.FromArgb(57,52,47),90);
        g.FillPath(shell,body);using var edge=new Pen(Color.FromArgb(139,130,116),2);g.DrawPath(edge,body);
        using var top=Round(new(3,2,854,38),9);using var topFill=new LinearGradientBrush(new RectangleF(3,2,854,38),Color.FromArgb(186,181,170),Color.FromArgb(118,113,104),90);g.FillPath(topFill,top);
        using var darkLine=new Pen(Color.FromArgb(42,39,36),2);g.DrawLine(darkLine,9,43,850,43);
        TextAt(g,"SHARP",71,8,22,Color.WhiteSmoke,true);TextAt(g,"POCKET COMPUTER   "+model,188,15,10,Color.WhiteSmoke,true);
        TextAt(g,"64KB",746,4,25,Color.WhiteSmoke,true,88);
        using var bezel=Round(new(28,60,521,132),8);using var bezelFill=new LinearGradientBrush(new RectangleF(28,60,521,132),Color.FromArgb(72,67,79),Color.FromArgb(172,167,158),0f);g.FillPath(bezelFill,bezel);
        using var trim=new Pen(Color.FromArgb(211,205,191),2.5f);g.DrawPath(trim,bezel);
        using var inner=Round(new(65,73,450,109),3);using var innerFill=new SolidBrush(Color.FromArgb(66,65,59));g.FillPath(innerFill,inner);
        using var panel=Round(new(70,76,442,104),2);using var greenFill=new LinearGradientBrush(new RectangleF(70,76,442,104),Color.FromArgb(188,207,132),Color.FromArgb(151,178,111),90);g.FillPath(greenFill,panel);
        using var seam=new Pen(Color.FromArgb(43,40,36),1);g.DrawLine(seam,556,61,556,389);
    }
    static readonly Color Ink=Color.FromArgb(53,75,48),Ghost=Color.FromArgb(143,169,105);
    (int,int,int) Symbols=>Core is null?(0,0,0)
        :(Native.e500_symbols(Core.Handle,0),Native.e500_symbols(Core.Handle,4),Native.e500_symbols(Core.Handle,5));
    // The annunciator row is fifteen pieces of measured, antialiased text, which is
    // far too much to redraw at 62 Hz for a row that changes when a mode does. It
    // rides along in the cached layer and its own state joins the cache key.
    void DrawSymbols(Graphics g) {
        float symbolX=IsWide?76:84,symbolY=IsWide?80:93,symbolScale=IsWide?1.24f:1;
        var (s0,s4,s5)=Symbols;
        (string Name,float X,float Y,int Value,int Mask)[] symbols=[
            ("BUSY",0,9,s0,1),("DBL",26,9,s0,8),("RUN",55,9,s0,2),("PRO",78,9,s0,4),
            ("DEG",166,0,s5,16),("G",187,0,s5,8),("RAD",195,0,s5,4),("HYP",218,0,s4,4),
            ("2ndF",171,9,s4,16),("CAPS",199,9,s4,8),("カナ",237,9,s4,2),("SMALL",260,9,s4,1),
            ("PRINT",265,0,s5,1),("E",307,9,s5,2),("BATT",319,9,s5,128)
        ];
        foreach(var s in symbols)TextAt(g,s.Name,symbolX+s.X*symbolScale,symbolY+s.Y,6,s.Value!=0&&(s.Value&s.Mask)!=0?Ink:Ghost,true);
    }
    void DrawLcd(Graphics g) {
        var ink=Ink;
        float pixelX=IsWide?76:84,pixelY=IsWide?101:113,pixelW=IsWide?432:348,pixelH=IsWide?72:62;
        if(Core is null){TextAt(g,L.T("ROMを開いてください","Open a ROM"),pixelX+6,pixelY+10,12,ink);return;}
        g.SmoothingMode=SmoothingMode.None;
        if(showDotLines) {
            using var brush=new SolidBrush(ink);
            for(int y=0;y<32;y++)for(int x=0;x<240;x++)if(Core.Pixels[y*240+x]!=0)
                g.FillRectangle(brush,pixelX+x*(pixelW/240),pixelY+y*(pixelH/32),pixelW/240*0.82f,pixelH/32*0.86f);
        } else {
            int on=ink.ToArgb();
            for(int i=0;i<lcdArgb.Length;i++)lcdArgb[i]=Core.Pixels[i]!=0?on:0;
            var bits=lcdBitmap.LockBits(new Rectangle(0,0,240,32),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try {Marshal.Copy(lcdArgb,0,bits.Scan0,lcdArgb.Length);} finally {lcdBitmap.UnlockBits(bits);}
            var interpolation=g.InterpolationMode;var pixelOffset=g.PixelOffsetMode;
            g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
            g.DrawImage(lcdBitmap,new Rectangle((int)pixelX,(int)pixelY,(int)pixelW,(int)pixelH),0,0,240,32,GraphicsUnit.Pixel);
            g.InterpolationMode=interpolation;g.PixelOffsetMode=pixelOffset;
        }
        g.SmoothingMode=SmoothingMode.AntiAlias;
    }
    void DrawTransportFrame(Graphics g) {
        float x=IsWide?418:357,y=IsWide?343:325,w=IsWide?90:74,h=IsWide?42:40,half=w/2;
        using var pen=new Pen(Color.FromArgb(237,234,220),1.1f);
        float top=y+8,bottom=y+h;
        g.DrawLine(pen,x,top,x,bottom);g.DrawLine(pen,x,bottom,x+w,bottom);g.DrawLine(pen,x+w,bottom,x+w,top);
        g.DrawLine(pen,x,top,x+5,top);g.DrawLine(pen,x+half-4,top,x+half+4,top);g.DrawLine(pen,x+w-5,top,x+w,top);
        TextAt(g,"PLAY",x+3,y,8,Color.WhiteSmoke,false,half-5);TextAt(g,"BACK",x+half+2,y,8,Color.WhiteSmoke,false,half-5);
    }
    void DrawKey(Graphics g,Key k) {
        var r=k.Bounds;bool down=pressed.Contains(k.Id);if(down)r.Y+=1.2f;
        if(k.Style==7) {
            TextAt(g,"RESET",r.X-8,r.Y-13,6.5f,Color.WhiteSmoke,false,r.Width+16);
            float cx=r.X+r.Width/2,cy=r.Y+r.Height/2;
            using var resetRim=new Pen(Color.FromArgb(193,187,164),1.2f);using var hole=new SolidBrush(Color.FromArgb(24,27,24));
            g.FillEllipse(hole,cx-2.7f,cy-2.7f,5.4f,5.4f);g.DrawEllipse(resetRim,cx-3.2f,cy-3.2f,6.4f,6.4f);
            return;
        }
        if(k.Shift.Length>0)TextAt(g,k.Shift,r.X-6,r.Y-13,8,k.Id is "ON/BRK" or "CAPS"?Color.WhiteSmoke:gold,false,r.Width+12);
        if(k.Extra.Length>0)TextAt(g,k.Extra,r.X-9,r.Y-14,8,k.Style==6?Color.FromArgb(169,188,226):green,false,r.Width+18);
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
        TextAt(g,k.Label,r.X,r.Y+(r.Height-fontSize*1.5f)/2,fontSize,k.Style==2&&!IsWide?Color.FromArgb(60,54,33):Color.FromArgb(245,240,220),k.Style is not 4,r.Width);
        if(k.Lower.Length>0)TextAt(g,k.Lower,r.X,r.Bottom+1,8,Color.WhiteSmoke,false,r.Width);
    }
    internal void TestPaint(Graphics g)=>OnPaint(new PaintEventArgs(g,new Rectangle(0,0,Width,Height)));
    // The LCD with its symbol row, in control pixels, for the manual's close-ups.
    internal Rectangle LcdArea() {
        float x=IsWide?70:78,y=IsWide?74:86,w=IsWide?444:360,h=IsWide?106:96;
        return Rectangle.Round(new RectangleF(ox+x*scale,oy+y*scale,w*scale,h*scale));
    }
    protected override void Dispose(bool disposing) {if(disposing){lcdBitmap.Dispose();chrome?.Dispose();}base.Dispose(disposing);}
}
