// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing;
namespace PokeCom;

internal static partial class RamLayout
{
    /// <summary>区画の種類の色。</summary>
    internal static Color Color(RegionKind kind,bool dark)=>kind switch{
        RegionKind.Program=>dark?Rgb(76,160,255):Rgb(0,103,192),
        RegionKind.Variables=>dark?Rgb(56,196,186):Rgb(0,153,143),
        RegionKind.Block=>dark?Rgb(150,150,200):Rgb(110,110,160),
        RegionKind.RamDisk=>dark?Rgb(120,200,110):Rgb(70,150,60),
        RegionKind.Free=>dark?Rgb(58,58,58):Rgb(232,232,232),
        RegionKind.Stack=>dark?Rgb(190,150,240):Rgb(135,100,184),
        RegionKind.Work=>dark?Rgb(160,130,210):Rgb(160,130,200),
        RegionKind.MachineCode=>dark?Rgb(255,150,60):Rgb(230,110,10),
        RegionKind.System=>dark?Rgb(110,110,110):Rgb(120,120,120),
        _=>dark?Rgb(90,90,90):Rgb(180,180,180)};
    static Color Rgb(int r,int g,int b)=>System.Drawing.Color.FromArgb(r,g,b);
    // Widths: each region gets its share of the bar, but none less than a few
    // pixels; what the small ones are given comes out of the large ones.
    /// <summary>区画ごとの幅（大きさの割合、細いものは下限まで広げ、その分を大きいものから引く）。</summary>
    internal static int[] Widths(IReadOnlyList<Region> regions,int total,int min) {
        int n=regions.Count;if(n==0)return [];
        long span=Math.Max(1,regions[^1].End-regions[0].Start);
        var w=new double[n];var small=new bool[n];
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
}
