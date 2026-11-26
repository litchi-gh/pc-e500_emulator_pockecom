// SPDX-License-Identifier: GPL-2.0-or-later
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
namespace PokeCom;

/// <summary>デバッガのメモリ表示：1MB 全体の HEX ダンプ。カーソルの位置のバイトを16進のキー入力で書き換えられる（RAM のみ）。</summary>
internal sealed class HexPane : Control
{
    const int HexStart=7,GroupChars=32,Group=8;
    const double Size=13;
    readonly Func<Core?> read;
    readonly ScrollBar scroll;
    readonly double charWidth,lineHeight;
    int columns=16,address,cursor=-1,nibble,pending=-1;
    byte[] data=[];
    MemoryKind[] kinds=[];
    Core? shown;
    internal event Action<string>? Note;
    internal event Action? Moved;
    internal int Address=>address;
    internal int Caret=>cursor;
    internal int Columns {get=>columns;set{if(value==columns)return;columns=value;Goto(cursor>=0?cursor:address);InvalidateMeasure();InvalidateVisual();}}
    int LineChars=>HexStart+columns/Group*GroupChars-4;
    static int ByteColumn(int i)=>HexStart+i/Group*GroupChars+i%Group*3;
    static FormattedText Form(string text,Color color)=>new(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(Ui.Mono),Size,Paint.Solid(color));
    internal double PreferredWidth=>charWidth*(LineChars+1)+8;
    internal HexPane(Func<Core?> read,ScrollBar scroll) {
        this.read=read;this.scroll=scroll;Focusable=true;ClipToBounds=true;Cursor=new Cursor(StandardCursorType.Ibeam);
        var probe=Form(new string('0',40),Colors.Black);charWidth=probe.Width/40;lineHeight=Math.Ceiling(probe.Height);
        scroll.Minimum=0;scroll.SmallChange=1;
        scroll.Scroll+=(_,e)=>{int row=(int)e.NewValue;if(row!=address/columns)Goto(row*columns);};
        SizeChanged+=(_,_)=>Goto(address);
        GotFocus+=(_,_)=>InvalidateVisual();LostFocus+=(_,_)=>InvalidateVisual();
    }
    protected override Size MeasureOverride(Size available)=>new(PreferredWidth,0);
    int TotalRows=>0x100000/columns;
    internal int VisibleRows()=>Math.Max(4,Math.Min(TotalRows,(int)((Bounds.Height-4)/lineHeight)));
    /// <summary>value を含む行が先頭に来るように表示を動かし、読み直す。</summary>
    internal void Goto(int value) {
        int rows=VisibleRows();
        address=Math.Clamp(value,0,0x100000-rows*columns)&~(columns-1);
        scroll.Maximum=TotalRows-rows;scroll.ViewportSize=rows;scroll.LargeChange=rows;scroll.Value=address/columns;
        Reload();Moved?.Invoke();
    }
    /// <summary>カーソルを value に置き、見える位置まで動かす。</summary>
    internal void Jump(int value){cursor=Math.Clamp(value,0,0xfffff);nibble=0;pending=-1;Goto(cursor);InvalidateVisual();}
    void EnsureVisible() {
        if(cursor<0)return;
        int rows=VisibleRows();
        if(cursor<address)Goto(cursor);else if(cursor>=address+rows*columns)Goto(cursor-(rows-1)*columns);
    }
    /// <summary>表示している範囲のメモリを読み直す（変わったときだけ描き直す）。</summary>
    internal void Reload() {
        var core=read();
        if(core is null){if(data.Length>0 || shown is not null)InvalidateVisual();data=[];shown=null;return;}
        int count=Math.Min(VisibleRows()*columns,0x100000-address);
        byte[] next=core.Dump(address,count);
        var nextKinds=new MemoryKind[count];for(int i=0;i<count;i++)nextKinds[i]=MemoryMap.Kind(core,address+i);
        if(core==shown && next.AsSpan().SequenceEqual(data) && nextKinds.AsSpan().SequenceEqual(kinds))return;
        shown=core;data=next;kinds=nextKinds;InvalidateVisual();
    }
    int shownAddress=-1;
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        int rows=VisibleRows();bool ctrl=e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch(e.Key) {
        case Key.Left:Move(-1);break;
        case Key.Right:Move(1);break;
        case Key.Up:Move(-columns);break;
        case Key.Down:Move(columns);break;
        case Key.PageUp:if(cursor>=0)Move(-rows*columns);else Goto(address-rows*columns);break;
        case Key.PageDown:if(cursor>=0)Move(rows*columns);else Goto(address+rows*columns);break;
        case Key.Home:if(ctrl)Jump(0);else if(cursor>=0)Move(-(cursor%columns));break;
        case Key.End:if(ctrl)Jump(0xfffff);else if(cursor>=0)Move(columns-1-cursor%columns);break;
        default:
            // A digit with Ctrl, Alt or Shift is a shortcut or a symbol, not a nibble.
            int digit=e.KeyModifiers!=KeyModifiers.None?-1:e.Key is >=Key.D0 and <=Key.D9?e.Key-Key.D0:e.Key is >=Key.NumPad0 and <=Key.NumPad9?e.Key-Key.NumPad0:e.Key is >=Key.A and <=Key.F?e.Key-Key.A+10:-1;
            if(digit<0)return;
            Edit(digit);break;
        }
        e.Handled=true;
    }
    void Move(int delta){if(cursor<0)cursor=address;cursor=Math.Clamp(cursor+delta,0,0xfffff);nibble=0;pending=-1;EnsureVisible();Reload();InvalidateVisual();Moved?.Invoke();}
    // The high digit waits for the low one, and the byte is written once, whole:
    // written straight away, a running program would read the half-typed value.
    /// <summary>カーソルの位置のバイトの、今の桁を digit に書き換え、次の桁へ進む。</summary>
    void Edit(int digit) {
        var core=read();
        if(core is null || cursor<0){Note?.Invoke(L.T("書き換える位置をクリックしてください。","Click the byte to edit."));return;}
        if(MemoryMap.Kind(core,cursor)!=MemoryKind.Ram){Note?.Invoke(L.T($"{cursor:X5} は {MemoryMap.Name(core,cursor)} のため書き換えできません。内蔵RAM・RAMカードのみ書き換えられます。",$"{cursor:X5} is {MemoryMap.Name(core,cursor)} and cannot be edited. Only internal RAM and the RAM card can."));return;}
        byte old=core.Dump(cursor,1)[0];
        if(nibble==0) {
            pending=digit;nibble=1;Note?.Invoke(L.T($"{cursor:X5}: {old:X2} → {digit:X}_（下の桁を入力してください）",$"{cursor:X5}: {old:X2} → {digit:X}_ (type the low digit)"));
            InvalidateVisual();Moved?.Invoke();return;
        }
        byte value=pending>=0?(byte)((pending<<4)|digit):(byte)((old&0xf0)|digit);pending=-1;
        try{core.Write(cursor,[value]);}catch(Exception ex){nibble=0;Note?.Invoke(ex.Message);return;}
        Note?.Invoke($"{cursor:X5}: {old:X2} → {value:X2}");
        nibble=0;if(cursor<0xfffff)cursor++;
        EnsureVisible();Reload();InvalidateVisual();Moved?.Invoke();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        base.OnPointerPressed(e);Focus();
        if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || data.Length==0)return;
        var p=e.GetPosition(this);int row=(int)((p.Y-2)/lineHeight),column=(int)((p.X-4)/charWidth);
        if(column<HexStart || column>=LineChars)return;
        int c=column-HexStart,group=c/GroupChars,within=c%GroupChars;
        if(within>=Group*3-1 || within%3>=2)return;
        int offset=row*columns+group*Group+within/3;
        if(offset<data.Length){cursor=address+offset;nibble=within%3;pending=-1;InvalidateVisual();Moved?.Invoke();}
        e.Handled=true;
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e){base.OnPointerWheelChanged(e);Goto(address-(int)Math.Round(e.Delta.Y*3)*columns);}
    public override void Render(DrawingContext g) {
        g.FillRectangle(Paint.Solid(Ui.Input),new Rect(Bounds.Size));shownAddress=address;
        if(shown is null || data.Length==0){g.DrawText(Form(L.T("ROMが読み込まれていません。","No ROM is loaded."),Colors.Gray),new Point(4,2));return;}
        Color rom=Ui.Dark?Color.FromRgb(130,170,255):Color.FromRgb(0,0,140),empty=Ui.Dark?Color.FromRgb(110,110,110):Color.FromRgb(160,160,160);
        double X(int column)=>4+column*charWidth;
        for(int row=0;row*columns<data.Length;row++) {
            double y=2+row*lineHeight;int first=row*columns;
            // The whole line in one text, coloured by runs: a call per byte would be some 1300 a page.
            var line=new StringBuilder($"{address+first:X5}  ");var runs=new List<(int At,int Length,Color Color)>{(0,HexStart,Ui.SubText)};
            for(int c=0;c<columns && first+c<data.Length;c++) {
                int a=address+first+c,i=first+c;
                if(c>0 && c%Group==0){runs.Add((line.Length,4,Ui.SubText));line.Append("  - ");}
                if(a==cursor) {
                    double hx=X(ByteColumn(c));
                    g.FillRectangle(Paint.Solid(Ui.Dark?(IsFocused?Color.FromRgb(38,79,120):Color.FromRgb(60,60,60)):(IsFocused?Color.FromRgb(200,222,255):Color.FromRgb(225,225,225))),new Rect(hx,y,charWidth*2,lineHeight));
                    if(IsFocused){double ux=hx+nibble*charWidth;g.DrawLine(Paint.Pen(Ui.Dark?Color.FromRgb(120,180,255):Color.FromRgb(0,90,200),2),new Point(ux,y+lineHeight-1),new Point(ux+charWidth,y+lineHeight-1));}
                }
                runs.Add((line.Length,2,kinds[i] switch{MemoryKind.Ram=>Ui.Text,MemoryKind.Rom=>rom,_=>empty}));
                line.Append(a==cursor && pending>=0?$"{pending:X}{data[i]&15:X}":data[i].ToString("X2"));
                if(c%Group==Group-1) {
                    int sum=0;for(int k=0;k<Group;k++)sum+=data[i-k];
                    runs.Add((line.Length,3,Ui.SubText));line.Append(" : ");runs.Add((line.Length,2,Ui.NoteText));line.Append((sum&0xff).ToString("X2"));
                } else line.Append(' ');
            }
            var text=Form(line.ToString(),Ui.Text);
            foreach(var (at,length,color) in runs)text.SetForegroundBrush(Paint.Solid(color),at,Math.Min(length,line.Length-at));
            g.DrawText(text,new Point(4,y));
        }
    }
}

