// SPDX-License-Identifier: GPL-2.0-or-later
using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace PokeCom;

// Where the CE-126P's lines go besides the paper in its window: a USB thermal
// printer written to directly in ESC/POS (the MPT-II needs no driver and has
// no print queue), or any Windows printer through its queue.
/// <summary>CE-126P の印字の送り先：USB のサーマルプリンタ（ESC/POS で直接）か、Windows のプリンタ。</summary>
/// <param name="Kind">種類（Usb か Windows）</param>
/// <param name="Id">USB なら機器のパス、Windows ならプリンタの名前</param>
/// <param name="Name">表示する名前</param>
internal sealed record PrintTarget(string Kind,string Id,string Name)
{
    internal const string Usb="usb",Windows="windows";
    /// <summary>一覧に出す名前。</summary>
    internal string Label=>Kind==Usb?L.T($"USB サーマルプリンタ: {Name}",$"USB thermal printer: {Name}"):L.T($"プリンタ: {Name}",$"Printer: {Name}");
    public override string ToString()=>Label;
    // The USB printer class's device interface (GUID_DEVINTERFACE_USBPRINT).
    static readonly Guid UsbPrint=new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
    [StructLayout(LayoutKind.Sequential)] struct InterfaceData {public int Size;public Guid Class;public int Flags;public IntPtr Reserved;}
    [StructLayout(LayoutKind.Sequential)] struct DeviceData {public int Size;public Guid Class;public uint Instance;public IntPtr Reserved;}
    [DllImport("setupapi.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevsW(ref Guid guid,IntPtr enumerator,IntPtr parent,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr device,ref Guid guid,uint index,ref InterfaceData data);
    [DllImport("setupapi.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set,ref InterfaceData data,IntPtr detail,int size,out int required,ref DeviceData device);
    [DllImport("setupapi.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set,ref DeviceData device,uint property,out uint type,byte[] buffer,int size,out int required);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    /// <summary>今つながっている USB プリンタと、Windows のプリンタの一覧。</summary>
    internal static List<PrintTarget> Find() {
        var found=new List<PrintTarget>();
        try {found.AddRange(UsbPrinters());} catch(Exception) {}
        try {foreach(string name in PrinterSettings.InstalledPrinters)found.Add(new(Windows,name,name));} catch(Exception) {}
        return found;
    }
    /// <summary>USB プリンタの機器（パスと名前）。</summary>
    static IEnumerable<PrintTarget> UsbPrinters() {
        var guid=UsbPrint;
        IntPtr set=SetupDiGetClassDevsW(ref guid,IntPtr.Zero,IntPtr.Zero,0x12);   // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
        if(set==new IntPtr(-1))yield break;
        try {
            for(uint i=0;;i++) {
                var data=new InterfaceData{Size=Marshal.SizeOf<InterfaceData>()};
                if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref data))break;
                var device=new DeviceData{Size=Marshal.SizeOf<DeviceData>()};
                SetupDiGetDeviceInterfaceDetailW(set,ref data,IntPtr.Zero,0,out int size,ref device);
                if(size<=0)continue;
                IntPtr detail=Marshal.AllocHGlobal(size);
                string path;
                try {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W: its cbSize is 8 on x64, the path follows at 4.
                    Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                    if(!SetupDiGetDeviceInterfaceDetailW(set,ref data,detail,size,out _,ref device))continue;
                    path=Marshal.PtrToStringUni(detail+4)??"";
                } finally {Marshal.FreeHGlobal(detail);}
                string name=Property(set,ref device,0x0C) ?? Property(set,ref device,0x00) ?? "USB printer";   // SPDRP_FRIENDLYNAME, SPDRP_DEVICEDESC
                if(path.Length>0)yield return new(Usb,path,name);
            }
        } finally {SetupDiDestroyDeviceInfoList(set);}
    }
    static string? Property(IntPtr set,ref DeviceData device,uint property) {
        var buffer=new byte[512];
        if(!SetupDiGetDeviceRegistryPropertyW(set,ref device,property,out _,buffer,buffer.Length,out int length) || length<2)return null;
        string text=System.Text.Encoding.Unicode.GetString(buffer,0,length).TrimEnd('\0');
        return text.Length>0?text:null;
    }
    // A USB printer's path changes with the port it is plugged into: one that is
    // not there under its path is looked for under its name.
    /// <summary>保存してある送り先を、今の一覧から探す（USB はパスが変わっていれば同じ名前の機器）。</summary>
    internal static PrintTarget? Resolve(PrintTarget? saved,IReadOnlyList<PrintTarget> found) {
        if(saved is null)return null;
        return found.FirstOrDefault(t=>t.Kind==saved.Kind && t.Id==saved.Id)
            ?? (saved.Kind==Usb?found.FirstOrDefault(t=>t.Kind==Usb && t.Name==saved.Name):null)
            ?? saved;
    }
}

// The CE-126P's line on a 58 mm thermal printer, as the ESP32-S3 build prints
// it: 24 columns across 384 dots is 16 dots a character, so the 5x7 ROM glyph
// at three times its size (15 x 21) with a dot between fills the paper, 2 mm a
// character as on the real one; a line is 32 dots, 4 mm. The gap between lines
// goes as blank raster too: the MPT-II ignores ESC J, and lines came out touching.
// サーマルプリンタの1行：24桁 × 16 ドット = 384 ドット。ROM の字を縦横3倍にし、1行 32 ドット（4mm）。
// 行の間も白い絵として送る（MPT-II は紙送りの ESC J を受け付けず、行がくっついたため）。
/// <summary>CE-126P の行を、横 384 ドットのサーマルプリンタの絵（ESC/POS の GS v 0）にする。</summary>
internal static class ThermalRaster
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
    // What the printer will be sent, as a picture: checked before paper is spent.
    /// <summary>行を、プリンタに出る絵と同じ画像にする（送る前に確かめるため）。</summary>
    internal static Bitmap Preview(IReadOnlyList<byte[]> lines,byte[] font)=>Picture([..lines.SelectMany(line=>Rows(line,font))],Math.Max(1,lines.Count)*LineDots);
    /// <summary>絵の行（height 行 × Bytes バイト、1 が黒）を、白黒のビットマップにする。</summary>
    internal static Bitmap Picture(byte[] rows,int height) {
        var bitmap=new Bitmap(Dots,Math.Max(1,height),PixelFormat.Format1bppIndexed);
        var palette=bitmap.Palette;palette.Entries[0]=Color.White;palette.Entries[1]=Color.Black;bitmap.Palette=palette;
        var bits=bitmap.LockBits(new Rectangle(0,0,Dots,bitmap.Height),ImageLockMode.WriteOnly,PixelFormat.Format1bppIndexed);
        try {for(int y=0;y<height && (y+1)*Bytes<=rows.Length;y++)Marshal.Copy(rows,y*Bytes,bits.Scan0+y*bits.Stride,Bytes);}
        finally {bitmap.UnlockBits(bits);}
        return bitmap;
    }
}

// The same lines on an ordinary printer, at the size the CE-126P prints them:
// a character 2 mm wide, a line 4 mm, a glyph dot 0.375 mm (three thermal dots
// at 203 dpi), down the left of the page and on to the next one. Each line is
// drawn as the thermal printer's picture of it: a rectangle for every dot made
// the whole roll hundreds of thousands of shapes for the spooler.
// 1行はサーマルプリンタと同じ絵にして描く（ドットごとの四角では、印刷のデータが巨大になった）。
/// <summary>CE-126P の行を、ふつうのプリンタに実寸（1字 2mm、1行 4mm）で印刷する。</summary>
internal static class PagePrint
{
    const float CellMm=2f,LineMm=4f,DotMm=CellMm*ThermalRaster.Scale/ThermalRaster.Cell;
    /// <summary>印刷の文書を作る（行が尽きるまでページを続ける）。</summary>
    /// <param name="printer">プリンタの名前</param>
    /// <param name="lines">PC-E500 の文字コードの行</param>
    /// <param name="font">ROM の文字フォント（空なら、ふつうの文字で印刷する）</param>
    internal static PrintDocument Document(string printer,IReadOnlyList<byte[]> lines,byte[] font) {
        var document=new PrintDocument{DocumentName="CE-126P",PrintController=new StandardPrintController()};
        document.PrinterSettings.PrinterName=printer;
        int next=0;
        document.BeginPrint+=(_,_)=>next=0;
        document.PrintPage+=(_,e)=>{
            var g=e.Graphics!;g.PageUnit=GraphicsUnit.Millimeter;
            // MarginBounds is in hundredths of an inch, from the page's corner; the
            // graphics start at the printable area, which the hard margins offset.
            float left=e.MarginBounds.Left*0.254f-e.PageSettings.HardMarginX*0.254f;
            float top=e.MarginBounds.Top*0.254f-e.PageSettings.HardMarginY*0.254f;
            int perPage=Math.Max(1,(int)(e.MarginBounds.Height*0.254f/LineMm));
            using var ink=new SolidBrush(Color.Black);
            using var text=new Font("Consolas",CellMm*1.6f,GraphicsUnit.Millimeter);
            g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.Half;
            for(int row=0;row<perPage && next<lines.Count;row++,next++) {
                var line=lines[next];float y=top+row*LineMm;
                if(font.Length==0) {g.DrawString(new string([..line.Select(PrinterPaper.ToChar)]),text,ink,left,y);continue;}
                using var picture=ThermalRaster.Picture(ThermalRaster.Rows(line,font),ThermalRaster.LineDots);
                g.DrawImage(picture,new RectangleF(left,y,ThermalRaster.Dots*DotMm/ThermalRaster.Scale,LineMm));
            }
            e.HasMorePages=next<lines.Count;
        };
        return document;
    }
}

// Writing to a printer can take its time - a USB write waits for the head, a
// print job for the spooler, and a PDF printer asks for a file name - so it is
// done on a thread of its own, never on the one that runs the machine. A USB
// printer is fed after printing has paused for three seconds, to bring the last
// line out to the cutter, and let go of until the next line. An ordinary
// printer gets the lines gathered since its last job as one job at the same
// pause, so a program's output is one printout, not a sheet per line. Lines
// printed while the target cannot be reached are dropped, as on the device.
// 印字の送り出し：専用のスレッドで送る（エミュレータを止めない）。USB は3秒止まったら紙を送って機器を放す。
// ふつうのプリンタは、3秒止まったら、それまでの行をまとめて1つの印刷にする。送れない間の行は捨てる。
/// <summary>CE-126P の印字を、選んだプリンタへ送る。</summary>
internal sealed class PrintOutput : IDisposable
{
    internal const int IdleMs=3000;
    abstract record Work;
    sealed record LineWork(byte[] Line,byte[] Font) : Work;
    sealed record SheetWork(byte[][] Lines,byte[] Font) : Work;
    sealed record TargetWork(PrintTarget? Target) : Work;
    sealed record FlushWork(ManualResetEventSlim Done) : Work;
    readonly BlockingCollection<Work> queue=new();
    readonly Thread worker;
    volatile PrintTarget? target;
    /// <summary>今の送り先（null なら送らない）。</summary>
    internal PrintTarget? Target=>target;
    // Replaceable for the tests, which must not spend the user's paper.
    /// <summary>USB の機器を開く処理（テストでは差し替える）。</summary>
    internal Func<string,Stream> OpenUsb=OpenDevice;
    /// <summary>USB プリンタの機器を書き込み用に開く（テスト用：開けるかを確かめる）。</summary>
    internal static Stream TestOpen(string path)=>OpenDevice(path);
    /// <summary>ふつうのプリンタで印刷する処理（テストでは差し替える）。</summary>
    internal Action<PrintDocument> PrintPages=document=>document.Print();
    /// <summary>送り出しの状態（エラーなら理由）。</summary>
    internal string Status {get;private set;}=L.T("送り先なし","No printer");
    /// <summary>送り先へ送った行の数。</summary>
    internal int Sent {get;private set;}
    /// <summary>最後の送り出しが失敗したか。</summary>
    internal bool Failed {get;private set;}
    // Worker's own state.
    Stream? device;
    bool unfed;
    readonly List<byte[]> job=[];
    byte[] jobFont=[];
    DateTime last,jobStarted;
    // A program that prints more often than the pause never paused: nothing came
    // out until it ended. A page's worth, or half a minute, prints all the same.
    // 3秒より短い間隔で印字し続けても、1ページ分（60行）か30秒で印刷する。
    const int JobLines=60,JobSeconds=30;
    // A USB write that the printer does not take (out of paper, cover open) gives up
    // after this, instead of holding the thread - and every later line - for good.
    /// <summary>USB の書き込みを待つ時間の上限（ミリ秒）。</summary>
    internal int WriteTimeout=10000;
    /// <summary>送り出しのスレッドを始める。</summary>
    internal PrintOutput() {
        // STA: a printer that asks where to save (a PDF printer) shows a dialog.
        worker=new Thread(Run){IsBackground=true,Name="PokeCom printer"};
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }
    /// <summary>送り先を変える（前の送り先にたまった分は先に出す）。</summary>
    internal void Select(PrintTarget? next){target=next;Add(new TargetWork(next));}
    /// <summary>印字された1行を送る（送り先がなければ何もしない）。</summary>
    internal void Line(byte[] line,byte[] font){if(target is not null)Add(new LineWork(line,font));}
    /// <summary>紙の全体を今の送り先へ送る。</summary>
    internal void Sheet(IReadOnlyList<byte[]> lines,byte[] font){if(lines.Count>0)Add(new SheetWork([..lines],font));}
    /// <summary>たまった分をすぐに出し、終わるまで待つ（テストと終了のため）。</summary>
    internal bool Flush(int timeout) {
        using var done=new ManualResetEventSlim();
        if(!Add(new FlushWork(done)))return false;
        return done.Wait(timeout);
    }
    bool Add(Work work) {try {queue.Add(work);return true;} catch(InvalidOperationException) {return false;}}
    void Run() {
        try {
            while(!queue.IsCompleted) {
                if(queue.TryTake(out var work,200))Do(work);
                else if((unfed || job.Count>0) && (DateTime.UtcNow-last).TotalMilliseconds>=IdleMs)Settle();
                if(job.Count>=JobLines || (job.Count>0 && (DateTime.UtcNow-jobStarted).TotalSeconds>=JobSeconds))Settle();
            }
        } catch(InvalidOperationException) {}
        Settle();
    }
    void Do(Work work) {
        switch(work) {
        case TargetWork t: Settle();current=t.Target;Status=current is null?L.T("送り先なし","No printer"):L.T("待機中: ","Ready: ")+current.Label;Failed=false;break;
        case LineWork l: Send([l.Line],l.Font);break;
        // Lines waiting for a job go out first: the sheet holds them too, and added
        // to the same job they printed twice.
        case SheetWork s: Settle();Send(s.Lines,s.Font);Settle();break;
        case FlushWork f: Settle();f.Done.Set();break;
        }
    }
    PrintTarget? current;
    /// <summary>行を送り先へ（USB はすぐ印字、ふつうのプリンタは印刷にためる）。</summary>
    void Send(IReadOnlyList<byte[]> lines,byte[] font) {
        if(current is null)return;
        last=DateTime.UtcNow;
        if(current.Kind==PrintTarget.Windows) {if(job.Count==0)jobStarted=DateTime.UtcNow;job.AddRange(lines);jobFont=font;return;}
        try {
            if(device is null) {device=Open(current);Write(ThermalRaster.Reset);}
            foreach(var line in lines) {
                // Without the ROM's font, the printer's own characters (ASCII only).
                Write(font.Length>0?ThermalRaster.Line(line,font):[..line.Select(b=>b is >=0x20 and <0x7f?b:(byte)' '),(byte)'\n']);
                Sent++;
            }
            unfed=true;Failed=false;Status=L.T($"印字しました（{Sent}行）: ",$"Printed ({Sent} lines): ")+current.Label;
        } catch(Exception ex) {
            // A device that failed is let go of: kept, every later line went to the dead handle.
            Fail(ex);unfed=false;device?.Dispose();device=null;
        }
    }
    // A printer moved to another USB port is there under another path: looked
    // for by its name once, and used from then on.
    /// <summary>USB の送り先を開く（開けなければ、同じ名前の機器を探して開き直す）。</summary>
    Stream Open(PrintTarget at) {
        try {return OpenUsb(at.Id);}
        catch(Exception) {
            var moved=PrintTarget.Resolve(at,PrintTarget.Find());
            if(moved is null || moved==at || moved.Kind!=PrintTarget.Usb)throw;
            var stream=OpenUsb(moved.Id);current=moved;target=moved;Moved?.Invoke(moved);return stream;
        }
    }
    /// <summary>USB の送り先が、別の差し込み口（別のパス）で見つかったとき（設定に残すため。送り出しのスレッドから呼ぶ）。</summary>
    internal event Action<PrintTarget>? Moved;
    void Write(byte[] bytes) {
        using var cancel=new CancellationTokenSource(WriteTimeout);
        try {device!.WriteAsync(bytes,0,bytes.Length,cancel.Token).GetAwaiter().GetResult();device.Flush();}
        catch(OperationCanceledException) {throw new IOException(L.T("プリンタが受け付けません（紙切れ、カバーが開いている、など）。","The printer is not taking data (out of paper, cover open...)."));}
    }
    /// <summary>止まったときの後始末：USB は紙を送って機器を放し、ふつうのプリンタはためた行を印刷する。</summary>
    void Settle() {
        try {
            if(device is not null && unfed)Write(ThermalRaster.Feed(ThermalRaster.FeedDots));
        } catch(Exception ex) {Fail(ex);}
        finally {unfed=false;device?.Dispose();device=null;}
        if(job.Count==0 || current is not {Kind:PrintTarget.Windows} printer)return;
        var lines=job.ToArray();job.Clear();
        try {
            using var document=PagePrint.Document(printer.Id,lines,jobFont);
            PrintPages(document);
            Sent+=lines.Length;Failed=false;Status=L.T($"印刷しました（{lines.Length}行）: ",$"Printed ({lines.Length} lines): ")+printer.Label;
        } catch(Exception ex) {Fail(ex);}
    }
    void Fail(Exception ex){Failed=true;Status=L.T("印刷できません: ","Could not print: ")+ex.Message;}
    // A raw write to the printer class's interface, the way the device works
    // with it: no driver, no queue. Shared, so a print queue on it is not blocked.
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern SafeFileHandle CreateFileW(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    /// <summary>USB プリンタの機器を書き込み用に開く。</summary>
    static Stream OpenDevice(string path) {
        var handle=CreateFileW(path,0x40000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);   // GENERIC_WRITE, FILE_SHARE_READ|WRITE, OPEN_EXISTING, FILE_FLAG_OVERLAPPED
        if(handle.IsInvalid) {int error=Marshal.GetLastWin32Error();handle.Dispose();throw new IOException(L.T($"プリンタを開けません（つながっていないか、ほかで使用中。エラー {error}）",$"Could not open the printer (not connected, or in use; error {error})"));}
        // Overlapped, so a write that hangs can be given up (WriteTimeout).
        return new FileStream(handle,FileAccess.Write,1,true);
    }
    /// <summary>送り出しを止める（たまった分は出してから）。</summary>
    public void Dispose() {
        queue.CompleteAdding();
        worker.Join(5000);
    }
}
