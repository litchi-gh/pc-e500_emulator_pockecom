// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace PokeCom;

// The look of every window, in the manner of Windows 11: a light or a dark
// scheme (Windows' own choice by default), the accent colour, rounded buttons
// and menus, and icons from the system's icon font. The scheme is chosen once,
// before the first window, as WinForms' own dark mode must be; a change in the
// settings restarts the program, as a new language does.
// ウィンドウの見た目（Windows 11 の作り）：ライトかダーク（既定は Windows の設定に合わせる）、アクセントカラー、
// 角の丸いボタンとメニュー、システムのアイコンフォント。WinForms のダークモードと同じく、最初のウィンドウの前に1回だけ決める。
/// <summary>見た目のテーマ（ライト・ダークの色、ボタン・メニューの描き方、アイコン）。</summary>
internal static class Theme
{
    /// <summary>設定の値：Windows に合わせる・ライト・ダーク。</summary>
    internal static string[] Choices=>HardwareSettings.Themes;
    /// <summary>ダークで描くか。</summary>
    internal static bool Dark {get;private set;}
    /// <summary>テーマを決める（最初のウィンドウを作る前に呼ぶ）。</summary>
    /// <param name="choice">設定の値（System・Light・Dark）</param>
    internal static void Init(string choice) {
        Dark=choice=="Dark" || (choice!="Light" && WindowsDark());
#pragma warning disable WFO5001
        Application.SetColorMode(Dark?SystemColorMode.Dark:SystemColorMode.Classic);
#pragma warning restore WFO5001
        Accent=WindowsAccent();
    }
    /// <summary>Windows のアプリがダークモードか（読めなければライト）。</summary>
    static bool WindowsDark() {
        try {return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is int light && light==0;}
        catch(Exception) {return false;}
    }
    // The accent as Windows keeps it (ABGR); too pale or too dark for the scheme's
    // text on it, the Windows 11 default blue.
    /// <summary>Windows のアクセントカラー（読めないときや、白い文字が読めないほど明るいときは既定の青）。</summary>
    static Color WindowsAccent() {
        try {
            if(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM","AccentColor",null) is int abgr) {
                var c=Color.FromArgb(abgr&0xff,(abgr>>8)&0xff,(abgr>>16)&0xff);
                if(c.GetBrightness() is > 0.15f and < 0.6f)return c;
            }
        } catch(Exception) {}
        return Color.FromArgb(0,103,192);
    }
    /// <summary>アクセントカラー（「適用」などの既定のボタン、選んだ項目）。</summary>
    internal static Color Accent {get;private set;}=Color.FromArgb(0,103,192);
    /// <summary>ウィンドウの地の色。</summary>
    internal static Color Window=>Dark?Color.FromArgb(32,32,32):Color.FromArgb(243,243,243);
    /// <summary>地の上に置く面（本体の絵の後ろ、文字の欄）。</summary>
    internal static Color Card=>Dark?Color.FromArgb(43,43,43):Color.FromArgb(251,251,251);
    /// <summary>文字の入る欄の地。</summary>
    internal static Color Input=>Dark?Color.FromArgb(28,28,28):Color.White;
    /// <summary>文字、薄い文字（説明・ステータス）。</summary>
    internal static Color Text=>Dark?Color.FromArgb(255,255,255):Color.FromArgb(27,27,27);
    internal static Color SubText=>Dark?Color.FromArgb(200,200,200):Color.FromArgb(96,96,96);
    /// <summary>お知らせの文字（アクセントに近い、読める濃さの色）。</summary>
    internal static Color NoteText=>Dark?Color.FromArgb(153,204,255):Color.FromArgb(0,84,166);
    /// <summary>ふちの線。</summary>
    internal static Color Border=>Dark?Color.FromArgb(62,62,62):Color.FromArgb(214,214,214);
    /// <summary>ボタンの地：ふつう・マウスが乗った・押した。</summary>
    internal static Color ButtonFace=>Dark?Color.FromArgb(55,55,55):Color.FromArgb(253,253,253);
    internal static Color ButtonHover=>Dark?Color.FromArgb(62,62,62):Color.FromArgb(246,246,246);
    internal static Color ButtonPressed=>Dark?Color.FromArgb(48,48,48):Color.FromArgb(240,240,240);
    /// <summary>ツールバーのボタン（地のないもの）のマウスが乗った・押したときの地。</summary>
    internal static Color SubtleHover=>Dark?Color.FromArgb(18,255,255,255):Color.FromArgb(10,0,0,0);
    internal static Color SubtlePressed=>Dark?Color.FromArgb(11,255,255,255):Color.FromArgb(6,0,0,0);
    /// <summary>アクセントの地に、マウスが乗った・押したとき。</summary>
    internal static Color AccentHover=>Mix(Accent,Dark?Color.Black:Color.White,0.1f);
    internal static Color AccentPressed=>Mix(Accent,Dark?Color.Black:Color.White,0.2f);
    /// <summary>a と b を t の割合で混ぜた色。</summary>
    internal static Color Mix(Color a,Color b,float t)=>Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));

    // Windows 11 has Segoe Fluent Icons; Windows 10 the older Segoe MDL2 Assets,
    // with the same code points for the icons used here.
    /// <summary>アイコンのフォントの名前（Windows 11 は Segoe Fluent Icons、10 は Segoe MDL2 Assets）。</summary>
    internal static readonly string IconFamily=FontExists("Segoe Fluent Icons")?"Segoe Fluent Icons":"Segoe MDL2 Assets";
    static bool FontExists(string name){using var f=new Font(name,10);return f.Name==name;}
    /// <summary>ツールバー・メニューのアイコンの文字。</summary>
    internal static readonly Font IconFont=new(IconFamily,11),SmallIconFont=new(IconFamily,9);
    /// <summary>アイコンの文字（Segoe Fluent Icons の符号）。</summary>
    internal static class Icons
    {
        internal const char OpenFolder='\uE838',Import='\uE8B5',Pause='\uE769',Play='\uE768',Save='\uE74E',Load='\uE8DA',
            Settings='\uE713',Display='\uE7F4',Drive='\uEDA2',Print='\uE749',Text='\uE8D2',Reset='\uE72C',Bug='\uEBE8',
            Help='\uE897',Speed='\uEC4A',Chip='\uE950',Memory='\uE964',Volume='\uE767',Mute='\uE74F',Info='\uE946',Code='\uE943',Warning='\uE7BA',Download='\uE896',Check='\uE73E',Folder='\uE8B7',Window='\uE737',Minimize='\uE921',Close='\uE8BB',ChevronRight='\uE76C',Game='\uE7FC',ChevronDown='\uE70D';
    }
    // A menu item's picture: the glyph drawn into a bitmap of the menu's image size.
    /// <summary>アイコンの文字を描いた絵（メニューの項目用）。</summary>
    internal static Bitmap Glyph(char glyph,int size,Color color) {
        var b=new Bitmap(size,size);
        using var g=Graphics.FromImage(b);g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var f=new Font(IconFamily,size*0.62f,GraphicsUnit.Pixel);
        TextRenderer.DrawText(g,glyph.ToString(),f,new Rectangle(0,0,size,size),color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        return b;
    }
    /// <summary>角の丸い四角の形。</summary>
    internal static GraphicsPath Rounded(RectangleF r,float radius) {
        var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        if(d<=0){p.AddRectangle(r);return p;}
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);
        p.CloseFigure();return p;
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    const int DWMWA_USE_IMMERSIVE_DARK_MODE=20,DWMWA_WINDOW_CORNER_PREFERENCE=33;
    // Windows 10 and the first Windows 11 builds lack the attributes; they are only asked for.
    /// <summary>ウィンドウの角の丸め方（0 既定・1 丸めない・2 丸める・3 少し丸める。Windows 11 だけ効く）。</summary>
    internal static void SetCorners(IntPtr hwnd,int preference){try{DwmSetWindowAttribute(hwnd,DWMWA_WINDOW_CORNER_PREFERENCE,ref preference,4);}catch(Exception){}}
    /// <summary>タイトルバーをテーマの明るさにする。</summary>
    internal static void SetTitleBar(IntPtr hwnd){int dark=Dark?1:0;try{DwmSetWindowAttribute(hwnd,DWMWA_USE_IMMERSIVE_DARK_MODE,ref dark,4);}catch(Exception){}}

    // The colours a window was given for the old grey look - white text boxes, the
    // blue note - follow the scheme; the rest of the controls take WinForms' own.
    /// <summary>ウィンドウとその中のコントロールに、テーマの色を付ける。</summary>
    internal static void Apply(Control root) {
        if(root is Form f){f.BackColor=Window;f.ForeColor=Text;}
        foreach(Control c in root.Controls)ApplyTo(c);
    }
    static void ApplyTo(Control c) {
        switch(c) {
        case TextBox t: t.BackColor=t.BorderStyle==BorderStyle.None?Card:Input;t.ForeColor=Text;break;
        case NumericUpDown n: n.BackColor=Input;n.ForeColor=Text;break;
        case ComboBox b: b.BackColor=Input;b.ForeColor=Text;break;
        case Label l when l.ForeColor==LegacyNote: l.ForeColor=NoteText;break;
        case TableLayoutPanel t when t.BackColor==Color.White: t.BackColor=Card;break;
        }
        foreach(Control child in c.Controls)ApplyTo(child);
    }
    /// <summary>以前の見た目のお知らせの青（テーマの色に置き換える目印）。</summary>
    internal static readonly Color LegacyNote=Color.FromArgb(0,80,160);
}