/// <summary>CPU のレジスタの表示：状態とサイクル数、各レジスタ、フラグ。</summary>
internal sealed class RegisterView : Control
{
    (string Name,string Value)[] cells=[];
    string state="",cycles="";
    Color stateColor;
    bool carry,zero;
    internal RegisterView(){Height=214;}
    internal void Show(Core core,int status) {
        uint ba=core.Register(2),i=core.Register(3),f=core.Register(10);
        cells=[("A",$"{core.Register(0):X2}"),("B",$"{ba>>8:X2}"),("BA",$"{ba:X4}"),("I",$"{i:X4}"),("IL",$"{core.Register(1):X2}"),("IH",$"{i>>8:X2}"),("F",$"{f:X2}"),("PC",$"{core.Register(9):X5}"),
            ("X",$"{core.Register(4):X5}"),("Y",$"{core.Register(5):X5}"),("U",$"{core.Register(7):X5}"),("S",$"{core.Register(8):X5}")];
        (state,stateColor)=(status&4)!=0?(L.T("エラー停止","Fault"),Color.FromRgb(196,43,28)):(status&1)!=0?(L.T("電源OFF","Power OFF"),Color.FromRgb(128,128,128)):(status&2)!=0?("HALT",Color.FromRgb(202,128,0)):(L.T("実行中","Running"),Color.FromRgb(16,140,60));
        cycles=L.T($"サイクル {core.Cycles:N0}",$"Cycles {core.Cycles:N0}");carry=(f&1)!=0;zero=(f&2)!=0;InvalidateVisual();
    }
    static FormattedText Form(string text,Color color,bool mono=false)=>new(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(mono?Ui.Mono:Paint.Ui),mono?13:12,Paint.Solid(color));
    public override void Render(DrawingContext g) {
        if(cells.Length==0)return;
        var badge=Form(state,Colors.White);var box=new Rect(0,0,badge.Width+20,22);
        g.DrawRectangle(Paint.Solid(stateColor),null,new RoundedRect(box,11));g.DrawText(badge,new Point(10,(22-badge.Height)/2));
        var count=Form(cycles,Ui.SubText);g.DrawText(count,new Point(box.Right+10,(22-count.Height)/2));
        double gap=6,top=32,w=(Bounds.Width-gap*3)/4,h=44;
        var tile=Paint.Solid(Ui.Dark?Color.FromRgb(50,50,50):Color.FromRgb(243,245,248));
        for(int n=0;n<cells.Length;n++) {
            var r=new Rect(n%4*(w+gap),top+n/4*(h+gap),w,h);
            g.DrawRectangle(tile,null,new RoundedRect(r,5));
            g.DrawText(Form(cells[n].Name,Ui.SubText),new Point(r.X+8,r.Y+4));g.DrawText(Form(cells[n].Value,Ui.Text,true),new Point(r.X+8,r.Y+21));
        }
        double fy=top+3*(h+gap)+2,fx=0;
        foreach(var (name,on) in new[]{(L.T("C（キャリー）","C (carry)"),carry),(L.T("Z（ゼロ）","Z (zero)"),zero)}) {
            g.DrawEllipse(Paint.Solid(on?Ui.Accent:Ui.Dark?Color.FromRgb(70,70,70):Color.FromRgb(215,215,215)),null,new Rect(fx,fy+3,12,12));
            g.DrawText(Form(name,on?Ui.Text:Ui.SubText),new Point(fx+18,fy+1));fx+=130;
        }
    }
}

