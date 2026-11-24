// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing.Drawing2D;
namespace PokeCom;

// One RAM slot as a bar, low addresses at the left: each region in its kind's
// colour, wide in proportion to its size but never too thin to see or to point
// at; its name inside where it fits. Hovering tells the region's range and size;
// clicking one is reported (the debugger jumps there). A legend of the kinds
// present runs underneath.
// RAM のスロット1つを横の帯で描く（左が低い番地）。区画は種類の色で、大きさに比べた幅（細すぎないよう下限あり）。
// マウスを乗せると範囲と大きさ、押すと知らせる（デバッグウィンドウはそこへ飛ぶ）。下に凡例。
/// <summary>メモリマップの帯。</summary>
internal sealed class MemoryMapView : Control
{
    IReadOnlyList<Region> regions=[];
    readonly List<(Rectangle Area,Region Region)> laid=new();
    Region? hover;
    readonly ToolTip tip=new(){InitialDelay=200};
    /// <summary>帯の上に出す見出し（「内蔵 RAM 32KB」など）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string Title {get;set;}="";
    /// <summary>目立たせる種類（機械語エリアのウィンドウでは機械語エリア）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal RegionKind? Emphasis {get;set;}
    /// <summary>凡例を描くか。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Legend {get;set;}=true;
    /// <summary>帯の下に印を付ける番地（デバッグウィンドウの表示位置。-1 なら付けない）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int Marker {get=>marker;set{if(marker!=value){marker=value;Invalidate();}}}
    int marker=-1;
    /// <summary>区画が押された。</summary>
    internal event Action<Region>? RegionClicked;
    internal MemoryMapView() {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        SetStyle(ControlStyles.Selectable,false);TabStop=false;Font=Fonts.Small;
    }
    /// <summary>区画を入れ替える（同じなら描き直さない）。</summary>
    internal void SetRegions(IReadOnlyList<Region> value){if(regions.SequenceEqual(value))return;regions=value;Invalidate();}
    internal IReadOnlyList<Region> Regions=>regions;
    int S(int v)=>LogicalToDeviceUnits(v);
    int TitleHeight=>Title.Length>0?Font.Height+S(4):0;
    int BarHeight=>S(30);
    /// <summary>この幅で要る高さ（見出し・帯・印・凡例）。</summary>
    internal int PreferredHeight=>TitleHeight+BarHeight+S(10)+Font.Height+S(8)+(Legend?LegendHeight(Width>0?Width:S(600)):0);
    int LegendHeight(int width) {
        int lines=1,x=0;
        foreach(var k in Kinds()){int w=LegendWidth(k);if(x>0 && x+w>width){lines++;x=0;}x+=w;}
        return lines*(Font.Height+S(4));
    }
    IEnumerable<RegionKind> Kinds()=>regions.Select(r=>r.Kind).Distinct().OrderBy(k=>k);
    int LegendWidth(RegionKind k)=>S(14)+TextRenderer.MeasureText(RamLayout.KindName(k),Font,Size.Empty,TextFormatFlags.NoPadding).Width+S(14);
    int[] Widths(int total)=>RamLayout.Widths(regions,total,S(6));
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Card);
        laid.Clear();if(regions.Count==0)return;
        int y=0;
        if(Title.Length>0){TextRenderer.DrawText(g,Title,Fonts.UiBold,new Point(0,0),Theme.Text,TextFormatFlags.NoPadding);y+=TitleHeight;}
        var bar=new Rectangle(0,y,Width-1,BarHeight);
        var widths=Widths(bar.Width);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var clip=Theme.Rounded(bar,S(6))) {
            g.SetClip(clip);
            int x=bar.X;
            for(int i=0;i<regions.Count;i++) {
                var r=regions[i];var area=new Rectangle(x,bar.Y,widths[i],bar.Height);x+=widths[i];
                laid.Add((area,r));
                var color=RamLayout.Color(r.Kind,Theme.Dark);
                if(Emphasis is RegionKind em && r.Kind!=em && r.Kind!=RegionKind.Free)color=Theme.Mix(color,Theme.Card,0.45f);
                using(var brush=new SolidBrush(color))g.FillRectangle(brush,area);
                if(r.Kind==RegionKind.Free) {
                    using var hatch=new HatchBrush(HatchStyle.WideUpwardDiagonal,Theme.Mix(color,Theme.Dark?Color.White:Color.Black,0.07f),color);
                    g.FillRectangle(hatch,area);
                }
                if(r==hover){using var lit=new SolidBrush(Color.FromArgb(50,255,255,255));g.FillRectangle(lit,area);}
                using(var sep=new Pen(Parent?.BackColor??Theme.Card))g.DrawLine(sep,area.Right,area.Top,area.Right,area.Bottom);
                // The name inside, where it fits: white on the strong colours, plain text on the free space.
                var text=r.Name+"  "+RamLayout.Size(r.Size);
                var fit=TextRenderer.MeasureText(text,Font,Size.Empty,TextFormatFlags.NoPadding);
                if(fit.Width+S(8)>area.Width){text=r.Name;fit=TextRenderer.MeasureText(text,Font,Size.Empty,TextFormatFlags.NoPadding);}
                if(fit.Width+S(8)<=area.Width)
                    TextRenderer.DrawText(g,text,Font,area,r.Kind==RegionKind.Free?Theme.SubText:Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
            }
            g.ResetClip();
        }
        using(var edge=Theme.Rounded(new RectangleF(bar.X+0.5f,bar.Y+0.5f,bar.Width-1,bar.Height-1),S(6)))using(var pen=new Pen(Theme.Border))g.DrawPath(pen,edge);
        // The addresses at the two ends, and the marker under the bar.
        y=bar.Bottom+S(10);
        TextRenderer.DrawText(g,$"{regions[0].Start:X5}",Font,new Point(0,y),Theme.SubText,TextFormatFlags.NoPadding);
        string top=$"{regions[^1].End-1:X5}";
        TextRenderer.DrawText(g,top,Font,new Point(Width-TextRenderer.MeasureText(top,Font,Size.Empty,TextFormatFlags.NoPadding).Width-S(2),y),Theme.SubText,TextFormatFlags.NoPadding);
        if(marker>=0 && laid.FirstOrDefault(l=>marker>=l.Region.Start && marker<l.Region.End) is {Region: not null} hit) {
            float mx=hit.Area.X+hit.Area.Width*(float)(marker-hit.Region.Start)/Math.Max(1,hit.Region.Size);
            using var brush=new SolidBrush(Theme.Text);
            float s=S(5);g.FillPolygon(brush,[new PointF(mx,bar.Bottom+1),new PointF(mx-s,bar.Bottom+1+s*1.4f),new PointF(mx+s,bar.Bottom+1+s*1.4f)]);
        }
        if(!Legend)return;
        y=bar.Bottom+S(10)+Font.Height+S(8);int lx=0;
        foreach(var k in Kinds()) {
            int w=LegendWidth(k);
            if(lx>0 && lx+w>Width){lx=0;y+=Font.Height+S(4);}
            var sw=new Rectangle(lx,y+(Font.Height-S(10))/2,S(10),S(10));
            using(var path=Theme.Rounded(sw,S(2)))using(var brush=new SolidBrush(RamLayout.Color(k,Theme.Dark)))g.FillPath(brush,path);
            TextRenderer.DrawText(g,RamLayout.KindName(k),Font,new Point(lx+S(14),y),Theme.SubText,TextFormatFlags.NoPadding);
            lx+=w;
        }
    }
    Region? At(Point p)=>laid.FirstOrDefault(l=>l.Area.Contains(p)).Region;
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        var now=At(e.Location);if(now==hover)return;
        hover=now;Invalidate();
        Cursor=now is not null && RegionClicked is not null?Cursors.Hand:Cursors.Default;
        tip.SetToolTip(this,now is null?"":$"{now.Name}\n{now.Start:X5}〜{now.End-1:X5}  ({now.Size:X}h = {now.Size:N0} {L.T("バイト","bytes")})");
    }
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=null;Invalidate();}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left && At(e.Location) is {} r)RegionClicked?.Invoke(r);}
    protected override void Dispose(bool disposing){if(disposing)tip.Dispose();base.Dispose(disposing);}
}
