// SPDX-License-Identifier: GPL-2.0-or-later
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
namespace PokeCom;

/// <summary>テキストボックスの中身を、スクロール位置を保ち、ちらつかせずに入れ替える。</summary>
internal static class LiveText
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
    const int EM_GETFIRSTVISIBLELINE=0xce,EM_LINESCROLL=0xb6,WM_SETREDRAW=0x0b;
    // Assigning Text scrolls a TextBox back to the top and repaints the whole
    // control, which a periodic refresh would turn into a flicker.
    /// <summary>中身を入れ替える（同じなら何もしない）。</summary>
    /// <param name="box">テキストボックス</param>
    /// <param name="value">新しい中身</param>
    // A selection being made (to copy registers) is not swept away by the refresh,
    // and with follow a view at the end follows what is added there (the CE-140F
    // trace, which also loses lines from its start, so the top line counts for
    // nothing there). An empty box is not at the end: the first text shows its top.
    // 選んでいる範囲（レジスタをコピーするため）があれば入れ替えない。follow なら、最後の行を見ているとき増えた分に追従する（空の欄は先頭から）。
    internal static void Update(TextBox box,string value,bool follow=false) {
        if(box.Text==value || box.SelectionLength>0)return;
        var top=SendMessage(box.Handle,EM_GETFIRSTVISIBLELINE,IntPtr.Zero,IntPtr.Zero);
        int visible=Math.Max(1,box.ClientSize.Height/Math.Max(1,box.Font.Height));
        bool atEnd=follow && box.TextLength>0 && (int)top+visible>=box.GetLineFromCharIndex(box.TextLength-1);
        SendMessage(box.Handle,WM_SETREDRAW,IntPtr.Zero,IntPtr.Zero);
        box.Text=value;
        if(atEnd)top=Math.Max(0,box.GetLineFromCharIndex(Math.Max(0,box.TextLength-1))-visible+1);
        SendMessage(box.Handle,EM_LINESCROLL,IntPtr.Zero,top);
        SendMessage(box.Handle,WM_SETREDRAW,1,IntPtr.Zero);
        box.Invalidate();
    }
}