/// <summary>デバッグウィンドウ：CPU のレジスタ・内部メモリ・I/O ポートの表示と、1MB のメモリの HEX 表示・書き換え、BIN の読込・保存。</summary>
internal sealed class DebugWindow : AppWindow
{
    static readonly (string Name,int Address)[] Ports=[("BP",0xec),("PX",0xed),("PY",0xee),("AMC",0xef),("KOL",0xf0),("KOH",0xf1),("KI",0xf2),("EOL",0xf3),("EOH",0xf4),("EIL",0xf5),("EIH",0xf6),("UCR",0xf7),
        ("USR",0xf8),("RXD",0xf9),("TXD",0xfa),("IMR",0xfb),("ISR",0xfc),("SCR",0xfd),("LCC",0xfe),("SSR",0xff)];
    readonly Func<Core?> read;
    readonly HexPane memory;
    readonly RegisterView registers=new();
    readonly MemoryBar internalMap=new(),cardMap=new(){Legend=false};
    readonly TextBox addressBox=new(){Width=90,FontFamily=Ui.Mono,Text="B0000"};
    readonly CheckBox live=new(){Content=L.T("自動更新","Auto refresh"),IsChecked=true};
    readonly SelectableTextBlock cpu=new(){FontFamily=Ui.Mono,FontSize=12.5};
    readonly TextBlock region=new(){FontSize=12},note=new(){FontSize=12,TextWrapping=TextWrapping.Wrap};
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    internal DebugWindow(Func<Core?> read) : base(L.T("デバッグ / CPUレジスタ・メモリ","Debug / CPU registers and memory"),1120,800) {
        this.read=read;MinWidth=640;MinHeight=480;
        var scroll=new ScrollBar{Orientation=Orientation.Vertical,AllowAutoHide=false};
        memory=new HexPane(read,scroll);
        note.Foreground=Paint.Solid(Ui.NoteText);
        Button Jump(string text,Func<Core,int> target)=>Small(text,()=>{if(read() is {} core){memory.Jump(target(core));Render();}});
        static Button Small(string text,Action click){var b=Ui.Button(text,click);b.MinWidth=0;b.Padding=new Thickness(8,5);b.Margin=new Thickness(0,0,4,4);return b;}
        var width=new ComboBox{ItemsSource=(string[])[L.T("16 バイト/行","16 bytes/row"),L.T("8 バイト/行","8 bytes/row")],SelectedIndex=0,Margin=new Thickness(8,0,0,4)};
        width.SelectionChanged+=(_,_)=>{memory.Columns=width.SelectedIndex==0?16:8;Render();};
        var bar=new WrapPanel();var label=Ui.Label(L.T("アドレス","Address"));label.Margin=new Thickness(0,0,8,4);addressBox.Margin=new Thickness(0,0,8,4);live.Margin=new Thickness(8,0,8,4);
        bar.Children.AddRange([label,addressBox,Jump("PC",c=>(int)c.Register(9)),Jump("S",c=>(int)c.Register(8)),Jump("X",c=>(int)c.Register(4)),Jump("Y",c=>(int)c.Register(5)),Jump("U",c=>(int)c.Register(7)),
            Jump(L.T("内蔵RAM","Internal RAM"),c=>0xc0000-c.InternalKB*1024),Jump(L.T("カード","Card"),_=>0x40000),Jump("ROM",_=>0xc0000),Small(L.T("更新","Refresh"),Render),live,
            Small(L.T("BIN書込み","Load BIN"),()=>_=Transfer(false)),Small(L.T("BIN書出し","Save BIN"),()=>_=Transfer(true)),width]);
        var maps=Ui.CardOf(L.T("メモリマップ（押すとその区画へ）","Memory map (click to go there)"),Ui.Stack(internalMap,cardMap));maps.Margin=new Thickness(0,4,0,8);
        foreach(var map in new[]{internalMap,cardMap})map.RegionClicked+=r=>{memory.Jump(r.Start);Render();};
        var caption=Ui.Heading(L.T("内部 RAM のポインタ・I/O と 256 バイト","Internal RAM pointers, I/O and its 256 bytes"));
        var left=Ui.CardOf("CPU",new ScrollViewer{Content=new Border{Padding=new Thickness(0,0,16,0),Child=Ui.Stack(registers,caption,cpu)}});left.Width=458;left.Margin=new Thickness(0,0,8,0);
        var hex=new DockPanel();DockPanel.SetDock(scroll,Dock.Right);hex.Children.AddRange([scroll,memory]);
        var body=new DockPanel();
        foreach(var (c,d) in new (Control,Dock)[]{(bar,Dock.Top),(maps,Dock.Top),(left,Dock.Left)}){DockPanel.SetDock(c,d);body.Children.Add(c);}
        body.Children.Add(Ui.CardOf(L.T("メモリ","Memory"),hex));
        Fill(body,Ui.Footer(Ui.Stack(note,region),Ui.Button(L.T("閉じる","Close"),Close)));
        addressBox.KeyDown+=(_,e)=>{if(e.Key!=Key.Enter)return;e.Handled=true;if(TryParse(addressBox.Text??"",out int v))memory.Jump(v);Render();};
        memory.Moved+=()=>{int at=memory.Caret>=0?memory.Caret:memory.Address;addressBox.Text=at.ToString("X5");ShowRegion();internalMap.Marker=cardMap.Marker=at;};
        memory.Note+=text=>note.Text=text;
        note.Text=L.T("メモリ欄をクリックして16進を入力すると、その場で書き換えます（内蔵RAM・RAMカードのみ）。灰色は未定義、青はROM、「:」の右は8バイトのチェックサムです。","Click a byte and type hex digits to edit it in place (internal RAM and RAM card only). Grey is unmapped, blue is ROM, and the byte after ':' is the checksum of the eight before it.");
        timer.Tick+=(_,_)=>{if(live.IsChecked==true)Render();};
        Opened+=(_,_)=>{if(read() is {} core)memory.Jump(0xc0000-core.InternalKB*1024);Render();timer.Start();};
        Closed+=(_,_)=>timer.Stop();
    }
    /// <summary>16進のアドレスを読む（&amp;H・0x・$ の付いた形も受け付ける）。</summary>
    internal static bool TryParse(string text,out int value) {
        text=text.Trim();
        if(text.StartsWith("&H",StringComparison.OrdinalIgnoreCase) || text.StartsWith("0x",StringComparison.OrdinalIgnoreCase))text=text[2..];
        else if(text.StartsWith('$') || text.StartsWith('&'))text=text[1..];
        return int.TryParse(text,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value);
    }
    void ShowRegion() {
        var core=read();if(core is null){region.Text="";return;}
        int at=memory.Address;
        region.Text=$"{at:X5}-{at+memory.VisibleRows()*memory.Columns-1:X5}   {MemoryMap.Name(core,at)}"+(memory.Caret>=0?L.T($"   カーソル {memory.Caret:X5} ({MemoryMap.Name(core,memory.Caret)})",$"   Caret {memory.Caret:X5} ({MemoryMap.Name(core,memory.Caret)})"):"");
    }
    void Render() {
        var core=read();
        if(core is null){cpu.Text=L.T("ROMが読み込まれていません。","No ROM is loaded.");memory.Reload();region.Text="";return;}
        byte[] imem=core.InternalMemory();var text=new StringBuilder();
        for(int p=0;p<Ports.Length;p+=4)text.AppendLine(string.Join("  ",Ports.Skip(p).Take(4).Select(x=>$"{x.Name,-3} {x.Address:X2}={imem[x.Address]:X2}")));
        text.AppendLine();
        for(int row=0;row<256;row+=16)text.AppendLine($"{row:X2}  "+string.Join(" ",imem.Skip(row).Take(16).Select(b=>b.ToString("X2"))));
        if(cpu.SelectionStart==cpu.SelectionEnd)cpu.Text=text.ToString().TrimEnd();
        registers.Show(core,Native.e500_status(core.Handle));
        internalMap.BarTitle=L.T($"内蔵 RAM {core.InternalKB}KB",$"Internal RAM {core.InternalKB}KB");internalMap.SetRegions(RamLayout.Internal(core));
        cardMap.IsVisible=core.CardKB>0;
        if(cardMap.IsVisible){cardMap.BarTitle=L.T($"RAM カード {core.CardKB}KB",$"RAM card {core.CardKB}KB");cardMap.SetRegions(RamLayout.Card(core));}
        memory.Reload();ShowRegion();
    }
    async Task Transfer(bool saving) {
        var dialog=new BinWindow(read,saving,memory.Caret>=0?memory.Caret:memory.Address);
        await dialog.ShowDialog(this);
        if(dialog.Done is not int at)return;
        note.Text=saving?L.T($"{at:X5} からメモリを書き出しました。",$"Memory saved from {at:X5}."):L.T($"{at:X5} へ書き込みました。",$"Written at {at:X5}.");
        memory.Jump(at);Render();
    }
}

