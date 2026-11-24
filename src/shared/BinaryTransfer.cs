// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// Reads a binary file straight into RAM at an address, or writes a range of
// memory out to one. A PC-E500 SAVE M file starts with a 16-byte header that
// names its own load address, so such a file can go back where it came from.
/// <summary>メモリとバイナリファイルのやり取り（デバッガの BIN 読込・保存）。</summary>
internal static class BinaryTransfer
{
    /// <summary>SAVEM ヘッダーの長さ。</summary>
    internal const int HeaderLength=16;
    // FF 00 06 01 10 <length:3> <start:3> ... as CE-140F SAVE M writes it.
    /// <summary>SAVEM ヘッダー（CE-140F の SAVE M が付ける）を読む。</summary>
    /// <param name="file">ファイルの中身</param>
    /// <param name="start">書き込み先の先頭アドレス</param>
    /// <param name="length">データの長さ</param>
    /// <returns>ヘッダーがあったか</returns>
    internal static bool TryReadHeader(byte[] file,out int start,out int length) {
        start=length=0;
        if(file.Length<HeaderLength || file[0]!=0xff || file[1]!=0 || file[2]!=6 || file[3]!=1 || file[4]!=0x10)return false;
        length=file[5]|file[6]<<8|file[7]<<16;start=file[8]|file[9]<<8|file[10]<<16;
        return length>0 && length==file.Length-HeaderLength && start<=0xfffff;
    }
    // Writes the file to the machine; returns where it went and how many bytes.
    /// <summary>ファイルをメモリに書く（RAM の範囲だけ）。</summary>
    /// <param name="core">書き込み先のコア</param>
    /// <param name="file">ファイルの中身</param>
    /// <param name="address">書き込み先（useHeader なら無視してヘッダーの値）</param>
    /// <param name="useHeader">SAVEM ヘッダーのアドレスを使うか</param>
    /// <returns>書き込んだ先頭アドレスとバイト数</returns>
    internal static (int Address,int Length) Load(Core core,byte[] file,int address,bool useHeader) {
        byte[] payload=file;
        if(useHeader) {
            if(!TryReadHeader(file,out int start,out _))throw new InvalidDataException(L.T("SAVEMヘッダーがありません。","The file has no SAVEM header."));
            address=start;payload=file.AsSpan(HeaderLength).ToArray();
        }
        if(payload.Length==0)throw new InvalidDataException(L.T("ファイルが空です。","The file is empty."));
        if(address<0||address>0xfffff||payload.Length>0x100000-address)throw new ArgumentOutOfRangeException(nameof(address),L.T("アドレスが範囲外です。","Address out of range."));
        core.Write(address,payload);
        return (address,payload.Length);
    }
    /// <summary>メモリを読み、ファイルの中身にする。</summary>
    /// <param name="core">読み出し元のコア</param>
    /// <param name="address">先頭アドレス</param>
    /// <param name="length">バイト数</param>
    /// <param name="withHeader">SAVEM ヘッダーを付けるか</param>
    internal static byte[] Save(Core core,int address,int length,bool withHeader) {
        if(length<=0||address<0||address>0xfffff||length>0x100000-address)throw new ArgumentOutOfRangeException(nameof(length),L.T("アドレスまたは長さが範囲外です。","Address or length out of range."));
        byte[] data=core.Dump(address,length);
        if(!withHeader)return data;
        byte[] file=new byte[HeaderLength+length];
        file[0]=0xff;file[1]=0;file[2]=6;file[3]=1;file[4]=0x10;
        file[5]=(byte)length;file[6]=(byte)(length>>8);file[7]=(byte)(length>>16);
        file[8]=(byte)address;file[9]=(byte)(address>>8);file[10]=(byte)(address>>16);
        file[11]=file[12]=file[13]=0xff;file[14]=0;file[15]=0x0f;
        data.CopyTo(file,HeaderLength);
        return file;
    }
}
