// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing;

namespace PokeCom;

/// <summary>LCD のガラスの色と表示シンボル（描き方は各プログラムが持つ）。</summary>
internal static class LcdGlass
{
    /// <summary>コントラストの既定値（0〜100。今までの表示と同じ濃さ）。</summary>
    internal const int DefaultContrast=50;
    // Glass is the bare LCD green, for legends cut out of a segment.
    internal static readonly Color Ink=Color.FromArgb(53,75,48),Ghost=Color.FromArgb(143,169,105),Glass=Color.FromArgb(181,202,125);
    /// <summary>表示シンボルの上下の段の中心（ドットの行、LCD の上端から上向きが負）と、大文字の高さ（ドット）。</summary>
    internal const float UpperRow=-7.6f,LowerRow=-3.2f,CapDots=2.35f;
    /// <summary>表示シンボルの一覧：名前、左右の端（ドットの列）、段、点灯ビットの値とマスク、前のシンボルに続くか。</summary>
    internal static (string Name,float From,float To,float Row,int Value,int Mask,bool Joined)[] Legends(int s0,int s4,int s5)=>[
        ("BUSY",4.2f,17.4f,LowerRow,s0,1,false),("DBL",24.3f,33.9f,LowerRow,s0,8,false),
        ("RUN",39.3f,49.2f,LowerRow,s0,2,false),("PRO",54.3f,64.5f,LowerRow,s0,4,false),
        ("DE",114.2f,120.6f,UpperRow,s5,16,false),("G",121.2f,124.8f,UpperRow,s5,8,true),("RAD",125.7f,135.3f,UpperRow,s5,4,true),
        ("HYP",140.7f,150.6f,UpperRow,s4,4,false),("2ndF",119.4f,131.4f,LowerRow,s4,16,false),("CAPS",136.5f,150.3f,LowerRow,s4,8,false),
        ("PRINT",186f,201.3f,UpperRow,s5,1,false),
        ("カナ",181.2f,188.7f,LowerRow,s4,2,false),("小文字",192.3f,205.8f,LowerRow,s4,1,false),("E",208.2f,211.8f,LowerRow,s5,2,false)
    ];
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
    internal static (Color Lit,Color Unlit,Color Off) Colors(int contrast,bool showDotLines) {
        static Color Mix(Color a,Color b,float t)=>Color.FromArgb((int)MathF.Round(a.R+(b.R-a.R)*t),(int)MathF.Round(a.G+(b.G-a.G)*t),(int)MathF.Round(a.B+(b.B-a.B)*t));
        float c=contrast/(float)DefaultContrast;
        Color lit=c<=1?Mix(Bare,Ink,0.25f+0.75f*c):Mix(Ink,Darkest,c-1);
        float over=Math.Clamp((contrast-60)/40f,0,1);over*=over;
        // The real display shows the dots that are off faintly always, as the glass does.
        float unlit=(showDotLines?0.07f:0)*(1-over)+over;
        Color off=contrast<=DefaultContrast?Mix(Bare,Ghost,Math.Clamp((contrast-10)/40f,0,1)):Mix(Ghost,lit,over);
        return (lit,Color.FromArgb((int)MathF.Round(unlit*255),lit),off);
    }
}