/// <summary>BIN の読込・保存のウィンドウ。</summary>
internal sealed class BinWindow : AppWindow
{
    /// <summary>成功したときの先頭アドレス。</summary>
    internal int? Done {get;private set;}
    internal BinWindow(Func<Core?> read,bool saving,int suggested) : base(saving?L.T("BIN書出し (メモリ → ファイル)","Save BIN (memory → file)"):L.T("BIN書込み (ファイル → メモリ)","Load BIN (file → memory)"),600) {
        var file=new TextBox{Width=330};var address=new TextBox{Width=100,FontFamily=Ui.Mono,Text=suggested.ToString("X5")};var length=new TextBox{Width=100,FontFamily=Ui.Mono,Text="100"};
        var header=new CheckBox{Content=saving?L.T("SAVEMヘッダー(16バイト)を付けて書き出す","Write with a 16-byte SAVEM header"):L.T("SAVEMヘッダーを除き、ヘッダーの開始アドレスへ書き込む","Strip the SAVEM header and load at its start address"),IsChecked=saving,IsEnabled=saving};
        var info=Ui.Note(saving?L.T("指定した範囲のメモリをそのままファイルへ書き出します。","Writes the selected memory range to a file as it is."):L.T("ファイルの内容を指定アドレスから書き込みます。内蔵RAM・RAMカードの範囲のみです。","Writes the file from the given address: internal RAM and the RAM card only."));
        byte[]? loaded=null;
        void Inspect() {
            loaded=null;string path=file.Text??"";
            if(!File.Exists(path)){info.Text=L.T("ファイルが見つかりません。","File not found.");return;}
            if(new FileInfo(path).Length>0x100000+BinaryTransfer.HeaderLength){info.Text=L.T("1MBを超えるファイルは書き込めません。","Files larger than 1MB cannot be loaded.");return;}
            loaded=File.ReadAllBytes(path);
            if(BinaryTransfer.TryReadHeader(loaded,out int start,out int count)) {
                header.IsEnabled=true;header.IsChecked=true;address.Text=start.ToString("X5");
                info.Text=L.T($"{loaded.Length}バイト。SAVEMヘッダーを検出: 開始 {start:X5}、長さ {count:X}h ({count}バイト)。",$"{loaded.Length} bytes. SAVEM header found: start {start:X5}, length {count:X}h ({count} bytes).");
            } else {
                header.IsEnabled=false;header.IsChecked=false;
                info.Text=L.T($"{loaded.Length}バイト ({loaded.Length:X}h)。ヘッダーなし: アドレス欄の位置から書き込みます。",$"{loaded.Length} bytes ({loaded.Length:X}h). No header: loads at the address given above.");
            }
        }
        file.TextChanged+=(_,_)=>loaded=null;
        if(!saving)header.IsCheckedChanged+=(_,_)=>address.IsEnabled=header.IsChecked!=true;
        var browse=Ui.Button(L.T("参照","Browse"),async ()=>{
            string? path=saving?await Ui.SaveFile(this,null,"MEMORY.BIN","bin",Ui.Type(L.T("バイナリ","Binary"),"*.bin")):await Ui.OpenFile(this,null,Ui.Type(L.T("バイナリ","Binary"),"*.bin","*.ram","*.rom"));
            if(path is null)return;
            file.Text=path;
            if(!saving)try{Inspect();}catch(Exception ex){info.Text=L.T("読み込めません: ","Could not read the file: ")+ex.Message;}
        });
        var rows=Ui.Stack(Ui.Row(Ui.Label(L.T("ファイル","File"),120),file,browse),Ui.Row(Ui.Label(L.T("アドレス (16進)","Address (hex)"),120),address));
        if(saving)rows.Children.Add(Ui.Row(Ui.Label(L.T("長さ (16進)","Length (hex)"),120),length));
        rows.Children.Add(header);rows.Children.Add(info);
        var ok=Ui.Button(saving?L.T("書出し","Save"):L.T("書込み","Load"),async ()=>{
            try {
                var core=read()??throw new InvalidOperationException(L.T("ROMが読み込まれていません。","No ROM is loaded."));
                if(!DebugWindow.TryParse(address.Text??"",out int at))throw new FormatException(L.T("アドレスは16進で入力してください。","Enter the address in hex."));
                if(saving) {
                    if(!DebugWindow.TryParse(length.Text??"",out int count))throw new FormatException(L.T("長さは16進で入力してください。","Enter the length in hex."));
                    if(string.IsNullOrWhiteSpace(file.Text))throw new InvalidOperationException(L.T("書き出すファイルを指定してください。","Choose the file to save to."));
                    AtomicFile.Write(file.Text,BinaryTransfer.Save(core,at,count,header.IsChecked==true));Done=at;
                } else {
                    if(loaded is null)Inspect();
                    if(loaded is null)throw new FileNotFoundException(L.T("読み込むファイルを指定してください。","Choose the file to load."));
                    Done=BinaryTransfer.Load(core,loaded,at,header.IsChecked==true).Address;
                }
                Close();
            } catch(Exception ex) {await Ask.Message(this,ex.Message,Title??"");}
        },true);ok.IsDefault=true;
        Fill(Ui.CardOf(null,rows),Ui.Footer(null,ok,Ui.Button(L.T("キャンセル","Cancel"),Close)));
    }
}
