// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

internal sealed partial class PrinterPaper
{
    /// <summary>紙の一部を、ARGB の画素（幅 Width）に描く（1ドット1ピクセル、白地に黒）。</summary>
    /// <param name="pixels">描き先（Width×height 以上）</param>
    /// <param name="font">ROM の文字フォント（20h から、1文字6バイト）</param>
    /// <param name="top">描き始める位置（ドット）</param>
    /// <param name="height">描く高さ（ドット）</param>
    internal void Pixels(int[] pixels,byte[] font,int top,int height) {
        Array.Fill(pixels,unchecked((int)0xffffffff),0,Width*height);
        int ink=unchecked((int)0xff282830);
        int first=Math.Max(0,(top-Margin)/LineHeight),last=Math.Min(lines.Count-1,(top+height-Margin)/LineHeight);
        for(int row=first;row<=last;row++) {
            var line=lines[row];
            for(int col=0;col<Math.Min(Columns,line.Length);col++) {
                int at=(line[col]-0x20)*CellWidth;
                if(at<0 || at+CellWidth>font.Length)continue;
                for(int x=0;x<CellWidth;x++) for(int y=0;y<8;y++) {
                    int py=Margin+row*LineHeight+y-top;
                    if(py>=0 && py<height && (font[at+x]>>y&1)!=0)pixels[py*Width+Margin+col*CellWidth+x]=ink;
                }
            }
        }
    }
}
