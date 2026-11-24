// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing.Drawing2D;
namespace PokeCom;
using Part=PadLayout.Part;
using Shape=PadLayout.Shape;

// A plain drawing of a two-grip pad, laid out for the kind in use, so a button
// pressed shows where it is. An Xbox-style pad (XInput) has its names; a
// DirectInput pad has only numbers, put where most of them have them (1-4 the
// face buttons left, bottom, right, top; 5-8 the shoulders; 9 and 10 the middle;
// 11 and 12 the sticks pressed; 13 the home button) - the light shows the truth.
/// <summary>コントローラーの絵。押されているボタンを光らせ、スティックの倒し具合とトリガーの引き具合を描く。
/// 部分を押すとその操作を知らせ、マウスを重ねると割り当てを出す。</summary>
internal sealed class PadView : Control
{
    readonly List<Part> parts=new();
    PadDevice? device;
    bool live;
    readonly HashSet<string> pressed=new();
    readonly PadAxes axes=new();
    readonly ToolTip tip=new(){InitialDelay=300};
    string? tipText;
    Dictionary<string,int> layout=PadConfig.StandardLayout();
    string? target;
    /// <summary>操作の割り当て（マウスを重ねたときに出す。なければ null）。</summary>
    internal Func<string,string?>? BindingOf;
    /// <summary>部分が押されたとき（その部分の最初の操作）。</summary>
    internal event Action<string>? ControlClicked;
    internal static string[] Positions=>PadLayout.Positions;
    internal static string PositionName(string pos)=>PadLayout.PositionName(pos);
    internal PadView() {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        SetStyle(ControlStyles.Selectable,false);
        Size=new Size(560,270);
        Build();
    }
    /// <summary>描くコントローラー（null なら Xbox 系の並び）、つながっているか（つながっていなければ薄く描く）、DirectInput のボタンの位置。</summary>
    internal void SetDevice(PadDevice? d,bool connected,Dictionary<string,int>? buttons=null){device=d;live=connected;layout=buttons??PadConfig.StandardLayout();target=null;pressed.Clear();axes.Clear();Build();Invalidate();}
    /// <summary>位置合わせで尋ねている位置（null なら位置合わせ中でない）。位置合わせ中は、すべての位置を描く。</summary>
    internal void SetTarget(string? pos,Dictionary<string,int> buttons){target=pos;layout=buttons;Build();Invalidate();}
    /// <summary>押されている操作と軸の量を入れる（変わったときだけ描き直す）。</summary>
    internal void SetState(IEnumerable<string> now,PadAxes a) {
        var set=now as HashSet<string>??new HashSet<string>(now);
        if(pressed.SetEquals(set) && axes.Same(a))return;
        pressed.Clear();pressed.UnionWith(set);a.CopyTo(axes);Invalidate();
    }
    /// <summary>絵にある操作（テスト用）。</summary>
    internal IEnumerable<string> Drawn=>parts.SelectMany(p=>p.Controls).Where(c=>c.Length>0);
    /// <summary>位置にあるボタンの番号の文字（テスト用。なければ null）。</summary>
    internal string? LabelAt(string pos)=>parts.FirstOrDefault(p=>p.Pos==pos)?.Label;

    static RectangleF Circle(float x,float y,float r)=>new(x-r,y-r,r*2,r*2);
    void Build(){parts.Clear();parts.AddRange(PadLayout.Parts(device,layout,target is not null));}