// Every sub-window takes the scheme as it opens, with a title bar to match.
/// <summary>テーマの色で開くウィンドウ（サブウィンドウの元）。</summary>
internal class ThemedForm : Form
{
    protected override void OnHandleCreated(EventArgs e) {base.OnHandleCreated(e);Theme.SetTitleBar(Handle);}
    protected override void OnLoad(EventArgs e) {Theme.Apply(this);base.OnLoad(e);}
    // A fixed window takes the height its content asks for, under a footer.
    /// <summary>ウィンドウの高さを、中身と下の帯に合わせる（幅は今のまま）。</summary>
    protected void FitHeight(Control body,Control footer)=>ClientSize=new Size(ClientSize.Width,body.PreferredSize.Height+footer.PreferredSize.Height);
    // A window whose content has a set width gains only blank space from being
    // widened: it keeps the width it opens with (or narrower, if allowed) and only
    // grows downwards. Called once it is laid out, so the width is the scaled one.
    /// <summary>横幅を開いたときの幅までにする（canShrink でなければ狭めることもできない）。高さは変えられる。最大化はなし。</summary>
    internal void LockWidth(bool canShrink=false) {
        MaximizeBox=false;
        MaximumSize=new Size(Width,Screen.FromControl(this).WorkingArea.Height*4);
        if(!canShrink)MinimumSize=new Size(Width,MinimumSize.Height);
    }
    /// <summary>行1つ：左に名前（幅 labelWidth）、右にコントロール。</summary>
    internal static FlowLayoutPanel Row(string title,int labelWidth,params Control[] controls) {
        var row=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=new Padding(0,2,0,2),BackColor=Color.Transparent};
        int height=controls.Length>0?controls[0].PreferredSize.Height:24;
        // At least labelWidth wide, wider when the text (or the display's scaling) asks for it.
        row.Controls.Add(new Label{Text=title,AutoSize=true,MinimumSize=new Size(labelWidth,height+6),TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0,0,6,0)});
        foreach(var c in controls){c.Margin=new Padding(0,3,6,3);row.Controls.Add(c);}
        return row;
    }
}

