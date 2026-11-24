// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.IO.Compression;
namespace PokeCom;

// A PNG of the LCD, written by hand: the one picture the command line makes
// needs no graphics library, so it is the same on every system.
/// <summary>LCD のドットを PNG にする（画像の部品を使わない）。</summary>
internal static class Png
{
    /// <summary>240×32 のドットを、1ドット scale ピクセルの PNG にする（点灯は濃い色、消灯は液晶の緑）。</summary>
    internal static byte[] Lcd(byte[] pixels,int scale=4) {
        int width=240*scale,height=32*scale;
        // Two colours, one byte a pixel: the glass and the ink.
        var raw=new byte[height*(width+1)];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)raw[y*(width+1)+1+x]=pixels[y/scale*240+x/scale]!=0?(byte)1:(byte)0;
        using var packed=new MemoryStream();
        using(var z=new ZLibStream(packed,CompressionLevel.Optimal,true))z.Write(raw);
        using var file=new MemoryStream();
        file.Write([0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a]);
        var header=new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);
        header[8]=8;header[9]=3;
        Chunk(file,"IHDR",header);
        Chunk(file,"PLTE",[181,202,125,53,75,48]);
        Chunk(file,"IDAT",packed.ToArray());
        Chunk(file,"IEND",[]);
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
}
