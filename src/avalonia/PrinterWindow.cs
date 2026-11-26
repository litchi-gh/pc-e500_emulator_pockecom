// SPDX-License-Identifier: GPL-2.0-or-later
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
namespace PokeCom;

/// <summary>紙を3倍に拡大して表示する。行単位でスクロールし、見えている行だけを描く。</summary>
internal sealed class PaperView : Panel
{
    internal const int Zoom=3;
    const int Gap=12;
    readonly PrinterPaper paper;
    readonly Func<byte[]> font;
    readonly ScrollBar scroll=new(){Orientation=Orientation.Vertical,HorizontalAlignment=HorizontalAlignment.Right,SmallChange=1,AllowAutoHide=false};
    readonly Image sheet=new(){Stretch=Stretch.Fill,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
    int last;
    long dropped;
    internal PaperView(PrinterPaper paper,Func<byte[]> font) {
        this.paper=paper;this.font=font;Background=Paint.Solid(70,74,80);ClipToBounds=true;
        RenderOptions.SetBitmapInterpolationMode(sheet,BitmapInterpolationMode.None);
        Children.AddRange([sheet,scroll]);
        scroll.ValueChanged+=(_,_)=>Draw();SizeChanged+=(_,_)=>PaperChanged();
    }
    int VisibleDots=>Math.Max(1,((int)Bounds.Height-Gap*2)/Zoom);
    /// <summary>紙か大きさが変わったときに、スクロールの範囲と位置を直す（最新の行を見ていたら追従する）。</summary>
    internal void PaperChanged() {
        bool follow=scroll.Value>=last;
        int cut=(int)Math.Min(int.MaxValue,paper.Dropped-dropped);dropped=paper.Dropped;
        last=Math.Max(0,(paper.Height-VisibleDots+PrinterPaper.LineHeight-1)/PrinterPaper.LineHeight);
        int large=Math.Max(1,VisibleDots/PrinterPaper.LineHeight);
        scroll.LargeChange=large;scroll.ViewportSize=large;scroll.Maximum=last;
        scroll.Value=follow?last:Math.Clamp((int)scroll.Value-cut,0,last);
        Draw();
    }
    void Draw() {
        int top=(int)scroll.Value*PrinterPaper.LineHeight,height=Math.Min(VisibleDots,paper.Height-top);
        if(height<=0){sheet.Source=null;return;}
        sheet.Source=Picture(paper,font(),top,height);
        sheet.Width=PrinterPaper.Width*Zoom;sheet.Height=height*Zoom;
        sheet.Margin=new Thickness(Math.Max(Gap,(Bounds.Width-18-sheet.Width)/2),Gap,0,0);
    }
    /// <summary>紙の一部を絵にする。</summary>
    internal static WriteableBitmap Picture(PrinterPaper paper,byte[] font,int top,int height) {
        var pixels=new int[PrinterPaper.Width*height];paper.Pixels(pixels,font,top,height);
        var bitmap=new WriteableBitmap(new PixelSize(PrinterPaper.Width,height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Opaque);
        using var frame=bitmap.Lock();
        for(int y=0;y<height;y++)Marshal.Copy(pixels,y*PrinterPaper.Width,frame.Address+y*frame.RowBytes,PrinterPaper.Width);
        return bitmap;
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e){base.OnPointerWheelChanged(e);scroll.Value=Math.Clamp(scroll.Value-Math.Round(e.Delta.Y*3),0,last);}
}

/// <summary>CE-126P のウィンドウ。紙の表示と、接続・紙を切る・コピー・保存・印刷の操作。</summary>
internal sealed class PrinterWindow : AppWindow
{
    readonly PrinterPaper paper;
    readonly PaperView view;
    readonly Func<byte[]> font;
    readonly TextBlock note=Ui.Note(""),result=new(){FontSize=12,VerticalAlignment=VerticalAlignment.Center,Foreground=Paint.Solid(Ui.SubText),TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(10,0)};
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    int shown=-1;
    byte[]? shownFont;
    static string NoFont=>L.T("このROMの文字フォントが見つからないため、紙に文字を描けません。文字のコピーとテキスト保存は使えます。","The character font of this ROM was not found, so the paper stays blank. Copying and saving the text still work.");
    static string NoTarget=>L.T("なし（このウィンドウだけ）","None (this window only)");
    internal PrinterWindow(PrinterPaper paper,Func<byte[]> font,bool connected,LinePrinter output,Action<PrintTarget?> choose,Action<bool> connect) : base("CE-126P",640,720) {
        this.paper=paper;this.font=font;view=new PaperView(paper,font);MinWidth=600;MinHeight=420;
        var attach=new CheckBox{Content=L.T("接続","Connected"),IsChecked=connected};
        attach.IsCheckedChanged+=(_,_)=>{bool on=attach.IsChecked==true;connect(on);result.Text=on?L.T("接続しました。","Connected."):L.T("外しました。LPRINT は印字されません。","Disconnected. LPRINT prints nothing.");};
        var tools=Ui.Row(Ui.Button(L.T("紙を切る","Clear"),()=>{paper.Clear();Render();}),
            Ui.Button(L.T("文字をコピー","Copy text"),async ()=>{
                if(paper.Count==0){result.Text=L.T("印字がありません。","Nothing printed.");return;}
                try{if(Clipboard is not null)await Clipboard.SetTextAsync(paper.Text());result.Text=L.T($"{paper.Count}行をコピーしました。",$"Copied {paper.Count} lines.");}
                catch(Exception ex){result.Text=L.T("コピーできません: ","Could not copy: ")+ex.Message;}
            }),
            Ui.Button(L.T("テキストで保存","Save text"),()=>Save("txt",Ui.Type(L.T("テキスト","Text"),"*.txt"),path=>File.WriteAllText(path,paper.Text(),new UTF8Encoding(false)))),
            Ui.Button(L.T("画像で保存","Save image"),()=>{
                if(font().Length==0){result.Text=NoFont;return;}
                Save("png",Ui.Type("PNG","*.png"),path=>{using var b=PaperView.Picture(paper,font(),0,paper.Height);b.Save(path,PngBitmapEncoderOptions.Default);});
            }));
        var targets=new ComboBox{Width=270};bool filling=false;
        void Fill(List<PrintTarget> found) {
            filling=true;
            var list=new List<object>{NoTarget};list.AddRange(found);
            if(output.Target is {} now && !found.Contains(now))list.Add(now);
            targets.ItemsSource=list;targets.SelectedItem=output.Target as object??NoTarget;filling=false;
        }
        async void Refresh()=>Fill(await Task.Run(LinePrinter.Find));
        targets.SelectionChanged+=(_,_)=>{if(!filling)choose(targets.SelectedItem as PrintTarget);};
        Fill([]);
        var send=Ui.Row(Ui.Label(L.T("印刷先","Print to")),targets,Ui.Button(L.T("更新","Refresh"),Refresh),Ui.Button(L.T("紙を印刷","Print paper"),()=>{
            if(paper.Count==0){result.Text=L.T("印字がありません。","Nothing printed.");return;}
            if(output.Target is null){result.Text=L.T("印刷先を選んでください。","Choose a printer first.");return;}
            output.Sheet(paper.Lines.ToArray(),font());result.Text=L.T($"{paper.Count}行を送りました。",$"Sent {paper.Count} lines.");
        }));
        var controls=Ui.CardOf(L.T("操作","Controls"),Ui.Stack(tools,send));controls.Margin=new Thickness(0,10,0,0);note.Margin=new Thickness(0,0,0,8);
        var body=new DockPanel();DockPanel.SetDock(note,Dock.Top);DockPanel.SetDock(controls,Dock.Bottom);
        body.Children.AddRange([note,controls,Ui.CardOf(L.T("紙","Paper"),view)]);
        var left=new DockPanel();DockPanel.SetDock(attach,Dock.Left);left.Children.AddRange([attach,result]);
        base.Fill(body,Ui.Footer(left,Ui.Button(L.T("閉じる","Close"),Close)));
        string? shownStatus=null;
        timer.Tick+=(_,_)=>{
            if(shown!=paper.Version || !ReferenceEquals(shownFont,font()))Render();
            if(output.Status!=shownStatus){if(shownStatus is not null)result.Text=output.Status;shownStatus=output.Status;}
        };
        Opened+=(_,_)=>{Render();timer.Start();Refresh();};Closed+=(_,_)=>timer.Stop();
    }
    async void Save(string extension,Avalonia.Platform.Storage.FilePickerFileType type,Action<string> write) {
        if(paper.Count==0){result.Text=L.T("印字がありません。","Nothing printed.");return;}
        if(await Ui.SaveFile(this,null,"CE-126P-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"."+extension,extension,type) is not string path)return;
        try{write(path);result.Text=L.T("保存しました: ","Saved: ")+path;}
        catch(Exception ex){result.Text=L.T("保存できません: ","Could not save: ")+ex.Message;}
    }
    void Render() {
        shownFont=font();note.Text=shownFont.Length==0?NoFont:L.T("LPRINT・LLIST の印字（24桁）。","Output of LPRINT and LLIST (24 columns).");
        shown=paper.Version;view.PaperChanged();
        Title=L.T($"CE-126P（{paper.Count}行）",$"CE-126P ({paper.Count} lines)");
    }
}

// The same rules as the Windows program's: a thread of its own writes, a USB
// thermal printer gets each line at once and is fed and let go of after three
// seconds of quiet, and an ordinary printer (CUPS) gets the lines gathered
// since its last job as one job at the same pause.
/// <summary>CE-126P の印字を、USB のサーマルプリンタ（/dev/usb/lp*）か CUPS のプリンタへ送る。</summary>
internal sealed class LinePrinter : IDisposable
{
    const int IdleMs=3000,JobLines=60,JobSeconds=30;
    readonly BlockingCollection<Action> queue=new();
    readonly Thread worker;
    volatile PrintTarget? target;
    internal PrintTarget? Target=>target;
    internal string Status {get;private set;}=L.T("送り先なし","No printer");
    PrintTarget? current;
    FileStream? device;
    bool unfed;
    readonly List<byte[]> job=[];
    byte[] jobFont=[];
    DateTime last,jobStarted;
    int sent;
    internal LinePrinter() {worker=new Thread(Run){IsBackground=true,Name="PokeCom printer"};worker.Start();}
    /// <summary>今つながっている USB プリンタと、CUPS のプリンタの一覧。</summary>
    internal static List<PrintTarget> Find() {
        var found=new List<PrintTarget>();
        if(!OperatingSystem.IsLinux())return found;
        try {
            if(Directory.Exists("/dev/usb"))foreach(string path in Directory.GetFiles("/dev/usb","lp*").Order()) {
                string name=Path.GetFileName(path);
                try{name=File.ReadAllText($"/sys/class/usbmisc/{name}/device/../product").Trim();}catch(Exception){}
                found.Add(new(PrintTarget.Usb,path,name));
            }
        } catch(Exception) {}
        try {
            using var p=Process.Start(new ProcessStartInfo("lpstat","-e"){RedirectStandardOutput=true,RedirectStandardError=true});
            if(p is not null) {
                string text=p.StandardOutput.ReadToEnd();
                if(p.WaitForExit(5000))foreach(string name in text.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))found.Add(new(PrintTarget.Windows,name,name));
            }
        } catch(Exception) {}
        return found;
    }
    bool Post(Action work){try{queue.Add(work);return true;}catch(InvalidOperationException){return false;}}
    /// <summary>送り先を変える（前の送り先にたまった分は先に出す）。</summary>
    internal void Select(PrintTarget? next){target=next;Post(()=>{Settle();current=next;Status=next is null?L.T("送り先なし","No printer"):L.T("待機中: ","Ready: ")+next.Label;});}
    /// <summary>印字された1行を送る（送り先がなければ何もしない）。</summary>
    internal void Line(byte[] line,byte[] font){if(target is not null)Post(()=>Send([line],font));}
    /// <summary>紙の全体を今の送り先へ送る。</summary>
    internal void Sheet(byte[][] lines,byte[] font){if(lines.Length>0)Post(()=>{Settle();Send(lines,font);Settle();});}
    /// <summary>たまった分をすぐに出し、終わるまで待つ（終了のため）。</summary>
    internal bool Flush(int timeout) {
        if(target is null)return true;
        using var done=new ManualResetEventSlim();
        return Post(()=>{Settle();done.Set();}) && done.Wait(timeout);
    }
    void Run() {
        try {
            while(!queue.IsCompleted) {
                if(queue.TryTake(out var work,200))work();
                else if((unfed || job.Count>0) && (DateTime.UtcNow-last).TotalMilliseconds>=IdleMs)Settle();
                if(job.Count>=JobLines || (job.Count>0 && (DateTime.UtcNow-jobStarted).TotalSeconds>=JobSeconds))Settle();
            }
        } catch(InvalidOperationException) {}
        Settle();
    }
    void Send(IReadOnlyList<byte[]> lines,byte[] font) {
        if(current is null)return;
        last=DateTime.UtcNow;
        if(current.Kind==PrintTarget.Windows){if(job.Count==0)jobStarted=last;job.AddRange(lines);jobFont=font;return;}
        try {
            if(device is null){device=new FileStream(current.Id,FileMode.Open,FileAccess.Write,FileShare.ReadWrite,1);device.Write(ThermalRaster.Reset);}
            foreach(var line in lines){device.Write(font.Length>0?ThermalRaster.Line(line,font):[..line.Select(b=>b is >=0x20 and <0x7f?b:(byte)' '),(byte)'\n']);device.Flush();sent++;}
            unfed=true;Status=L.T($"印字しました（{sent}行）: ",$"Printed ({sent} lines): ")+current.Label;
        } catch(Exception ex) {Fail(ex);unfed=false;Drop();}
    }
    void Drop(){try{device?.Dispose();}catch(Exception){}device=null;}
    void Settle() {
        try{if(device is not null && unfed){device.Write(ThermalRaster.Feed(ThermalRaster.FeedDots));device.Flush();}}
        catch(Exception ex){Fail(ex);}
        finally{unfed=false;Drop();}
        if(job.Count==0 || current is not {Kind:PrintTarget.Windows} printer)return;
        var lines=job.ToArray();job.Clear();
        string file=Path.Combine(Path.GetTempPath(),"pc-e500-"+Guid.NewGuid().ToString("N")+".pbm");
        try {
            // The thermal printer's picture of the lines, at its own 203 dpi: a character is 2 mm wide.
            using(var f=File.Create(file)) {
                f.Write(Encoding.ASCII.GetBytes($"P4\n{ThermalRaster.Dots} {lines.Length*ThermalRaster.LineDots}\n"));
                foreach(var line in lines)f.Write(ThermalRaster.Rows(line,jobFont));
            }
            var start=new ProcessStartInfo("lp"){RedirectStandardError=true,RedirectStandardOutput=true};
            foreach(string a in (string[])["-d",printer.Id,"-t","CE-126P","-o","ppi=203",file])start.ArgumentList.Add(a);
            using var p=Process.Start(start)!;
            string error=p.StandardError.ReadToEnd();p.WaitForExit(30000);
            if(p.ExitCode!=0)throw new IOException(error.Trim());
            sent+=lines.Length;Status=L.T($"印刷しました（{lines.Length}行）: ",$"Printed ({lines.Length} lines): ")+printer.Label;
        } catch(Exception ex) {Fail(ex);}
        finally {try{File.Delete(file);}catch(Exception){}}
    }
    void Fail(Exception ex)=>Status=L.T("印刷できません: ","Could not print: ")+ex.Message;
    public void Dispose(){queue.CompleteAdding();worker.Join(5000);}
}