// A dialog button as Windows 11 draws one: rounded, a hairline border, and the
// accent colour for the button Enter presses.
/// <summary>Windows 11 の作りのボタン（角が丸く、Enter で押す既定のボタンはアクセントカラー）。</summary>
internal class FluentButton : Button
{
    bool hover,down;
    internal FluentButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);FlatStyle=FlatStyle.Flat;}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;down=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){down=true;Invalidate();}base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){down=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
    /// <summary>Enter で押すボタン（アクセントカラーで描く）か。</summary>
    bool Primary=>FindForm()?.AcceptButton==this;
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;
        var r=new RectangleF(0.5f,0.5f,Width-1.5f,Height-1.5f);
        bool primary=Primary && Enabled;
        Color face=primary?(down?Theme.AccentPressed:hover?Theme.AccentHover:Theme.Accent):(down?Theme.ButtonPressed:hover?Theme.ButtonHover:Theme.ButtonFace);
        using(var path=Theme.Rounded(r,4)) {
            using var brush=new SolidBrush(face);g.FillPath(brush,path);
            using var pen=new Pen(primary?Theme.Mix(Theme.Accent,Color.Black,0.15f):Theme.Border);g.DrawPath(pen,path);
        }
        Color text=!Enabled?Theme.SubText:primary?Color.White:Theme.Text;
        TextRenderer.DrawText(g,Text,Font,ClientRectangle,text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        if(Focused && ShowFocusCues) {
            using var focus=Theme.Rounded(new RectangleF(2.5f,2.5f,Width-5.5f,Height-5.5f),3);
            using var pen=new Pen(primary?Color.White:Theme.Text){DashStyle=DashStyle.Dot};g.DrawPath(pen,focus);
        }
    }
}


