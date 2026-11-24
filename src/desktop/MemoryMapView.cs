// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing.Drawing2D;
namespace PokeCom;

/// <summary>メモリの区画の種類（色分けに使う）。</summary>
internal enum RegionKind {Header,Variables,Program,Block,RamDisk,Free,Stack,Work,MachineCode,System}

/// <summary>メモリの区画1つ：先頭、終わり（含まない）、種類、名前。</summary>
internal sealed record Region(int Start,int End,RegionKind Kind,string Name)
{
    internal int Size=>End-Start;
}

// How a RAM slot is laid out, read from the machine: the slot header, the chain
// of named blocks with the free space one of them holds, and for the internal
// RAM the work areas, stacks and machine-code area the pointer table at BFCDEh
// gives, up to the system's own bytes from BFC00h (Analysis/MEMORY.md §2-§4).
// RAM のスロットの並びを本体から読む：スロットの見出し、名前付きのブロックの並びと空き、内蔵 RAM ではさらに
// BFCDEh からのポインタ表が示す作業域・スタック・機械語エリアと、BFC00h からのシステムの番地。
/// <summary>RAM の区画の並びを読む。</summary>
internal static class RamLayout
{
    const int TableAt=0xbfcde,TableCount=21,SystemAt=0xbfc00;
    static int Le3(ReadOnlySpan<byte> b)=>b[0]|b[1]<<8|b[2]<<16;
    /// <summary>内蔵 RAM の並び（読めない所は「不明」の1区画にする）。</summary>
    internal static List<Region> Internal(Core core) {
        int bottom=0xc0000-core.InternalKB*1024;
        var regions=new List<Region>();
        int chainEnd=Chain(core,bottom,SystemAt,regions);
        var table=core.Dump(TableAt,TableCount*3);
        var p=Enumerable.Range(0,TableCount).Select(i=>Le3(table.AsSpan(i*3))).ToArray();
        bool ordered=p[0]>=chainEnd && p[^1]<=SystemAt && p.Zip(p.Skip(1)).All(x=>x.First<=x.Second);
        if(!ordered){regions.Add(new(chainEnd,SystemAt,RegionKind.Header,L.T("不明","Unknown")));}
        else {
            if(p[0]>chainEnd)regions.Add(new(chainEnd,p[0],RegionKind.Header,L.T("ブロックの終わり","End of blocks")));
            for(int i=0;i<TableCount-1;i++) {
                if(p[i+1]<=p[i])continue;
                var (kind,name)=i switch{0=>(RegionKind.Stack,L.T("U スタック","U stack")),1=>(RegionKind.Stack,L.T("S スタック","S stack")),
                    16=>(RegionKind.Work,L.T("BASIC 作業域","BASIC work")),19=>(RegionKind.Work,L.T("IOCS 作業域","IOCS work")),_=>(RegionKind.Work,L.T("作業域","Work area"))};
                regions.Add(new(p[i],p[i+1],kind,name));
            }
            regions.Add(new(p[^1],SystemAt,RegionKind.MachineCode,L.T("機械語エリア","Machine code")));
        }
        regions.Add(new(SystemAt,0xc0000,RegionKind.System,L.T("システム","System")));
        // Empty ones (the machine-code area before any is taken) are not drawn at all.
        return regions.Where(r=>r.Size>0).ToList();
    }
    /// <summary>RAM カードの並び（初期化されていなければ1区画）。</summary>
    internal static List<Region> Card(Core core) {
        int bottom=0x40000,top=bottom+core.CardKB*1024;
        var regions=new List<Region>();
        var head=core.Dump(bottom,2);
        if(head[0]!=0x10 || head[1]!=0x12){regions.Add(new(bottom,top,RegionKind.Free,L.T("未初期化","Not set up")));return regions;}
        int end=Chain(core,bottom,top,regions);
        if(end<top)regions.Add(new(end,top,RegionKind.Free,L.T("使っていない","Unused")));
        return regions;
    }
    /// <summary>スロットの見出しとブロックの並びを regions に足し、並びの終わりを返す。</summary>
    static int Chain(Core core,int bottom,int limit,List<Region> regions) {
        regions.Add(new(bottom,bottom+0x18,RegionKind.Header,L.T("スロットの見出し","Slot header")));
        int at=bottom+0x18;
        for(int n=0;n<64 && at<limit-0x22;n++) {
            var h=core.Dump(at,0x1c);
            if(h[0]!=0xfb)break;
            int size=Le3(h.AsSpan(0x11)),used=Le3(h.AsSpan(0x16));
            if(size<used || used<0x1c || at+size>limit)break;
            string name=System.Text.Encoding.ASCII.GetString(h,1,8).TrimEnd();
            var (kind,shown)=name switch{"DATA"=>(RegionKind.Variables,L.T("変数","Variables")),"TEXT"=>(RegionKind.Program,L.T("プログラム","Program")),
                "RAMFILE"=>(RegionKind.RamDisk,L.T("RAM ディスク","RAM disk")),"FUNCKEY"=>(RegionKind.Block,L.T("PF キー","PF keys")),_=>(RegionKind.Block,name)};
            regions.Add(new(at,at+used,kind,shown));
            if(size>used)regions.Add(new(at+used,at+size,RegionKind.Free,L.T("空き","Free")));
            at+=size;
        }
        return at;
    }
    // The ROM's reservation moves everything from the top of the free space up
    // to the machine-code area down by the change, and the free space shrinks:
    // what the map will look like once a new size is applied.
    /// <summary>機械語エリアを size にしたあとの並び（空きの上から機械語エリアまでが下へずれる）。</summary>
    internal static List<Region> WithMachineCode(List<Region> regions,int size) {
        // None taken yet: an empty one just below the system bytes, to grow from.
        if(regions.All(r=>r.Kind!=RegionKind.MachineCode) && regions.FindIndex(r=>r.Kind==RegionKind.System) is int sys and >=0) {
            regions=[..regions];regions.Insert(sys,new(regions[sys].Start,regions[sys].Start,RegionKind.MachineCode,L.T("機械語エリア","Machine code")));
        }
        var ml=regions.FirstOrDefault(r=>r.Kind==RegionKind.MachineCode);
        var free=regions.Where(r=>r.Kind==RegionKind.Free).OrderByDescending(r=>r.Size).FirstOrDefault();
        if(ml is null || free is null)return regions;
        int delta=size-ml.Size;
        return regions.Select(r=>
            r==free?r with{End=r.End-delta}
            :r.Kind==RegionKind.MachineCode?r with{Start=r.Start-delta}
            :r.Start>=free.End && r.Kind!=RegionKind.System?r with{Start=r.Start-delta,End=r.End-delta}
            :r).Where(r=>r.Size>0).ToList();
    }
    /// <summary>区画の種類の色。</summary>
    internal static Color Color(RegionKind kind)=>kind switch{
        RegionKind.Program=>Theme.Dark?System.Drawing.Color.FromArgb(76,160,255):System.Drawing.Color.FromArgb(0,103,192),
        RegionKind.Variables=>Theme.Dark?System.Drawing.Color.FromArgb(56,196,186):System.Drawing.Color.FromArgb(0,153,143),
        RegionKind.Block=>Theme.Dark?System.Drawing.Color.FromArgb(150,150,200):System.Drawing.Color.FromArgb(110,110,160),
        RegionKind.RamDisk=>Theme.Dark?System.Drawing.Color.FromArgb(120,200,110):System.Drawing.Color.FromArgb(70,150,60),
        RegionKind.Free=>Theme.Dark?System.Drawing.Color.FromArgb(58,58,58):System.Drawing.Color.FromArgb(232,232,232),
        RegionKind.Stack=>Theme.Dark?System.Drawing.Color.FromArgb(190,150,240):System.Drawing.Color.FromArgb(135,100,184),
        RegionKind.Work=>Theme.Dark?System.Drawing.Color.FromArgb(160,130,210):System.Drawing.Color.FromArgb(160,130,200),
        RegionKind.MachineCode=>Theme.Dark?System.Drawing.Color.FromArgb(255,150,60):System.Drawing.Color.FromArgb(230,110,10),
        RegionKind.System=>Theme.Dark?System.Drawing.Color.FromArgb(110,110,110):System.Drawing.Color.FromArgb(120,120,120),
        _=>Theme.Dark?System.Drawing.Color.FromArgb(90,90,90):System.Drawing.Color.FromArgb(180,180,180)};
    /// <summary>区画の種類の名前（凡例に使う）。</summary>
    internal static string KindName(RegionKind kind)=>kind switch{
        RegionKind.Program=>L.T("プログラム","Program"),RegionKind.Variables=>L.T("変数","Variables"),RegionKind.Block=>L.T("PFキー・AER など","PF keys, AER..."),
        RegionKind.RamDisk=>L.T("RAM ディスク","RAM disk"),RegionKind.Free=>L.T("空き","Free"),RegionKind.Stack=>L.T("スタック","Stacks"),RegionKind.Work=>L.T("作業域","Work areas"),
        RegionKind.MachineCode=>L.T("機械語エリア","Machine code"),RegionKind.System=>L.T("システム","System"),_=>L.T("見出しなど","Headers")};
    /// <summary>大きさを読みやすく（1KB 以上は KB、小数1桁まで）。</summary>
    internal static string Size(int n)=>n>=1024?(n%1024==0?$"{n/1024}KB":$"{n/1024.0:0.0}KB"):$"{n}B";
}

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
    // Widths: each region gets its share of the bar, but none less than a few
    // pixels; what the small ones are given comes out of the large ones.
    /// <summary>区画ごとの幅（大きさの割合、細いものは下限まで広げ、その分を大きいものから引く）。</summary>
    int[] Widths(int total) {
        int n=regions.Count;if(n==0)return [];
        long span=Math.Max(1,regions[^1].End-regions[0].Start);
        int min=S(6);var w=new double[n];var small=new bool[n];
        for(int pass=0;pass<4;pass++) {
            double big=regions.Where((r,i)=>!small[i]).Sum(r=>(double)r.Size);
            double left=total-small.Count(s=>s)*min;
            bool changed=false;
            for(int i=0;i<n;i++) {
                if(small[i]){w[i]=min;continue;}
                w[i]=big>0?left*regions[i].Size/big:0;
                if(w[i]<min){small[i]=true;changed=true;}
            }
            if(!changed)break;
        }
        var result=new int[n];double acc=0;int prev=0;
        for(int i=0;i<n;i++){acc+=w[i];int edge=(int)Math.Round(acc);result[i]=edge-prev;prev=edge;}
        return result;
    }
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
                var color=RamLayout.Color(r.Kind);
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
            using(var path=Theme.Rounded(sw,S(2)))using(var brush=new SolidBrush(RamLayout.Color(k)))g.FillPath(brush,path);
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
