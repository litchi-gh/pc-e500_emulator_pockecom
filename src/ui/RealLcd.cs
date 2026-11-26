// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing;

namespace PokeCom;

// The real display, worked out per screen pixel: each dot fills 82% of its
// cell across and 86% down, and a pixel takes the share of every dot it
// covers, so the gaps come out even at any size instead of rounding to a
// whole pixel here and none there. Built only when the dots, the colours or
// the size change.
/// <summary>
/// リアル表示：ドットはセルの横 82%・縦 86% を占め、画面の1ピクセルは覆うドットの割合で色を混ぜる
/// （どの大きさでも隙間が均一になる）。ドット・色・大きさが変わったときだけ作り直す。
/// </summary>
internal sealed class RealLcd
{
    const float DotFillX=0.82f,DotFillY=0.86f;
    int[] realArgb=[];
    /// <summary>作った絵（乗算済みの ARGB、幅×高さ）。</summary>
    internal int[] Argb=>realArgb;
    /// <summary>リアル表示にできる大きさか（1ドットに1ピクセル以上）。</summary>
    internal static bool Fits(float width,float height)=>width>=240 && height>=32;
    readonly byte[] realPixels=new byte[240*32];
    (int Width,int Height,int Lit,int Unlit,bool Gaps) realFor;
    /// <summary>描いた絵で滑っていたドット（その行を描き直すため）と、滑っているドットか。</summary>
    readonly List<(int Index,float X,float Y)> realSlides=[];
    readonly bool[] realSliding=new bool[240*32];
    /// <summary>画面のピクセルごとの、滑っているドットが覆う割合と、計算し直す画面の行。</summary>
    float[] realMotion=[];
    bool[] realRedo=[];
    (int Cell0,float Weight0,int Cell1,float Weight1)[] realColumns=[],realRows=[];
    /// <summary>画面のピクセルごとに、覆うドット（最大2つ）とその割合を求める。</summary>
    /// <param name="size">ピクセル数</param>
    /// <param name="cells">ドット数</param>
    /// <param name="fill">ドットがセルを占める割合</param>
    static (int,float,int,float)[] Spans(int size,int cells,float fill) {
        static double Overlap(double a,double b,double c,double d)=>Math.Max(0,Math.Min(b,d)-Math.Max(a,c));
        var spans=new (int,float,int,float)[size];double step=(double)cells/size;
        for(int i=0;i<size;i++) {
            double a=i*step,b=a+step;int k=Math.Min(cells-1,(int)a);
            double w0=Overlap(a,b,k,k+fill),w1=k+1<cells?Overlap(a,b,k+1,k+1+fill):0;
            spans[i]=(k,(float)(w0/step),Math.Min(k+1,cells-1),(float)(w1/step));
        }
        return spans;
    }
    /// <summary>絵を今のドットに合わせる。</summary>
    /// <param name="lit">点灯したドットの色</param>
    /// <param name="unlit">消えたドットの色（透明度つき）</param>
    /// <param name="showDotLines">ドットの間をあけるか</param>
    /// <param name="shade">ドットの濃さ</param>
    /// <param name="slides">滑っているドット</param>
    /// <returns>絵が変わったか</returns>
    internal bool Update(int width,int height,Color lit,Color unlit,bool showDotLines,byte[] shade,List<(int Index,float X,float Y)> slides) {
        var want=(width,height,lit.ToArgb(),unlit.ToArgb(),showDotLines);
        float fillX=showDotLines?DotFillX:1,fillY=showDotLines?DotFillY:1;
        // Only the screen rows over dot rows that changed are worked out again: a
        // line of text changes eight of the 32, and the whole glass at 4K was most
        // of a frame.
        // 変わったドットの行にかかる画面の行だけを計算し直す（4K では、全体の計算に1コマの大半がかかっていた）。
        bool whole=realFor!=want;
        Span<bool> changed=stackalloc bool[32];bool any=whole;
        if(!whole)for(int k=0;k<32;k++)if(!shade.AsSpan(k*240,240).SequenceEqual(realPixels.AsSpan(k*240,240))){changed[k]=true;any=true;}
        // The dot rows a sliding dot reaches, now or when last drawn, are worked out again too.
        foreach(var list in (List<(int Index,float X,float Y)>[])[slides,realSlides])
            foreach(var s in list){int row=s.Index/240;for(int k=Math.Max(0,row-1);k<=Math.Min(31,row+1);k++)changed[k]=true;any=true;}
        if(any) {
            if(realFor.Width!=width || realFor.Height!=height || realFor.Gaps!=showDotLines) {
                if(realFor.Width!=width || realFor.Height!=height) {
                    realArgb=new int[width*height];realMotion=new float[width*height];realRedo=new bool[height];
                }
                realColumns=Spans(width,240,fillX);realRows=Spans(height,32,fillY);
            }
            realFor=want;shade.CopyTo(realPixels,0);
            Array.Clear(realSliding);foreach(var s in slides)realSliding[s.Index]=true;
            realSlides.Clear();realSlides.AddRange(slides);
            // Premultiplied, so the shares of an opaque dot and a faint one simply add.
            float ua=unlit.A/255f;
            float[] on=[255,lit.R,lit.G,lit.B],off=[unlit.A,unlit.R*ua,unlit.G*ua,unlit.B*ua];
            for(int y=0;y<height;y++) {
                var (r0,v0,r1,v1)=realRows[y];
                realRedo[y]=whole || (v0>0 && changed[r0]) || (v1>0 && changed[r1]);
                if(realRedo[y])Array.Clear(realMotion,y*width,width);
            }
            // A sliding dot is a whole cell's square laid on the screen where it is
            // on its way; the gaps between the cells stay where they are, and it
            // shows through the dot areas it passes, as a picture behind a mesh.
            // 滑っているドットは、セル1つ分の四角を途中の位置に置く。ドットの間の線は動かさず、通り過ぎる
            // ドットの部分にだけ見える（網の向こうの絵のように）。
            float stepX=240f/width,stepY=32f/height;
            foreach(var (index,dx,dy) in slides) {
                float left=index%240+dx,top=index/240+dy;
                int x0=Math.Max(0,(int)(left/stepX)),x1=Math.Min(width-1,(int)MathF.Ceiling((left+1)/stepX)-1);
                int y0=Math.Max(0,(int)(top/stepY)),y1=Math.Min(height-1,(int)MathF.Ceiling((top+1)/stepY)-1);
                for(int y=y0;y<=y1;y++) {
                    if(!realRedo[y])continue;
                    float cy=(Math.Min((y+1)*stepY,top+1)-Math.Max(y*stepY,top))/stepY;
                    if(cy<=0)continue;
                    for(int x=x0;x<=x1;x++) {
                        float cx=(Math.Min((x+1)*stepX,left+1)-Math.Max(x*stepX,left))/stepX;
                        if(cx>0)realMotion[y*width+x]+=cx*cy;
                    }
                }
            }
            for(int y=0;y<height;y++) {
                if(!realRedo[y])continue;
                var (r0,v0,r1,v1)=realRows[y];int row0=r0*240,row1=r1*240,line=y*width;
                for(int x=0;x<width;x++) {
                    var (c0,u0,c1,u1)=realColumns[x];
                    float a=0,red=0,green=0,blue=0,moving=Math.Min(1,realMotion[line+x]);
                    // A dot on its way between off and on mixes the two (the afterimage).
                    // A sliding dot is not in its cell but where it is on its way.
                    void Add(int index,float weight) {
                        if(weight<=0)return;
                        float s=Math.Max(realSliding[index]?0:realPixels[index]/255f,moving);
                        a+=(off[0]+(on[0]-off[0])*s)*weight;red+=(off[1]+(on[1]-off[1])*s)*weight;
                        green+=(off[2]+(on[2]-off[2])*s)*weight;blue+=(off[3]+(on[3]-off[3])*s)*weight;
                    }
                    Add(row0+c0,u0*v0);Add(row0+c1,u1*v0);Add(row1+c0,u0*v1);Add(row1+c1,u1*v1);
                    realArgb[line+x]=Channel(a)<<24|Channel(red)<<16|Channel(green)<<8|Channel(blue);
                }
            }
        }
        return any;
    }
    /// <summary>0〜255 に丸めた色の成分。</summary>
    static int Channel(float v)=>Math.Min(255,(int)(v+0.5f));
}
