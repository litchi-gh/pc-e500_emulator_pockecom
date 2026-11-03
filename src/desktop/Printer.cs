// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
namespace PokeCom;

// The paper roll of the emulated CE-126P: printed lines in the PC-E500
// character set, kept while the program runs whether or not the window is open.
/// <summary>
/// CE-126P の紙（印字された行の巻紙）。ウィンドウを閉じても、ROM を切り替えても残る。
/// 行は PC-E500 の文字コードで持ち、2000行を超えたら古い行から捨てる。
/// </summary>
internal sealed class PrinterPaper
{
    // The line width is the native printer's, so the two cannot drift apart.
    /// <summary>1行の桁数（ネイティブの Ce126p::Columns）。</summary>
    internal static readonly int Columns=Native.e500_printer_columns();
    /// <summary>持っておく最大の行数。</summary>
    internal const int MaxLines=2000;
    // One character cell: the ROM font is 6 x 8, lines are fed 10 dots apart.
    /// <summary>1文字の幅、行の送り、紙の余白（ドット）。</summary>
    internal const int CellWidth=6,LineHeight=10,Margin=6;
    readonly Roll lines=new(MaxLines);
    // A read from the core can end mid-line; the rest comes with the next one.
    readonly List<byte> pending=[];
    /// <summary>紙が変わるたびに増える番号（ウィンドウが描き直すかの判断に使う）。</summary>
    internal int Version {get;private set;}
    // Lines cut from the top since the start, so a view can keep its place.
    /// <summary>上限を超えて先頭から捨てた行数の合計。</summary>
    internal long Dropped {get;private set;}
    /// <summary>今ある行数と、行の一覧（古い順）。</summary>
    internal int Count=>lines.Count;
    internal IReadOnlyList<byte[]> Lines=>lines;
    /// <summary>コアから取り出した印字を紙に加える（改行で1行、途中の行は次の分とつなぐ）。</summary>
    /// <param name="data">PC-E500 の文字コードの並び</param>
    internal void Feed(ReadOnlySpan<byte> data)
    {
        foreach(byte b in data) {
            if(b!=(byte)'\n'){pending.Add(b);continue;}
            if(lines.Add([..pending]))Dropped++;
            pending.Clear();Version++;
        }
    }
    /// <summary>紙を切る（すべての行を消す）。</summary>
    internal void Clear(){lines.Clear();pending.Clear();Version++;}
    // The newest lines up to a fixed count, the oldest overwritten: a full roll
    // takes a line without moving the others along.
    /// <summary>決まった行数までの行を持つリングバッファ。いっぱいなら一番古い行を上書きする。</summary>
    sealed class Roll(int capacity) : IReadOnlyList<byte[]>
    {
        readonly byte[][] slots=new byte[capacity][];
        int start;
        public int Count {get;private set;}
        public byte[] this[int index]=>(uint)index<(uint)Count?slots[(start+index)%slots.Length]:throw new ArgumentOutOfRangeException(nameof(index));
        // True when the oldest line made room.
        public bool Add(byte[] line) {
            if(Count<slots.Length){slots[(start+Count++)%slots.Length]=line;return false;}
            slots[start]=line;start=(start+1)%slots.Length;return true;
        }
        public void Clear(){Array.Clear(slots);start=Count=0;}
        public IEnumerator<byte[]> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    // The paper is text as printed: 5Ch and 7Ch are the ROM's ¥ and ¦, the way
    // the keycaps show them. The screen text keeps \ and | for them instead, to
    // be pasted back into a program (either types the same byte).
    /// <summary>PC-E500 の文字コードを Unicode の文字にする（5Ch・7Ch は印字のとおり ¥・¦。対応がなければ □）。</summary>
    internal static char ToChar(byte b)=>b switch {0x5c=>'¥',0x7c=>'¦',_=>LcdTextDecoder.Unicode(b) ?? '□'};
    /// <summary>紙の全体をテキスト（行は CRLF 区切り）にする。</summary>
    internal string Text()
    {
        var text=new StringBuilder();
        foreach(var line in lines){foreach(byte b in line)text.Append(ToChar(b));text.Append("\r\n");}
        return text.ToString();
    }
    // The whole sheet in dots, margins included.
    /// <summary>紙の全体の高さ（ドット、余白を含む）。</summary>
    internal int Height=>Margin*2+Math.Max(1,lines.Count)*LineHeight;
    /// <summary>紙の幅（ドット、余白を含む）。</summary>
    internal static readonly int Width=Margin*2+Columns*CellWidth;
    // Dots top to top+height of the sheet, one dot per pixel, black on white, drawn
    // with the ROM font from 20h; codes the font does not cover print blank. The
    // view asks only for what is on screen, so a long roll costs no more than a short one.
    /// <summary>紙の一部を新しいビットマップに描く（1ドット1ピクセル、白地に黒）。</summary>
    /// <param name="font">ROM の文字フォント（20h から、1文字6バイト）</param>
    /// <param name="top">描き始める位置（ドット）</param>
    /// <param name="height">描く高さ（ドット、負なら最後まで）</param>
    internal Bitmap Render(byte[] font,int top=0,int height=-1)
    {
        if(height<0)height=Height-top;
        height=Math.Max(1,height);
        var bitmap=new Bitmap(Width,height,PixelFormat.Format32bppArgb);
        Render(bitmap,new int[Width*height],font,top,height);
        return bitmap;
    }
    // The same into the top rows of a bitmap and buffer the caller keeps, at least
    // Width by height, so a view repainting on every scroll allocates nothing.
    /// <summary>紙の一部を、呼び出し元が持つビットマップと画素バッファの上の行に描く。</summary>
    /// <param name="bitmap">描き先（幅 Width、高さ height 以上）</param>
    /// <param name="pixels">作業用の画素バッファ（Width×height 以上）</param>
    /// <param name="font">ROM の文字フォント</param>
    /// <param name="top">描き始める位置（ドット）</param>
    /// <param name="height">描く高さ（ドット）</param>
    internal void Render(Bitmap bitmap,int[] pixels,byte[] font,int top,int height)
    {
        var data=bitmap.LockBits(new Rectangle(0,0,Width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        Array.Fill(pixels,Color.White.ToArgb(),0,Width*height);
        int ink=Color.FromArgb(40,40,48).ToArgb();
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
        System.Runtime.InteropServices.Marshal.Copy(pixels,0,data.Scan0,Width*height);
        bitmap.UnlockBits(data);
    }
}

// The paper at 3x, scrolled a line at a time. Only the rows in view are drawn,
// so neither the work per new line nor any control's size grows with the roll
// (a window taller than 32767 pixels breaks Win32 scrolling).
/// <summary>紙を3倍に拡大して表示するビュー。行単位でスクロールし、見えている行だけを描く。</summary>
internal sealed class PaperView : Control
{
    internal const int Zoom=3;
    const int Gap=12;
    readonly PrinterPaper paper;
    readonly Func<byte[]> font;
    readonly VScrollBar scroll=new(){Dock=DockStyle.Right,SmallChange=1};
    /// <summary>ビューを作る。</summary>
    /// <param name="paper">表示する紙</param>
    /// <param name="font">ROM の文字フォントを返す関数（ROM の切り替えに追従するため）</param>
    internal PaperView(PrinterPaper paper,Func<byte[]> font)
    {
        this.paper=paper;this.font=font;
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        BackColor=Color.FromArgb(70,74,80);Controls.Add(scroll);
        scroll.ValueChanged+=(_,_)=>Invalidate();
    }
    int last,wheel;
    long dropped;
    // The first line in view, and whether the newest line was in view when the
    // roll was last taken in; a line is LineHeight dots.
    /// <summary>見えている先頭の行と、最新の行まで見えているか。</summary>
    internal int FirstLine=>scroll.Value;
    internal bool AtBottom=>scroll.Value>=last;
    // Dots tall of the last paint, for the test that a long roll is not drawn whole.
    /// <summary>直前の描画で描いた高さ（ドット、テスト用）。</summary>
    internal int RenderedDots {get;private set;}
    int VisibleDots=>Math.Max(1,(ClientSize.Height-Gap*2)/Zoom);
    // Takes in a changed roll or size. The view follows the newest line only if
    // it was already there, so reading back up is not interrupted by more printing;
    // lines cut from the top of a full roll move it up with the text it shows.
    /// <summary>紙か大きさが変わったときに、スクロールの範囲と位置を直す（最新の行を見ていたら追従する）。</summary>
    internal void PaperChanged()
    {
        bool follow=AtBottom;
        int cut=(int)Math.Min(int.MaxValue,paper.Dropped-dropped);dropped=paper.Dropped;
        last=Math.Max(0,(paper.Height-VisibleDots+PrinterPaper.LineHeight-1)/PrinterPaper.LineHeight);
        int large=Math.Max(1,VisibleDots/PrinterPaper.LineHeight);
        scroll.LargeChange=large;scroll.Maximum=last+large-1;
        scroll.Value=follow?last:Math.Clamp(scroll.Value-cut,0,last);
        Invalidate();
    }
    /// <summary>指定の行が先頭に来るようにスクロールする。</summary>
    internal void ScrollTo(int line)=>scroll.Value=Math.Clamp(line,0,last);
    // The base constructor can resize before this one has set the paper.
    protected override void OnResize(EventArgs e){base.OnResize(e);if(paper is not null)PaperChanged();}
    // Three lines a notch (120), with the remainder of finer touchpad steps kept
    // for the next one the same way; turning back drops it, so a notch is
    // always three lines.
    /// <summary>ホイールでスクロールする（40 で1行、端数は同じ向きの次の回に持ち越し、向きが変われば捨てる）。</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if(wheel!=0 && Math.Sign(wheel)!=Math.Sign(e.Delta))wheel=0;
        wheel+=e.Delta;int lines=wheel/40;wheel-=lines*40;
        if(lines!=0)ScrollTo(scroll.Value-lines);
    }
    internal void TestWheel(int delta)=>OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,0,0,delta));
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();}
    /// <summary>見えている行だけを描き、3倍に拡大して表示する。</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(BackColor);
        int top=scroll.Value*PrinterPaper.LineHeight,height=Math.Min(VisibleDots,paper.Height-top);
        RenderedDots=Math.Max(0,height);
        if(height<=0)return;
        if(sheet is null || sheet.Height<height) {
            sheet?.Dispose();
            sheet=new Bitmap(PrinterPaper.Width,VisibleDots,PixelFormat.Format32bppArgb);sheetPixels=new int[PrinterPaper.Width*VisibleDots];
        }
        paper.Render(sheet,sheetPixels,font(),top,height);
        int x=Math.Max(Gap,(ClientSize.Width-scroll.Width-PrinterPaper.Width*Zoom)/2);
        g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
        g.DrawImage(sheet,new Rectangle(x,Gap,PrinterPaper.Width*Zoom,height*Zoom),new Rectangle(0,0,PrinterPaper.Width,height),GraphicsUnit.Pixel);
    }
    // The rows in view, kept from paint to paint and grown only with the window.
    Bitmap? sheet;
    int[] sheetPixels=[];
    protected override void Dispose(bool disposing){if(disposing)sheet?.Dispose();base.Dispose(disposing);}
}