/// <summary>
/// デバッグウィンドウ：CPU のレジスタ・内部メモリ・I/O ポートの表示と、
/// 1MB のメモリの HEX 表示・書き換え、BIN の読込・保存、チェックサム。
/// </summary>
internal sealed class DebugDialog : ThemedForm
{
    // e500_reg ids, in the order of the SC62015 REGNAME enum.
    const int RegA=0,RegIL=1,RegBA=2,RegI=3,RegX=4,RegY=5,RegU=7,RegS=8,RegP=9,RegF=10;
    // Internal RAM addresses the CPU uses as pointers and as I/O ports.
    static readonly (string Name,int Address)[] Ports=[
        ("BP",0xec),("PX",0xed),("PY",0xee),("AMC",0xef),
        ("KOL",0xf0),("KOH",0xf1),("KI",0xf2),("EOL",0xf3),
        ("EOH",0xf4),("EIL",0xf5),("EIH",0xf6),("UCR",0xf7),
        ("USR",0xf8),("RXD",0xf9),("TXD",0xfa),("IMR",0xfb),
        ("ISR",0xfc),("SCR",0xfd),("LCC",0xfe),("SSR",0xff)];
    /// <summary>内部 RAM の 256 バイトを並べる1行のバイト数。</summary>
    const int Columns=16;
    readonly Func<Core?> read;
    readonly TextBox cpu=new(){Multiline=true,ReadOnly=true,WordWrap=false,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Font=Fonts.Mono,BorderStyle=BorderStyle.None};
    /// <summary>レジスタの欄（状態・サイクル・各レジスタ・フラグ）。</summary>
    readonly RegisterView registers=new(){Dock=DockStyle.Top};
    /// <summary>内蔵 RAM と RAM カードのメモリマップ（押した区画へ飛ぶ）。</summary>
    readonly MemoryMapView internalMap=new(){Dock=DockStyle.Top,Legend=true},cardMap=new(){Dock=DockStyle.Top,Legend=false};
    /// <summary>メモリの HEX 表示。</summary>
    internal readonly HexView Memory;
    readonly TextBox addressBox=new(){Width=88,Font=Fonts.Mono,Text="B0000"};
    readonly CheckBox live=new(){Text=L.T("自動更新","Auto refresh"),Checked=true,AutoSize=true,Margin=new Padding(4,9,0,0)};
    readonly Label region=new(){Dock=DockStyle.Bottom,Height=24,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(12,0,0,0),Font=Fonts.Small};
    readonly Label note=new(){Dock=DockStyle.Bottom,Height=24,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(12,0,0,0),Font=Fonts.Small,ForeColor=Color.FromArgb(0,80,160)};
    readonly CardPanel mapCard=new(){Title=L.T("メモリマップ（押すとその区画へ）","Memory map (click to go there)"),Dock=DockStyle.Fill};
    readonly System.Windows.Forms.Timer timer=new(){Interval=250};
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="read">今のコアを返す関数（ROM の切り替えに追従するため）</param>
    internal DebugDialog(Func<Core?> read)
    {
        this.read=read;
        Memory=new HexView(read){Dock=DockStyle.Fill};
        Text=L.T("デバッグ / CPUレジスタ・メモリ","Debug / CPU registers and memory");
        StartPosition=FormStartPosition.CenterScreen;Font=Fonts.Ui;
        // Both panes are fixed-width hex, so measure the font rather than guess:
        // the internal RAM rows are 51 columns and the memory rows 72.
        var probe=TextRenderer.MeasureText(new string('0',40),cpu.Font);
        int charWidth=Math.Max(6,probe.Width/40),scrollbar=SystemInformation.VerticalScrollBarWidth;
        int lineHeight=Math.Max(12,probe.Height);
        int leftWidth=charWidth*52+scrollbar+10;
        ClientSize=new Size(leftWidth+Memory.PreferredWidth+64,Math.Max(760,lineHeight*40));
        MinimumSize=new Size(charWidth*60,lineHeight*28);
        var main=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12,10,12,4)};
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));main.RowStyles.Add(new RowStyle(SizeType.AutoSize));main.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Controls.Add(main);Controls.Add(note);Controls.Add(region);
        // The map across the top: the internal RAM, and the card's under it when there is one.
        var mapHolder=new Panel{Dock=DockStyle.Fill,Margin=new Padding(0,8,0,0),Height=10};
        mapHolder.Controls.Add(mapCard);
        mapCard.Controls.Add(cardMap);mapCard.Controls.Add(internalMap);
        main.Controls.Add(mapHolder,0,1);
        foreach(var map in new[]{internalMap,cardMap})map.RegionClicked+=r=>{Memory.Jump(r.Start);Render();};
        void SizeMap(){
            internalMap.Height=internalMap.PreferredHeight;cardMap.Height=cardMap.Visible?cardMap.PreferredHeight+LogicalToDeviceUnits(6):0;
            mapHolder.Height=mapCard.Padding.Vertical+internalMap.Height+cardMap.Height;
        }
        mapCard.Resize+=(_,_)=>SizeMap();mapSized=SizeMap;

        var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new Padding(0)};main.Controls.Add(bar,0,0);
        bar.Controls.Add(new Label{Text=L.T("アドレス","Address"),AutoSize=true,Margin=new Padding(0,9,4,0)});
        bar.Controls.Add(addressBox);
        void Jump(string text,Func<Core,int> target) {
            var b=new FluentButton{Text=text,AutoSize=true,Margin=new Padding(3,0,0,0)};
            b.Click+=(_,_)=>{if(read() is {} core){Memory.Jump(target(core));Render();}};
            bar.Controls.Add(b);
        }
        Jump("PC",c=>(int)c.Register(RegP));Jump("S",c=>(int)c.Register(RegS));
        Jump("X",c=>(int)c.Register(RegX));Jump("Y",c=>(int)c.Register(RegY));Jump("U",c=>(int)c.Register(RegU));
        Jump(L.T("内蔵RAM","Internal RAM"),c=>0xc0000-c.InternalKB*1024);Jump(L.T("カード","Card"),_=>0x40000);Jump("ROM",_=>0xc0000);
        var refresh=new FluentButton{Text=L.T("更新","Refresh"),AutoSize=true,Margin=new Padding(12,0,0,0)};
        refresh.Click+=(_,_)=>Render();bar.Controls.Add(refresh);bar.Controls.Add(live);
        var load=new FluentButton{Text=L.T("BIN書込み","Load BIN"),AutoSize=true,Margin=new Padding(12,0,0,0)};
        var save=new FluentButton{Text=L.T("BIN書出し","Save BIN"),AutoSize=true,Margin=new Padding(3,0,0,0)};
        load.Click+=(_,_)=>Transfer(false);save.Click+=(_,_)=>Transfer(true);
        bar.Controls.Add(load);bar.Controls.Add(save);
        // 16 or 8 bytes a row; each group of eight carries its checksum either way.
        var width=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=140,Margin=new Padding(12,4,0,0)};
        width.Items.AddRange([L.T("16 バイト/行","16 bytes/row"),L.T("8 バイト/行","8 bytes/row")]);width.SelectedIndex=0;
        // As wide as its longest choice (English is longer), plus the drop-down arrow.
        width.Width=width.Items.Cast<string>().Max(t=>TextRenderer.MeasureText(t,width.Font).Width)+SystemInformation.VerticalScrollBarWidth+12;
        width.SelectedIndexChanged+=(_,_)=>{Memory.Columns=width.SelectedIndex==0?16:8;Render();};
        bar.Controls.Add(width);rowWidth=width;
        // The window is never narrower than its toolbar, which would wrap onto a second row.
        Load+=(_,_)=>{int need=bar.PreferredSize.Width+main.Padding.Horizontal+(Width-ClientSize.Width);if(Width<need)Width=need;};
        // Wider than its 16-byte rows shows only blank; narrower suits 8-byte rows.
        Load+=(_,_)=>LockWidth(true);

        var panes=new SplitContainer{Dock=DockStyle.Fill,Margin=new Padding(0,8,0,0),FixedPanel=FixedPanel.Panel1,Panel1MinSize=140,Panel2MinSize=140,SplitterWidth=8};
        main.Controls.Add(panes,0,2);
        // Left, the registers and the internal RAM; right, the memory: each on a card.
        var cpuCard=new CardPanel{Title="CPU",Dock=DockStyle.Fill};
        var imemCaption=new Label{Text=L.T("内部 RAM のポインタ・I/O と 256 バイト","Internal RAM pointers, I/O and its 256 bytes"),Dock=DockStyle.Top,Height=LogicalToDeviceUnits(26),Font=Fonts.UiBold,TextAlign=ContentAlignment.BottomLeft};
        cpuCard.Controls.Add(cpu);cpuCard.Controls.Add(imemCaption);cpuCard.Controls.Add(registers);
        var memoryCard=new CardPanel{Title=L.T("メモリ","Memory"),Dock=DockStyle.Fill};
        memoryCard.Controls.Add(Memory);
        panes.Panel1.Controls.Add(cpuCard);panes.Panel2.Controls.Add(memoryCard);
        Load+=(_,_)=>panes.SplitterDistance=Math.Min(leftWidth,Math.Max(panes.Panel1MinSize,panes.Width-panes.Panel2MinSize-panes.SplitterWidth));

        addressBox.KeyDown+=(_,e)=>{
            if(e.KeyCode!=Keys.Enter)return;
            e.Handled=e.SuppressKeyPress=true;
            if(TryParseAddress(addressBox.Text,out int value))Memory.Jump(value);
            Render();
        };
        Memory.Moved+=()=>{addressBox.Text=(Memory.Caret>=0?Memory.Caret:Memory.Address).ToString("X5");UpdateRegion();internalMap.Marker=cardMap.Marker=Memory.Caret>=0?Memory.Caret:Memory.Address;};
        Memory.Note+=text=>note.Text=text;
        timer.Tick+=(_,_)=>{if(live.Checked)Render();};
        // Open on the machine own RAM rather than on its mirror below it.
        Shown+=(_,_)=>{if(read() is {} core)Memory.Jump(0xc0000-core.InternalKB*1024);Render();timer.Start();};
        FormClosed+=(_,_)=>timer.Stop();
        note.Text=L.T("メモリ欄をクリックして16進を入力すると、その場で書き換えます（内蔵RAM・RAMカードのみ）。灰色は未定義、青はROM、「:」の右は8バイトのチェックサムです。","Click a byte and type hex digits to edit it in place (internal RAM and RAM card only). Grey is unmapped, blue is ROM; after each \":\" is the checksum of eight bytes.");
    }
    /// <summary>メモリマップの高さを合わせ直す（カードの有無で変わる）。</summary>
    Action? mapSized;
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    /// <summary>16進のアドレスを読む（&H・0x・$・&amp; の付いた形も受け付ける）。</summary>
    /// <param name="text">入力</param>
    /// <param name="value">読んだ値</param>
    /// <returns>読めたか</returns>
    internal static bool TryParseAddress(string text,out int value) {
        text=text.Trim();
        if(text.StartsWith("&H",StringComparison.OrdinalIgnoreCase)||text.StartsWith("0x",StringComparison.OrdinalIgnoreCase))text=text[2..];
        else if(text.StartsWith('$')||text.StartsWith('&'))text=text[1..];
        return int.TryParse(text,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value);
    }
    /// <summary>BIN の読込・保存のウィンドウを開き、終わったらその位置を表示する。</summary>
    /// <param name="saving">保存なら true</param>
    void Transfer(bool saving) {
        using var dialog=new BinaryTransferDialog(read,saving,Memory.Caret>=0?Memory.Caret:Memory.Address);
        if(dialog.ShowDialog(this)!=DialogResult.OK||dialog.Done is not {} at)return;
        note.Text=saving?L.T($"{at:X5} からメモリを書き出しました。",$"Memory saved from {at:X5}."):L.T($"{at:X5} へ書き込みました。",$"Written at {at:X5}.");
        Memory.Jump(at);Render();
    }
    ComboBox? rowWidth;
    /// <summary>テスト用：1行のバイト数を選ぶ（0 で 16、1 で 8）。</summary>
    internal void TestRowWidth(int index)=>rowWidth!.SelectedIndex=index;
    /// <summary>表示している範囲とカーソルの位置が、どの領域（ROM・RAM など）かを表示する。</summary>
    void UpdateRegion() {
        var core=read();
        if(core is null){region.Text="";return;}
        int rows=Memory.VisibleRows(),address=Memory.Address;
        string caret=Memory.Caret>=0?L.T($"   カーソル {Memory.Caret:X5} ({MemoryMap.Name(core,Memory.Caret)})",$"   Caret {Memory.Caret:X5} ({MemoryMap.Name(core,Memory.Caret)})"):"";
        region.Text=$"{address:X5}-{address+rows*Memory.Columns-1:X5}   {MemoryMap.Name(core,address)}{caret}";
    }
    /// <summary>レジスタ・内部メモリ・メモリの表示を読み直す。</summary>
    internal void Render() {
        var core=read();
        if(core is null){LiveText.Update(cpu,L.T("ROMが読み込まれていません。","No ROM is loaded."));Memory.Reload();region.Text="";return;}
        int status=Native.e500_status(core.Handle);
        byte[] imem=core.InternalMemory();
        var text=new StringBuilder();
        // The registers are drawn above (RegisterView); the text keeps the internal RAM.
        for(int p=0;p<Ports.Length;p+=4) {
            var line=new StringBuilder();
            for(int q=p;q<Math.Min(p+4,Ports.Length);q++)line.Append($"{Ports[q].Name,-3} {Ports[q].Address:X2}={imem[Ports[q].Address]:X2}  ");
            text.AppendLine(line.ToString().TrimEnd());
        }
        text.AppendLine();
        for(int row=0;row<256;row+=Columns) {
            var line=new StringBuilder($"{row:X2}  ");
            for(int c=0;c<Columns;c++)line.Append($"{imem[row+c]:X2} ");
            text.AppendLine(line.ToString().TrimEnd());
        }
        LiveText.Update(cpu,text.ToString());
        registers.Show(core,status);
        internalMap.Title=L.T($"内蔵 RAM {core.InternalKB}KB",$"Internal RAM {core.InternalKB}KB");internalMap.SetRegions(RamLayout.Internal(core));
        cardMap.Visible=core.CardKB>0;
        if(cardMap.Visible){cardMap.Title=L.T($"RAM カード {core.CardKB}KB",$"RAM card {core.CardKB}KB");cardMap.SetRegions(RamLayout.Card(core));}
        mapSized?.Invoke();
        Memory.Reload();UpdateRegion();
    }
    // Test hooks: the caret and a typed hex digit, as the mouse and keyboard deliver them.
    internal void TestType(Keys key)=>Memory.TestKey(key);
    /// <summary>レジスタ欄の先頭に見えている行（テスト用）。</summary>
    internal int TestCpuTopLine=>(int)SendMessage(cpu.Handle,0xce,IntPtr.Zero,IntPtr.Zero);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
}