// A focused Button swallows Space and Enter to activate itself, and both are
// pocket-computer keys. The toolbar refuses focus so every keystroke reaches
// the machine no matter which button was clicked last. It is drawn as a
// Windows 11 command bar button: an icon and its label, with no face until the
// mouse comes over it.
/// <summary>フォーカスを取らないツールバーのボタン（Space・Enter をポケコンのキーとして使うため）。
/// Windows 11 のコマンドバーのように、アイコンと文字だけで、マウスが乗ると地が出る。</summary>
internal sealed class ToolButton : Button
{
    bool hover,down;
    /// <summary>アイコンの文字（なければ '\0'）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal char Glyph {get=>glyph;set{glyph=value;Invalidate();}}
    char glyph;
    /// <summary>目立たせる色（新しい版のお知らせ。空なら目立たせない）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Color Highlight {get;set;}=Color.Empty;
    internal ToolButton(){SetStyle(ControlStyles.Selectable,false);SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);TabStop=false;FlatStyle=FlatStyle.Flat;}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;down=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){down=true;Invalidate();}base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){down=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
    const int Gap=8,Side=11;
    int IconWidth=>glyph=='\0'?0:TextRenderer.MeasureText("\uE700",Theme.IconFont,Size.Empty,TextFormatFlags.NoPadding).Width;
    public override Size GetPreferredSize(Size proposed) {
        var t=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding);
        int icon=IconWidth;
        return new Size(Side*2+icon+(icon>0?Gap:0)+t.Width,Math.Max(36,t.Height+16));
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;
        var r=new RectangleF(0.5f,0.5f,Width-1.5f,Height-1.5f);
        bool marked=!Highlight.IsEmpty;
        Color? face=marked?(down?Theme.Mix(Highlight,Color.Black,0.15f):hover?Theme.Mix(Highlight,Color.White,0.1f):Highlight)
            :down?Theme.SubtlePressed:hover?Theme.SubtleHover:null;
        if(face is Color c) {using var path=Theme.Rounded(r,4);using var brush=new SolidBrush(c);g.FillPath(brush,path);}
        Color ink=marked?Color.Black:down?Theme.SubText:Theme.Text;
        int icon=IconWidth,x=Side;
        if(icon>0) {
            TextRenderer.DrawText(g,glyph.ToString(),Theme.IconFont,new Rectangle(x,0,icon,Height),marked?Color.Black:Theme.Dark?Theme.Mix(Theme.Accent,Color.White,0.45f):Theme.Accent,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPadding);
            x+=icon+Gap;
        }
        TextRenderer.DrawText(g,Text,Font,new Rectangle(x,0,Width-x,Height),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
    }
}

