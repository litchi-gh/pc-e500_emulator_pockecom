// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing.Drawing2D;
namespace PokeCom;

// The strip along the bottom of the main window, as Windows windows have one:
// the machine's state at the left, a message beside it, and the parts of the
// machine at the right, each in its own field with an icon and a divider
// between. A field that does something (open its window, mute) lights up under
// the mouse and says what it does in a tip. It never takes the focus, so every
// key still goes to the machine.
// メインウィンドウの下のステータスバー（左に本体の状態、その横にメッセージ、右に各部の欄）。
// 押せる欄はマウスが乗ると地が出て、ヒントで何をするかを示す。フォーカスは取らない（キーはいつも本体へ）。
/// <summary>Windows のウィンドウのような、欄に分かれたステータスバー。</summary>
internal sealed class StatusBar : Control
{
    /// <summary>欄1つ：アイコン（'\0' なら点）、文字、点の色、押したときの処理、ヒント、警告か、
    /// 幅が足りないときの短い文字（アイコンで何の欄か分かる形。null なら短くしない）。</summary>
    internal sealed record Part(char Glyph,string Text,Color? Dot=null,Action? Click=null,string? Tip=null,bool Warn=false,string? Short=null);
    // When the fields do not all fit (a narrow window, or a display scaled up, which
    // widens the text more than the window), they drop to their short forms one by
    // one, in the order given, until they do; only if all short is still too wide
    // do the leftmost go.
    /// <summary>短い文字にする欄の順（右の欄の番号）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int[] ShortOrder {get;set;}=[];
    /// <summary>短い文字で描いている欄。</summary>
    readonly HashSet<Part> compact=new();
    string Shown(Part p)=>compact.Contains(p) && p.Short is not null?p.Short:p.Text;
    Part? state;
    Part? message;
    IReadOnlyList<Part> parts=[];
    /// <summary>描いた欄の位置（マウスの当たり判定に使う）。</summary>
    readonly List<(Rectangle Area,Part Part)> areas=new();
    Part? hover;
    readonly ToolTip tip=new(){InitialDelay=400};
    string? shownTip;
    internal StatusBar() {
        SetStyle(ControlStyles.Selectable,false);
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        TabStop=false;Font=Fonts.Small;Dock=DockStyle.Bottom;
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Height=LogicalToDeviceUnits(30);}
    protected override void OnDpiChangedAfterParent(EventArgs e){base.OnDpiChangedAfterParent(e);Height=LogicalToDeviceUnits(30);}
    // Text joins the fields, for the tests and for screen readers.
    /// <summary>中身を入れ替える（変わっていなければ描き直さない）。</summary>
    /// <param name="state">左端の本体の状態</param>
    /// <param name="message">その横のメッセージ（なければ null）</param>
    /// <param name="right">右に並べる欄</param>
    internal void SetParts(Part state,Part? message,IReadOnlyList<Part> right) {
        this.state=state;this.message=message;parts=right;
        Text=string.Join("   ",new[]{state,message}.Concat(right).Where(p=>p is not null).Select(p=>p!.Text));
        Invalidate();
    }
    int Pad=>LogicalToDeviceUnits(8);
    int Gap=>LogicalToDeviceUnits(6);
    int IconWidth=>LogicalToDeviceUnits(16);
    int FieldWidth(Part p)=>Pad*2+IconWidth+Gap+TextRenderer.MeasureText(Shown(p),Font,Size.Empty,TextFormatFlags.NoPadding).Width;
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(Theme.Card);
        using(var line=new Pen(Theme.Border))g.DrawLine(line,0,0,ClientSize.Width,0);
        areas.Clear();
        if(state is null)return;
        int x=0;
        compact.Clear();
        var left=new Rectangle(x,1,FieldWidth(state),ClientSize.Height-1);Draw(g,left,state,false);x=left.Right;
        // A message of a few seconds has the whole bar after the state, as the old
        // single line did: shared, it pushed fields off and back as it came and went.
        // 数秒のメッセージは、状態の右をすべて使う（欄と分け合うと、出るたびに欄が消えたり戻ったりした）。
        if(message is {Warn:false}) {
            Draw(g,new Rectangle(x,1,ClientSize.Width-x,ClientSize.Height-1),message,false);
            Divider(g,left.Right);return;
        }
        // The fields at the right, from the right edge in; a lasting warning has what is left between.
        int right=ClientSize.Width,reserve=message is null?0:LogicalToDeviceUnits(120);
        foreach(int i in ShortOrder.Concat(Enumerable.Range(0,parts.Count))) {
            if(parts.Sum(FieldWidth)<=right-x-reserve)break;
            if(i<parts.Count)compact.Add(parts[i]);
        }
        var placed=new List<(Rectangle,Part)>();
        for(int i=parts.Count-1;i>=0;i--) {
            int w=FieldWidth(parts[i]);
            // Room is kept for a message only while there is one; a narrow window drops the leftmost fields first.
            if(right-w<x+reserve)break;
            right-=w;placed.Add((new Rectangle(right,1,w,ClientSize.Height-1),parts[i]));
        }
        if(message is not null && right-x>Pad*2)Draw(g,new Rectangle(x,1,right-x,ClientSize.Height-1),message,false);
        foreach(var (area,part) in placed)Draw(g,area,part,true);
        Divider(g,left.Right);
        foreach(var (area,_) in placed)Divider(g,area.Left);
    }
    void Divider(Graphics g,int x) {
        int inset=LogicalToDeviceUnits(8);using var pen=new Pen(Theme.Border);g.DrawLine(pen,x,inset,x,ClientSize.Height-inset+1);
    }
    void Draw(Graphics g,Rectangle area,Part part,bool fixedWidth) {
        areas.Add((area,part));
        if(part==hover && part.Click is not null) {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            int inset=LogicalToDeviceUnits(3);
            using var path=Theme.Rounded(new RectangleF(area.X+inset,area.Y+inset,area.Width-inset*2,area.Height-inset*2),4);
            using var brush=new SolidBrush(Theme.SubtleHover);g.FillPath(brush,path);
            g.SmoothingMode=SmoothingMode.None;
        }
        Color warn=Theme.Dark?Color.FromArgb(252,185,0):Color.FromArgb(157,93,0);
        int x=area.X+Pad;var icon=new Rectangle(x,area.Y,IconWidth,area.Height);
        if(part.Glyph=='\0') {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            int d=LogicalToDeviceUnits(8);using var dot=new SolidBrush(part.Dot??Theme.SubText);
            g.FillEllipse(dot,icon.X+(icon.Width-d)/2f,icon.Y+(icon.Height-d)/2f,d,d);
            g.SmoothingMode=SmoothingMode.None;
        } else TextRenderer.DrawText(g,part.Glyph.ToString(),Theme.SmallIconFont,icon,part.Warn?warn:Theme.SubText,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        x+=IconWidth+Gap;
        TextRenderer.DrawText(g,Shown(part),Font,new Rectangle(x,area.Y,area.Right-x-(fixedWidth?0:Pad),area.Height),part.Warn?warn:Theme.Text,
            TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
    }
    Part? At(Point p)=>areas.FirstOrDefault(a=>a.Area.Contains(p)).Part;
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        var now=At(e.Location);
        if(now!=hover){hover=now;Cursor=now?.Click is not null?Cursors.Hand:Cursors.Default;Invalidate();}
        // A message cut short shows in full in its tip.
        string? want=now==message?now?.Text:now is null?null:Shown(now)!=now.Text?now.Text+(now.Tip is null?"":"\n"+now.Tip):now.Tip;
        if(want!=shownTip){shownTip=want;tip.SetToolTip(this,want??"");}
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=null;Cursor=Cursors.Default;Invalidate();}
    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        if(e.Button==MouseButtons.Left && At(e.Location) is {Click: Action click})click();
    }
    protected override void Dispose(bool disposing){if(disposing)tip.Dispose();base.Dispose(disposing);}
    /// <summary>テスト用：欄の文字と、押せるかの一覧。</summary>
    internal IEnumerable<(string Text,bool Clickable)> TestParts=>new[]{state,message}.Concat(parts).Where(p=>p is not null).Select(p=>(p!.Text,p.Click is not null));
    /// <summary>テスト用：文字に text を含む欄を押す。</summary>
    internal void TestClick(string text)=>new[]{state}.Concat(parts).First(p=>p is not null && p.Text.Contains(text,StringComparison.Ordinal))!.Click!();
}
