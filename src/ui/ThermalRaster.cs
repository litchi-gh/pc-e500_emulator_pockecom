// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// The CE-126P's line on a 58 mm thermal printer, as the ESP32-S3 build prints
// it: 24 columns across 384 dots is 16 dots a character, so the 5x7 ROM glyph
// at three times its size (15 x 21) with a dot between fills the paper, 2 mm a
// character as on the real one; a line is 32 dots, 4 mm. The gap between lines
// goes as blank raster too: the MPT-II ignores ESC J, and lines came out touching.
// サーマルプリンタの1行：24桁 × 16 ドット = 384 ドット。ROM の字を縦横3倍にし、1行 32 ドット（4mm）。
// 行の間も白い絵として送る（MPT-II は紙送りの ESC J を受け付けず、行がくっついたため）。
/// <summary>CE-126P の行を、横 384 ドットのサーマルプリンタの絵（ESC/POS の GS v 0）にする。</summary>
internal static partial class ThermalRaster
{
    internal const int Dots=384,Bytes=Dots/8,LineDots=32,Scale=3,Cell=16,FeedDots=96;
    /// <summary>1行を、LineDots 行 × Bytes バイトの絵にする（ビット7が左、1が黒）。</summary>
    /// <param name="line">PC-E500 の文字コード（24桁を超えた分は使わない）</param>
    /// <param name="font">ROM の文字フォント（20h から、1文字6バイト、1バイトが縦1列でビット0が上）</param>
    internal static byte[] Rows(byte[] line,byte[] font) {
        var rows=new byte[LineDots*Bytes];
        for(int col=0;col<Math.Min(line.Length,Dots/Cell);col++) {
            int at=(line[col]-0x20)*6;
            if(at<0 || at+6>font.Length)continue;
            // Five columns of eight rows; the sixth column is the gap between characters.
            for(int gx=0;gx<5;gx++) for(int gy=0;gy<8;gy++) {
                if((font[at+gx]>>gy&1)==0)continue;
                for(int sx=0;sx<Scale;sx++) for(int sy=0;sy<Scale;sy++) {
                    int x=col*Cell+gx*Scale+sx,y=gy*Scale+sy;
                    rows[y*Bytes+x/8]|=(byte)(0x80>>(x&7));
                }
            }
        }
        return rows;
    }
    /// <summary>絵の行を GS v 0 の命令にする。</summary>
    /// <param name="rows">絵（height 行 × Bytes バイト）</param>
    /// <param name="height">行の数</param>
    internal static byte[] Command(byte[] rows,int height) {
        var command=new byte[8+rows.Length];
        byte[] head=[0x1d,(byte)'v',(byte)'0',0,Bytes,0,(byte)(height&255),(byte)(height>>8)];
        head.CopyTo(command,0);rows.CopyTo(command,8);
        return command;
    }
    /// <summary>1行を、プリンタへ送るバイト列にする。</summary>
    internal static byte[] Line(byte[] line,byte[] font)=>Command(Rows(line,font),LineDots);
    // The last line stays under the head; blank raster carries it out to the cutter.
    /// <summary>白い絵で紙を送る命令（dots ドット）。</summary>
    internal static byte[] Feed(int dots)=>Command(new byte[dots*Bytes],dots);
    /// <summary>プリンタを初めの状態にする命令（ESC @）。</summary>
    internal static readonly byte[] Reset=[0x1b,(byte)'@'];
}