// Modeless view of the paper, following the running machine like the CE-140F trace.
/// <summary>CE-126P のウィンドウ。紙の表示と、接続・紙を切る・コピー・保存の操作。</summary>
internal sealed class PrinterDialog : Form
{
    readonly PrinterPaper paper;
    readonly PaperView view;
    readonly Func<byte[]> font;
    readonly Label note=new(){Dock=DockStyle.Top,Height=30,Padding=new Padding(8,6,0,0)};
    readonly Label result=new(){Dock=DockStyle.Bottom,Height=26,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(6,0,0,0)};
    readonly System.Windows.Forms.Timer timer=new(){Interval=250};
    int shown=-1;
    // The font the window was last drawn with: a ROM switched to or from one
    // whose font is not found changes the note and the paper with nothing printed.
    /// <summary>直前に描いたときのフォント（ROM を切り替えたら、印字がなくても描き直す）。</summary>
    byte[]? shownFont;
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="paper">表示する紙</param>
    /// <param name="font">ROM の文字フォントを返す関数</param>
    /// <param name="connected">今の接続の状態を返す関数</param>
    /// <param name="connect">「接続」のチェックが変わったときに呼ぶ処理</param>
    internal PrinterDialog(PrinterPaper paper,Func<byte[]> font,Func<bool> connected,Action<bool> connect)
    {
        this.paper=paper;this.font=font;view=new PaperView(paper,font){Dock=DockStyle.Fill};
        Text="CE-126P";ClientSize=new Size(560,640);MinimumSize=new Size(520,320);
        StartPosition=FormStartPosition.CenterScreen;Font=new Font("Yu Gothic UI",10);
        var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=46,WrapContents=false,Padding=new Padding(6,4,0,0)};
        var attach=new CheckBox{Text=L.T("接続","Connected"),Checked=connected(),AutoSize=true,Margin=new Padding(4,10,8,0)};
        var clear=new Button{Text=L.T("紙を切る","Clear"),AutoSize=true};
        var copy=new Button{Text=L.T("文字をコピー","Copy text"),AutoSize=true};
        var saveText=new Button{Text=L.T("テキストで保存","Save text"),AutoSize=true};
        var saveImage=new Button{Text=L.T("画像で保存","Save image"),AutoSize=true};
        attach.CheckedChanged+=(_,_)=>{connect(attach.Checked);result.Text=attach.Checked?L.T("接続しました。","Connected."):L.T("外しました。LPRINT は Printer error になります。","Disconnected. LPRINT now reports a printer error.");};
        clear.Click+=(_,_)=>{paper.Clear();Render();};
        copy.Click+=(_,_)=>{
            if(paper.Count==0){result.Text=L.T("印字がありません。","Nothing printed.");return;}
            try{Clipboard.SetText(paper.Text(),TextDataFormat.UnicodeText);result.Text=L.T($"{paper.Count}行をコピーしました。",$"Copied {paper.Count} lines.");}
            catch(Exception ex){result.Text=L.T("コピーできません: ","Could not copy: ")+ex.Message;}
        };
        saveText.Click+=(_,_)=>Save(L.T("テキスト","Text")+" (*.txt)|*.txt","txt",path=>File.WriteAllText(path,paper.Text(),new UTF8Encoding(false)));
        saveImage.Click+=(_,_)=>{
            if(font().Length==0){result.Text=NoFont;return;}
            Save("PNG (*.png)|*.png","png",path=>{using var b=paper.Render(font());b.Save(path,ImageFormat.Png);});
        };
        bar.Controls.AddRange([attach,clear,copy,saveText,saveImage]);
        Controls.Add(view);Controls.Add(result);Controls.Add(bar);Controls.Add(note);
        timer.Tick+=(_,_)=>Follow();
        Shown+=(_,_)=>{Render();timer.Start();};
        FormClosed+=(_,_)=>timer.Stop();
    }
    /// <summary>保存先を選んで write で書き、結果を下の欄に出す。</summary>
    /// <param name="filter">ファイルの種類</param>
    /// <param name="extension">既定の拡張子</param>
    /// <param name="write">保存する処理（引数は保存先）</param>
    void Save(string filter,string extension,Action<string> write)
    {
        if(paper.Count==0){result.Text=L.T("印字がありません。","Nothing printed.");return;}
        using var dialog=new SaveFileDialog{Filter=filter,DefaultExt=extension,FileName="CE-126P-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")};
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{write(dialog.FileName);result.Text=L.T("保存しました: ","Saved: ")+dialog.FileName;}
        catch(Exception ex){result.Text=L.T("保存できません: ","Could not save: ")+ex.Message;}
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    internal PaperView TestView=>view;
    internal string TestResult=>result.Text;
    // The line count goes in the title, so the label below keeps the last
    // save, copy or connect message while the machine goes on printing.
    // Without the ROM's font (a ROM whose glyphs for 0-3 are not where they are
    // looked for) the paper can only be blank, so say so instead.
    static string NoFont=>L.T("このROMの文字フォントが見つからないため、紙に文字を描けません。文字のコピーとテキスト保存は使えます。","The character font of this ROM was not found, so the paper stays blank. Copy text and Save text still work.");
    internal string TestNote=>note.Text;
    internal void TestFollow()=>Follow();
    /// <summary>紙かフォント（ROM）が変わっていれば描き直す（タイマーで呼ぶ）。</summary>
    void Follow(){if(shown!=paper.Version || !ReferenceEquals(shownFont,font()))Render();}
    /// <summary>紙かフォントが変わったときに、案内・タイトル（行数）・ビューを更新する。</summary>
    internal void Render()
    {
        shownFont=font();bool blank=shownFont.Length==0;note.Height=blank?50:30;
        note.Text=blank?NoFont:L.T("LPRINT・LLIST の印字（24桁）。","Output of LPRINT and LLIST (24 columns).");
        shown=paper.Version;view.PaperChanged();
        Text=L.T($"CE-126P（{paper.Count}行）",$"CE-126P ({paper.Count} lines)");
    }
}
