// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing;
namespace PokeCom;

/// <summary>コントローラーの絵の部分と、その位置。</summary>
internal static class PadLayout
{
    /// <summary>絵を描く座標の広さ（これを枠に合わせて拡大・縮小する）。</summary>
    internal const float W=560,H=270;
    internal enum Shape {Round,Pill,Arm,Stick,Trigger,Hub}
    /// <summary>絵の部分1つ：形、場所、受け持つ操作（最初のものを押したときに知らせる）、文字、右側か、位置の名前（位置合わせ用）。</summary>
    internal sealed record Part(Shape Shape,RectangleF Area,string[] Controls,string Label="",bool Right=false,string Pos="");
    /// <summary>番号のボタンを置く位置（位置合わせで尋ねる順）。</summary>
    internal static readonly string[] Positions=["Top","Right","Bottom","Left","LB","RB","LT","RT","Back","Start","LS","RS","Home"];
    /// <summary>位置の表示名（位置合わせの案内に使う）。</summary>
    internal static string PositionName(string pos)=>pos switch{
        "Top"=>L.T("右の4つの上","the top of the four"),"Right"=>L.T("右の4つの右","the right of the four"),"Bottom"=>L.T("右の4つの下","the bottom of the four"),"Left"=>L.T("右の4つの左","the left of the four"),
        "LB"=>L.T("左の肩（手前）","left shoulder"),"RB"=>L.T("右の肩（手前）","right shoulder"),"LT"=>L.T("左のトリガー（奥）","left trigger"),"RT"=>L.T("右のトリガー（奥）","right trigger"),
        "Back"=>L.T("中央の左","middle left"),"Start"=>L.T("中央の右","middle right"),"LS"=>L.T("左スティックの押し込み","left stick press"),"RS"=>L.T("右スティックの押し込み","right stick press"),
        "Home"=>L.T("中央の下","middle bottom"),_=>pos};
    static RectangleF Circle(float x,float y,float r)=>new(x-r,y-r,r*2,r*2);
    /// <summary>コントローラーの絵の部分を作る。</summary>
    /// <param name="device">コントローラー（null なら Xbox 系の並び）</param>
    /// <param name="layout">DirectInput のボタンの位置</param>
    /// <param name="learning">位置合わせ中か（すべての位置を描く）</param>
    internal static List<Part> Parts(PadDevice? device,Dictionary<string,int> layout,bool learning) {
        var parts=new List<Part>();
        bool x=device is null || device.Kind==PadKind.XInput;
        int buttons=device?.Buttons??0;
        string B(int b)=>$"Button{b}";
        void Dpad(float cx,float cy) {
            parts.Add(new(Shape.Arm,new(cx-11,cy-34,22,24),["Up"]));parts.Add(new(Shape.Arm,new(cx-11,cy+10,22,24),["Down"]));
            parts.Add(new(Shape.Arm,new(cx-34,cy-11,24,22),["Left"]));parts.Add(new(Shape.Arm,new(cx+10,cy-11,24,22),["Right"]));
            parts.Add(new(Shape.Hub,new(cx-11,cy-11,22,22),[]));
        }
        // Where each place is; triggers and shoulders first, as the body is drawn over their lower edge.
        var places=new (string Pos,Shape Shape,RectangleF Area,bool Right)[]{
            ("LT",Shape.Trigger,new(125,0,80,20),false),("RT",Shape.Trigger,new(355,0,80,20),true),
            ("LB",Shape.Pill,new(112,21,106,19),false),("RB",Shape.Pill,new(342,21,106,19),true),
            ("Top",Shape.Round,Circle(395,72,13),false),("Bottom",Shape.Round,Circle(395,124,13),false),
            ("Left",Shape.Round,Circle(369,98,13),false),("Right",Shape.Round,Circle(421,98,13),false),
            ("Back",Shape.Pill,new(232,88,40,16),false),("Start",Shape.Pill,new(288,88,40,16),false),("Home",Shape.Round,Circle(280,124,10),false)};
        if(x) {
            var names=new Dictionary<string,string>{["LT"]="LT",["RT"]="RT",["LB"]="LB",["RB"]="RB",["Top"]="Y",["Bottom"]="A",["Left"]="X",["Right"]="B",["Back"]="Back",["Start"]="Start"};
            foreach(var (pos,shape,area,right) in places.Where(p=>names.ContainsKey(p.Pos)))parts.Add(new(shape,area,[names[pos]],names[pos],right,pos));
            parts.Add(new(Shape.Stick,Circle(165,98,27),["LStickUp","LStickDown","LStickLeft","LStickRight","LS"],"L"));
            Dpad(225,160);
            parts.Add(new(Shape.Stick,Circle(335,160,27),["RStickUp","RStickDown","RStickLeft","RStickRight","RS"],"R",true));
            return parts;
        }
        // A number is drawn only where the pad has that button; while the places are
        // being learned, every place is drawn, a "?" where none is known yet.
        int At(string pos)=>layout.TryGetValue(pos,out int n) && n>=1 && n<=buttons?n:0;
        foreach(var (pos,shape,area,right) in places) {
            int n=At(pos);
            if(n>0)parts.Add(new(shape,area,[B(n)],n.ToString(),right,pos));
            else if(learning)parts.Add(new(shape,area,[],"?",right,pos));
        }
        if(device!.Pov)Dpad(165,98);
        var a=device.Axes??[];
        int ls=At("LS"),rs=At("RS");
        string Press(int n,string none)=>n>0?n.ToString():learning?"?":none;
        if(a.Length>1 && a[0] is not null && a[1] is not null)
            parts.Add(new(Shape.Stick,Circle(225,160,27),["LStickUp","LStickDown","LStickLeft","LStickRight",..(ls>0?[B(ls)]:Array.Empty<string>())],Press(ls,"L"),false,"LS"));
        // Z and R are the right stick on most pads; one of them alone still moves the cap one way.
        bool z=a.Length>2 && a[2] is not null,r=a.Length>3 && a[3] is not null;
        if(z||r) {
            string[] tilt=[..(r?new[]{"AxisR-","AxisR+"}:[]),..(z?new[]{"AxisZ-","AxisZ+"}:[])];
            // Four tilt slots so the press is always fifth: an absent axis's slot is a name never pressed.
            while(tilt.Length<4)tilt=[..tilt,""];
            parts.Add(new(Shape.Stick,Circle(335,160,27),[..tilt,..(rs>0?[B(rs)]:Array.Empty<string>())],Press(rs,"R"),true,"RS"));
        }
        // Buttons with no place go in a row under the pad.
        var placed=Positions.Select(At).Where(n=>n>0).ToHashSet();
        var extra=Enumerable.Range(1,buttons).Where(b=>!placed.Contains(b)).ToArray();
        for(int i=0;i<extra.Length;i++)parts.Add(new(Shape.Round,Circle(W/2+(i-(extra.Length-1)/2f)*26,254,11),[B(extra[i])],extra[i].ToString()));
        return parts;
    }
}
