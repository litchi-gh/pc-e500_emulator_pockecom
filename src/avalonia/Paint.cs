// SPDX-License-Identifier: GPL-2.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using DrawingColor=System.Drawing.Color;
using RectangleF=System.Drawing.RectangleF;
namespace PokeCom;

/// <summary>本体の絵を描くための小さな道具（色・角丸・グラデーション・文字）。</summary>
internal static class Paint
{
    internal static Color Of(DrawingColor c)=>Color.FromArgb(c.A,c.R,c.G,c.B);
    internal static Rect Of(RectangleF r)=>new(r.X,r.Y,r.Width,r.Height);
    static readonly Dictionary<Color,IImmutableSolidColorBrush> brushes=[];
    internal static IBrush Solid(Color c) {
        if(!brushes.TryGetValue(c,out var brush))brushes[c]=brush=new Avalonia.Media.Immutable.ImmutableSolidColorBrush(c);
        return brush;
    }
    internal static IBrush Solid(byte r,byte g,byte b)=>Solid(Color.FromRgb(r,g,b));
    internal static IPen Pen(Color c,double width)=>new Pen(Solid(c),width);
    internal static IPen Pen(byte r,byte g,byte b,double width)=>Pen(Color.FromRgb(r,g,b),width);
    /// <summary>範囲 r の上から下（または左から右）への2色のグラデーション。範囲の外は折り返す。</summary>
    internal static IBrush Gradient(Rect r,Color from,Color to,bool across=false)=>new LinearGradientBrush {
        StartPoint=new RelativePoint(r.TopLeft,RelativeUnit.Absolute),
        EndPoint=new RelativePoint(across?r.TopRight:r.BottomLeft,RelativeUnit.Absolute),
        SpreadMethod=GradientSpreadMethod.Reflect,
        GradientStops={new GradientStop(from,0),new GradientStop(to,1)}
    };
    internal static void Rounded(DrawingContext g,IBrush? fill,IPen? pen,Rect r,double radius)=>g.DrawRectangle(fill,pen,new RoundedRect(r,radius));
    // The faces Windows has, then the ones Ubuntu has: Liberation Sans has Arial's
    // own widths, and Noto Sans CJK is the Japanese face of the desktop.
    static readonly FontFamily Latin=new("Arial, Liberation Sans, Noto Sans, DejaVu Sans, Ubuntu Sans"),
        Kana=new("Yu Gothic UI, Noto Sans CJK JP, Noto Sans JP, IPAexGothic, IPAGothic, Takao Pゴシック, Droid Sans Fallback");
    /// <summary>ウィンドウの文字の書体（日本語を持つもの）。</summary>
    internal static readonly FontFamily Ui=new("Yu Gothic UI, Noto Sans CJK JP, Noto Sans JP, Ubuntu Sans, Ubuntu, Noto Sans, DejaVu Sans");
    static Typeface Face(string text,bool bold)=>new(text.Any(c=>c>255)?Kana:Latin,FontStyle.Normal,bold?FontWeight.Bold:FontWeight.Normal);
    static FormattedText Form(string text,double size,bool bold,IBrush? brush)=>new(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Face(text,bold),size,brush);
    static readonly Dictionary<(string,bool,double),double> widths=[];
    /// <summary>文字を描く。width を指定すると、その幅の中央に置き、入らなければ縮める。</summary>
    internal static void TextAt(DrawingContext g,string text,double x,double y,double size,Color color,bool bold=false,double width=0) {
        if(!widths.TryGetValue((text,bold,size),out double measured))widths[(text,bold,size)]=measured=Form(text,size,bold,null).Width;
        double actual=width>0 && measured>width-3?size*(width-3)/measured:size;
        var form=Form(text,Math.Max(4,actual),bold,Solid(color));
        // Centred in a box a line and a half high, as the Windows program does.
        g.DrawText(form,width>0?new Point(x+(width-form.Width)/2,y+(size*1.5-form.Height)/2):new Point(x,y));
    }
    static readonly Dictionary<(string,bool),(Geometry Path,Rect Ink,Rect Cap)?> outlines=[];
    static readonly Dictionary<(string,bool,double,double,double,double),Geometry?> placed=[];
    /// <summary>文字の外形と、インクの範囲と、その書体の大文字 H の範囲（外形を作れなければ null）。</summary>
    static (Geometry Path,Rect Ink,Rect Cap)? Outline(string text,bool bold) {
        if(outlines.TryGetValue((text,bold),out var known))return known;
        var path=Form(text,100,bold,Brushes.Black).BuildGeometry(default);
        var capital=new FormattedText("H",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Face(text,bold),100,Brushes.Black).BuildGeometry(default);
        (Geometry,Rect,Rect)? made=path is null || capital is null?null:(path,path.Bounds,capital.Bounds);
        return outlines[(text,bold)]=made;
    }
    /// <summary>text のインクの幅が、measured の何倍か（別の型番を同じ字幅で描くため）。</summary>
    internal static double InkRatio(string text,string measured,bool bold)=>Outline(text,bold) is {} a && Outline(measured,bold) is {} b && b.Ink.Width>0?a.Ink.Width/b.Ink.Width:1;
    /// <summary>実機で測った範囲に、文字の外形がぴったり収まるように描く（横に伸ばして合わせる）。</summary>
    internal static void Lettering(DrawingContext g,string text,double left,double right,double top,double bottom,Color color,bool bold=true) {
        var key=(text,bold,left,right,top,bottom);
        if(!placed.TryGetValue(key,out var path)) {
            if(Outline(text,bold) is {} o && o.Ink.Width>0 && o.Ink.Height>0 && o.Cap.Height>0) {
                path=o.Path.Clone();
                path.Transform=new MatrixTransform(Matrix.CreateTranslation(-o.Ink.Left,-o.Cap.Top)*
                    Matrix.CreateScale((right-left)/o.Ink.Width,(bottom-top)/o.Cap.Height)*Matrix.CreateTranslation(left,top));
            }
            placed[key]=path;
        }
        if(path is null) {
            double size=(bottom-top)/0.72;
            TextAt(g,text,left,(top+bottom)/2-size*0.75,size,color,bold,right-left);
            return;
        }
        g.DrawGeometry(Solid(color),null,path);
    }
}