// The registers as tiles rather than lines of text: the state as a coloured
// badge with the cycle count, each register its name over its value, and the
// two flags as lamps that light when set.
// レジスタを文字の行ではなく欄で：状態は色付きの印とサイクル数、各レジスタは名前の下に値、フラグは立つと点く印。
/// <summary>CPU のレジスタの表示。</summary>
internal sealed class RegisterView : Control
{
    (string Name,string Value)[] cells=[];
    string state="";Color stateColor;string cycles="";bool carry,zero;
    internal RegisterView() {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        SetStyle(ControlStyles.Selectable,false);Height=LogicalToDeviceUnits(214);
    }
    /// <summary>今のレジスタを描く。</summary>
    internal void Show(Core core,int status) {
        uint ba=core.Register(2),i=core.Register(3),f=core.Register(10);
        cells=[("A",$"{core.Register(0):X2}"),("B",$"{ba>>8:X2}"),("BA",$"{ba:X4}"),("I",$"{i:X4}"),
            ("IL",$"{core.Register(1):X2}"),("IH",$"{i>>8:X2}"),("F",$"{f:X2}"),("PC",$"{core.Register(9):X5}"),
            ("X",$"{core.Register(4):X5}"),("Y",$"{core.Register(5):X5}"),("U",$"{core.Register(7):X5}"),("S",$"{core.Register(8):X5}")];
        (state,stateColor)=(status&4)!=0?(L.T("エラー停止","Fault"),Color.FromArgb(196,43,28)):(status&1)!=0?(L.T("電源OFF","Power OFF"),Color.FromArgb(128,128,128))
            :(status&2)!=0?("HALT",Color.FromArgb(202,128,0)):(L.T("実行中","Running"),Color.FromArgb(16,140,60));
        cycles=L.T($"サイクル {core.Cycles:N0}",$"Cycles {core.Cycles:N0}");
        carry=(f&1)!=0;zero=(f&2)!=0;Invalidate();
    }
    int S(int v)=>LogicalToDeviceUnits(v);
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(Parent?.BackColor??Theme.Card);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if(cells.Length==0)return;
        // The state badge and the cycles.
        var badgeText=TextRenderer.MeasureText(state,Fonts.Small,Size.Empty,TextFormatFlags.NoPadding);
        var badge=new Rectangle(0,0,badgeText.Width+S(20),S(22));
        using(var path=Theme.Rounded(badge,S(11)))using(var brush=new SolidBrush(stateColor))g.FillPath(brush,path);
        TextRenderer.DrawText(g,state,Fonts.Small,badge,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g,cycles,Fonts.Small,new Rectangle(badge.Right+S(10),0,Width,badge.Height),Theme.SubText,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
        // The registers, four to a row.
        int columns=4,gap=S(6),top=badge.Bottom+S(10);
        int w=(Width-gap*(columns-1))/columns,h=S(44);
        Color tile=Theme.Dark?Color.FromArgb(50,50,50):Color.FromArgb(243,245,248);
        for(int n=0;n<cells.Length;n++) {
            var r=new Rectangle(n%columns*(w+gap),top+n/columns*(h+gap),w,h);
            using(var path=Theme.Rounded(r,S(5)))using(var brush=new SolidBrush(tile))g.FillPath(brush,path);
            TextRenderer.DrawText(g,cells[n].Name,Fonts.Small,new Point(r.X+S(8),r.Y+S(4)),Theme.SubText,TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g,cells[n].Value,Fonts.Mono,new Point(r.X+S(8),r.Y+S(20)),Theme.Text,TextFormatFlags.NoPadding);
        }
        // The flags as lamps.
        int fy=top+3*(h+gap)+S(2),fx=0;
        foreach(var (name,on) in new[]{(L.T("C（キャリー）","C (carry)"),carry),(L.T("Z（ゼロ）","Z (zero)"),zero)}) {
            var lamp=new Rectangle(fx,fy+S(3),S(12),S(12));
            using(var brush=new SolidBrush(on?Theme.Accent:Theme.Dark?Color.FromArgb(70,70,70):Color.FromArgb(215,215,215)))g.FillEllipse(brush,lamp);
            TextRenderer.DrawText(g,name,Fonts.Small,new Point(lamp.Right+S(6),fy+S(1)),on?Theme.Text:Theme.SubText,TextFormatFlags.NoPadding);
            fx+=S(130);
        }
    }
}
