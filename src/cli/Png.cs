// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.IO.Compression;
namespace PokeCom;

// Pictures and sound written by hand: two colours and one channel need no
// graphics or audio library, so they are the same on every system.
/// <summary>LCD と CE-126P の紙を PNG に、音を WAV にする（画像・音声の部品を使わない）。</summary>
internal static class Png
{
    /// <summary>240×32 のドットを、1ドット scale ピクセルの PNG にする（点灯は濃い色、消灯は液晶の緑）。</summary>
    internal static byte[] Lcd(byte[] pixels,int scale=4)=>Encode(240*scale,32*scale,(x,y)=>pixels[y/scale*240+x/scale]!=0,[181,202,125,53,75,48]);
    /// <summary>紙の全体（白地に黒）。</summary>
    internal static byte[] Paper(PrinterPaper paper,byte[] font,int scale=2) {
        int width=PrinterPaper.Width,height=paper.Height;
        var ink=new bool[width*height];
        for(int row=0;row<paper.Count;row++) {
            var line=paper.Lines[row];
            for(int col=0;col<Math.Min(PrinterPaper.Columns,line.Length);col++) {
                int at=(line[col]-0x20)*PrinterPaper.CellWidth;
                if(at<0 || at+PrinterPaper.CellWidth>font.Length)continue;
                for(int x=0;x<PrinterPaper.CellWidth;x++)for(int y=0;y<8;y++)
                    if((font[at+x]>>y&1)!=0)ink[(PrinterPaper.Margin+row*PrinterPaper.LineHeight+y)*width+PrinterPaper.Margin+col*PrinterPaper.CellWidth+x]=true;
            }
        }
        return Encode(width*scale,height*scale,(x,y)=>ink[y/scale*width+x/scale],[255,255,255,40,40,48]);
    }
    /// <summary>2色の絵を PNG にする。</summary>
    /// <param name="palette">地の色と、on の所の色（RGB を3バイトずつ）</param>
    static byte[] Encode(int width,int height,Func<int,int,bool> on,byte[] palette) {
        var raw=new byte[height*(width+1)];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)raw[y*(width+1)+1+x]=on(x,y)?(byte)1:(byte)0;
        using var packed=new MemoryStream();
        using(var z=new ZLibStream(packed,CompressionLevel.Optimal,true))z.Write(raw);
        using var file=new MemoryStream();
        file.Write([0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a]);
        var header=new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);
        header[8]=8;header[9]=3;
        Chunk(file,"IHDR",header);Chunk(file,"PLTE",palette);Chunk(file,"IDAT",packed.ToArray());Chunk(file,"IEND",[]);
        return file.ToArray();
    }
    static void Chunk(Stream to,string type,byte[] data) {
        Span<byte> word=stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word,data.Length);to.Write(word);
        var body=new byte[4+data.Length];
        for(int i=0;i<4;i++)body[i]=(byte)type[i];
        data.CopyTo(body,4);to.Write(body);
        uint crc=0xffffffff;
        foreach(byte b in body){crc^=b;for(int k=0;k<8;k++)crc=(crc&1)!=0?crc>>1^0xedb88320:crc>>1;}
        BinaryPrimitives.WriteUInt32BigEndian(word,~crc);to.Write(word);
    }
    /// <summary>48kHz・16ビット・モノラルの WAV。</summary>
    internal static byte[] Wave(IReadOnlyCollection<short> samples) {
        using var file=new MemoryStream();using var w=new BinaryWriter(file);
        int bytes=samples.Count*2;
        w.Write("RIFF"u8);w.Write(36+bytes);w.Write("WAVEfmt "u8);w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(48000);w.Write(96000);w.Write((short)2);w.Write((short)16);
        w.Write("data"u8);w.Write(bytes);foreach(short s in samples)w.Write(s);
        w.Flush();return file.ToArray();
    }
}