// The right-click menu as Windows 11 draws its own: a plain face with a hairline
// border and rounded corners, roomy rows, and a rounded highlight inset from the
// edges.
/// <summary>Windows 11 の作りの右クリックメニュー（角の丸い枠、ゆったりした行、内側に丸めた選択の地）。</summary>
internal sealed class FluentMenuRenderer : ToolStripProfessionalRenderer
{
    internal FluentMenuRenderer():base(new Colors()){RoundedEdges=false;}
    sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground=>Theme.Dark?Color.FromArgb(44,44,44):Color.FromArgb(249,249,249);
        public override Color ImageMarginGradientBegin=>ToolStripDropDownBackground;
        public override Color ImageMarginGradientMiddle=>ToolStripDropDownBackground;
        public override Color ImageMarginGradientEnd=>ToolStripDropDownBackground;
        public override Color MenuBorder=>Theme.Dark?Color.FromArgb(70,70,70):Color.FromArgb(200,200,200);
        public override Color SeparatorDark=>Theme.Border;
        public override Color SeparatorLight=>Color.Transparent;
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
        if(!e.Item.Selected || !e.Item.Enabled)return;
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        using var path=Theme.Rounded(new RectangleF(4,1,e.Item.Width-8,e.Item.Height-2),4);
        using var brush=new SolidBrush(Theme.Dark?Color.FromArgb(61,61,61):Color.FromArgb(234,234,234));g.FillPath(brush,path);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
        e.TextColor=e.Item.Enabled?Theme.Text:Theme.SubText;base.OnRenderItemText(e);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
        TextRenderer.DrawText(e.Graphics,Theme.Icons.ChevronRight.ToString(),Theme.SmallIconFont,e.ArrowRectangle,Theme.SubText,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
    }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
        var r=e.ImageRectangle;
        TextRenderer.DrawText(e.Graphics,Theme.Icons.Check.ToString(),Theme.SmallIconFont,new Rectangle(r.X-2,r.Y-2,r.Width+4,r.Height+4),Theme.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
        using var pen=new Pen(Theme.Border);int y=e.Item.Height/2;e.Graphics.DrawLine(pen,4,y,e.Item.Width-4,y);
    }
    // The menu's own window is rounded by Windows 11 (asked for when it opens); the border follows it.
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        using var pen=new Pen(ColorTable.MenuBorder);
        g.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);
    }
    /// <summary>メニューにこの描き方を付け、開くたびに角を丸める（下の階層も）。</summary>
    internal static void Use(ToolStrip strip) {
        strip.Renderer=new FluentMenuRenderer();
        if(strip is ToolStripDropDown drop)drop.Opened+=(_,_)=>Theme.SetCorners(drop.Handle,3);
        strip.Padding=new Padding(2,4,2,4);
        foreach(ToolStripItem item in strip.Items)Style(item);
    }
    static void Style(ToolStripItem item) {
        if(item is ToolStripMenuItem m) {
            m.Padding=new Padding(4,5,4,5);
            if(m.HasDropDownItems) {
                m.DropDown.Renderer=new FluentMenuRenderer();m.DropDown.Padding=new Padding(2,4,2,4);
                m.DropDown.Opened+=(_,_)=>Theme.SetCorners(m.DropDown.Handle,3);
                foreach(ToolStripItem child in m.DropDownItems)Style(child);
            }
        }
    }
}