    float scale=1;PointF origin;
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(BackColor);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        // The drawing fills the box: the body and every part, with a little room.
        var bounds=new RectangleF(70,0,420,232);foreach(var p in parts)bounds=RectangleF.Union(bounds,p.Area);bounds.Inflate(6,6);
        scale=Math.Min(Width/bounds.Width,Height/bounds.Height);origin=new((Width-bounds.Width*scale)/2-bounds.X*scale,(Height-bounds.Height*scale)/2-bounds.Y*scale);
        g.TranslateTransform(origin.X,origin.Y);g.ScaleTransform(scale,scale);
        bool dark=Theme.Dark;
        Color body=dark?Color.FromArgb(62,62,68):Color.FromArgb(222,224,229),line=dark?Color.FromArgb(105,105,112):Color.FromArgb(160,163,170),
            face=dark?Color.FromArgb(36,36,40):Color.FromArgb(250,250,252),on=Theme.Accent;
        using var linePen=new Pen(line,1.5f);using var ringPen=new Pen(on,3.5f);
        using var faceBrush=new SolidBrush(face);using var onBrush=new SolidBrush(on);using var textBrush=new SolidBrush(Theme.Text);using var onText=new SolidBrush(Color.White);
        using var font=new Font(Fonts.Ui.FontFamily,12,FontStyle.Bold,GraphicsUnit.Pixel);using var small=new Font(Fonts.Ui.FontFamily,10,GraphicsUnit.Pixel);
        var center=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
        bool Lit(Part p)=>p.Controls.Any(pressed.Contains);
        void Label(Part p,Font f,bool lit){if(p.Label.Length>0)g.DrawString(p.Label,f,lit?onText:textBrush,p.Area,center);}
        void Draw(Part p) {
            bool lit=Lit(p);
            switch(p.Shape) {
            case Shape.Round:
                g.FillEllipse(lit?onBrush:faceBrush,p.Area);g.DrawEllipse(linePen,p.Area);Label(p,p.Area.Width<24?small:font,lit);break;
            case Shape.Pill: case Shape.Arm: {
                using var path=Theme.Rounded(p.Area,p.Shape==Shape.Pill?p.Area.Height/2:4);
                g.FillPath(lit?onBrush:faceBrush,path);g.DrawPath(linePen,path);Label(p,small,lit);break;
            }
            case Shape.Hub: g.FillRectangle(faceBrush,RectangleF.Inflate(p.Area,0.5f,0.5f));break;
            case Shape.Trigger: {
                using var path=Theme.Rounded(p.Area,7);
                g.FillPath(faceBrush,path);
                // How far it is pulled, from the bottom up; past the point that counts, all of it.
                float pull=lit?1:p.Right?axes.RT:axes.LT;
                if(pull>0.02f){var r=p.Area;var fill=new RectangleF(r.X,r.Bottom-r.Height*pull,r.Width,r.Height*pull);var keep=g.Clip;g.SetClip(path);g.FillRectangle(onBrush,fill);g.Clip=keep;}
                g.DrawPath(linePen,path);Label(p,small,lit);break;
            }
            case Shape.Stick: {
                // The ring lights for a direction that counts; the cap moves with the stick and lights when pressed in.
                bool tilted=p.Controls.Take(4).Any(pressed.Contains),pushed=p.Controls.Skip(4).Any(pressed.Contains);
                g.FillEllipse(faceBrush,p.Area);g.DrawEllipse(tilted?ringPen:linePen,p.Area);
                float r=p.Area.Width/2,cx=p.Area.X+r,cy=p.Area.Y+r,dx=(p.Right?axes.RX:axes.LX)*r*0.45f,dy=(p.Right?axes.RY:axes.LY)*r*0.45f;
                var cap=Circle(cx+dx,cy+dy,r*0.58f);
                using(var capBrush=new SolidBrush(pushed?on:body))g.FillEllipse(capBrush,cap);
                g.DrawEllipse(linePen,cap);
                g.DrawString(p.Label,small,pushed?onText:textBrush,cap,center);
                break;
            }
            }
        }
        foreach(var p in parts.Where(p=>p.Area.Y<40))Draw(p);
        // The body: two grips and the middle, outlined together, then filled over the inner lines.
        using(var mid=Theme.Rounded(new RectangleF(115,40,330,138),46))
        using(var grips=new GraphicsPath()) {
            grips.AddEllipse(70,82,150,150);grips.AddEllipse(340,82,150,150);
            using var outline=new Pen(line,3f);using var fill=new SolidBrush(body);
            g.DrawPath(outline,mid);g.DrawPath(outline,grips);
            g.FillPath(fill,grips);g.FillPath(fill,mid);
        }
        foreach(var p in parts.Where(p=>p.Area.Y>=40))Draw(p);
        // The place asked for while learning: a thick orange ring around it.
        if(target is not null && parts.FirstOrDefault(p=>p.Pos==target) is Part asked) {
            using var mark=new Pen(Color.FromArgb(240,120,0),4f);
            var ring=RectangleF.Inflate(asked.Area,6,6);
            if(asked.Shape is Shape.Round or Shape.Stick)g.DrawEllipse(mark,ring);else{using var path=Theme.Rounded(ring,ring.Height/2);g.DrawPath(mark,path);}
        }
        g.ResetTransform();
        if(!live) {
            // Not connected: the drawing stays, faded, with a word over it.
            using var veil=new SolidBrush(Color.FromArgb(150,BackColor));g.FillRectangle(veil,ClientRectangle);
            using var big=new Font(Fonts.Ui.FontFamily,14*scale,FontStyle.Bold,GraphicsUnit.Pixel);
            using var sub=new SolidBrush(Theme.SubText);
            g.DrawString(L.T("つながっていません","Not connected"),big,sub,ClientRectangle,center);
        }
    }
    /// <summary>画面の点にある部分（なければ null）。</summary>
    Part? At(Point p) {
        var d=new PointF((p.X-origin.X)/scale,(p.Y-origin.Y)/scale);
        return parts.LastOrDefault(x=>x.Controls.Length>0 && RectangleF.Inflate(x.Area,3,3).Contains(d));
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        var p=At(e.Location);
        Cursor=p is null?Cursors.Default:Cursors.Hand;
        string? text=p is null?null:string.Join("\n",p.Controls.Where(c=>c.Length>0).Select(c=>$"{GamePad.ControlName(c)} → {BindingOf?.Invoke(c)??L.T("なし","none")}"));
        if(text==tipText)return;
        tipText=text;tip.SetToolTip(this,text??"");
    }
    protected override void OnMouseClick(MouseEventArgs e) {
        base.OnMouseClick(e);
        if(At(e.Location) is Part p)ControlClicked?.Invoke(p.Controls[0]);
    }
    protected override void Dispose(bool disposing){if(disposing)tip.Dispose();base.Dispose(disposing);}
}