// The pieces every sub-window is built from, so they all look alike: content
// sits on rounded cards on the window's ground, a card may carry a heading,
// notes are in the quieter text colour, and the buttons that end the window
// line up at the bottom right in a footer, the one Enter presses first.
// サブウィンドウの共通の部品：地の上の角の丸い面（見出しを持てる）、控えめな色の説明、右下にそろえたボタンの帯。
/// <summary>角の丸い面（中に見出しを描ける）。</summary>
internal class CardPanel : Panel
{
    string title="";
    /// <summary>面の左上に描く見出し（空なら描かない）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string Title {get=>title;set{title=value;ApplyPadding();Invalidate();}}
    internal CardPanel() {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        BackColor=Theme.Card;ApplyPadding();
    }
    void ApplyPadding(){int s=LogicalToDeviceUnits(12);Padding=new Padding(s,title.Length>0?s+Fonts.UiBold.Height+LogicalToDeviceUnits(6):s,s,s);}
    protected override void OnPaintBackground(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Window);g.SmoothingMode=SmoothingMode.AntiAlias;
        using var path=Theme.Rounded(new RectangleF(0.5f,0.5f,Width-1.5f,Height-1.5f),LogicalToDeviceUnits(6));
        using(var brush=new SolidBrush(Theme.Card))g.FillPath(brush,path);
        using(var pen=new Pen(Theme.Border))g.DrawPath(pen,path);
        if(title.Length>0)TextRenderer.DrawText(g,title,Fonts.UiBold,new Point(LogicalToDeviceUnits(12),LogicalToDeviceUnits(10)),Theme.Text,TextFormatFlags.NoPadding);
    }
    protected override void OnPaint(PaintEventArgs e){}
    // A card that sizes itself to a column of controls: the stack sits inside the
    // padding, and the card grows with it.
    /// <summary>中身を縦に並べる欄を作って返す（面はその大きさに合わせて伸びる）。</summary>
    /// <param name="width">中身の幅</param>
    internal FlowLayoutPanel Stack(int width) {
        var flow=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,
            BackColor=Theme.Card,Location=new Point(Padding.Left,Padding.Top),Margin=new Padding(0),MinimumSize=new Size(width,0),MaximumSize=new Size(width,0)};
        Controls.Add(flow);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        return flow;
    }
}

/// <summary>ウィンドウの下の帯：右にボタン（先に足したものほど右。「閉じる」を最初に）、左にチェックや結果の文。</summary>
internal sealed class FooterBar : TableLayoutPanel
{
    readonly FlowLayoutPanel left=new(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=new Padding(0),Anchor=AnchorStyles.Left};
    readonly FlowLayoutPanel right=new(){AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,FlowDirection=FlowDirection.RightToLeft,Margin=new Padding(0),Anchor=AnchorStyles.Right};
    internal FooterBar() {
        Dock=DockStyle.Bottom;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;ColumnCount=2;RowCount=1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Padding=new Padding(16,12,12,12);
        Controls.Add(left,0,0);Controls.Add(right,1,0);
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }
    /// <summary>右にボタンを足す（先に足したものほど右）。</summary>
    internal FluentButton Add(string text,EventHandler? click=null,DialogResult result=DialogResult.None) {
        var b=new FluentButton{Text=text,AutoSize=true,MinimumSize=new Size(LogicalToDeviceUnits(88),0),Margin=new Padding(LogicalToDeviceUnits(8),0,0,0),DialogResult=result};
        if(click is not null)b.Click+=click;
        right.Controls.Add(b);return b;
    }
    /// <summary>左にコントロールを足す（チェックや結果の文）。</summary>
    internal T AddLeft<T>(T control) where T:Control {
        control.Margin=new Padding(0,LogicalToDeviceUnits(6),LogicalToDeviceUnits(12),0);
        left.Controls.Add(control);return control;
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);using var pen=new Pen(Theme.Border);e.Graphics.DrawLine(pen,0,0,Width,0);
    }
}

/// <summary>説明の文（控えめな色、折り返す）。</summary>
internal sealed class NoteLabel : Label
{
    internal NoteLabel(string text,int width) {
        Text=text;AutoSize=true;MaximumSize=new Size(width,0);ForeColor=Theme.SubText;Font=Fonts.Small;Margin=new Padding(0,4,0,4);
    }
}
