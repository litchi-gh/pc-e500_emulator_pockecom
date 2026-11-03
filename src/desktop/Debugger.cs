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
    internal static void Update(TextBox box,string value) {
        if(box.Text==value)return;
        var top=SendMessage(box.Handle,EM_GETFIRSTVISIBLELINE,IntPtr.Zero,IntPtr.Zero);
        SendMessage(box.Handle,WM_SETREDRAW,IntPtr.Zero,IntPtr.Zero);
        box.Text=value;
        SendMessage(box.Handle,EM_LINESCROLL,IntPtr.Zero,top);
        SendMessage(box.Handle,WM_SETREDRAW,1,IntPtr.Zero);
        box.Invalidate();
    }
}

/// <summary>
/// デバッグウィンドウ：CPU のレジスタ・内部メモリ・I/O ポートの表示と、
/// 1MB のメモリの HEX 表示・書き換え、BIN の読込・保存、チェックサム。
/// </summary>
internal sealed class DebugDialog : Form
{
    // e500_reg ids, in the order of the SC62015 REGNAME enum.
    const int RegA=0,RegIL=1,RegBA=2,RegI=3,RegX=4,RegY=5,RegU=7,RegS=8,RegP=9,RegF=10;
    // Internal RAM addresses the CPU uses as pointers and as I/O ports.
    static readonly (string Name,int Address)[] Ports=[
        ("BP",0xec),("PX",0xed),("PY",0xee),("AMC",0xef),
        ("KOL",0xf0),("KOH",0xf1),("KI",0xf2),("EOL",0xf3),
        ("EOH",0xf4),("EIL",0xf5),("EIH",0xf6),("UCR",0xf7),
        ("USR",0xf8),("RXD",0xf9),("TXD",0xfa),("IMR",0xfb),
        ("ISR",0xfc),("SCR",0xfd),("ACM",0xfe),("SSR",0xff)];
    const int Columns=HexView.Columns;
    readonly Func<Core?> read;
    readonly TextBox cpu=new(){Multiline=true,ReadOnly=true,WordWrap=false,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Font=new Font("Consolas",10),BackColor=Color.White};
    /// <summary>メモリの HEX 表示。</summary>
    internal readonly HexView Memory;
    readonly TextBox addressBox=new(){Width=88,Font=new Font("Consolas",10),Text="B0000"};
    readonly CheckBox live=new(){Text=L.T("自動更新","Auto refresh"),Checked=true,AutoSize=true,Margin=new Padding(4,9,0,0)};
    readonly Label region=new(){Dock=DockStyle.Bottom,Height=24,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(8,0,0,0)};
    readonly Label note=new(){Dock=DockStyle.Bottom,Height=24,TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(8,0,0,0),ForeColor=Color.FromArgb(0,80,160)};
    readonly System.Windows.Forms.Timer timer=new(){Interval=250};
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="read">今のコアを返す関数（ROM の切り替えに追従するため）</param>
    internal DebugDialog(Func<Core?> read)
    {
        this.read=read;
        Memory=new HexView(read){Dock=DockStyle.Fill};
        Text=L.T("デバッグ / CPUレジスタ・メモリ","Debug / CPU registers and memory");
        StartPosition=FormStartPosition.CenterScreen;Font=new Font("Yu Gothic UI",10);
        // Both panes are fixed-width hex, so measure the font rather than guess:
        // the internal RAM rows are 51 columns and the memory rows 72.
        var probe=TextRenderer.MeasureText(new string('0',40),cpu.Font);
        int charWidth=Math.Max(6,probe.Width/40),scrollbar=SystemInformation.VerticalScrollBarWidth;
        int lineHeight=Math.Max(12,probe.Height);
        int leftWidth=charWidth*52+scrollbar+10;
        ClientSize=new Size(leftWidth+Memory.PreferredWidth+40,Math.Max(600,lineHeight*32));
        MinimumSize=new Size(charWidth*60,lineHeight*20);
        var main=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(8)};
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));main.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        Controls.Add(main);Controls.Add(note);Controls.Add(region);

        var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new Padding(0)};main.Controls.Add(bar,0,0);
        bar.Controls.Add(new Label{Text=L.T("アドレス","Address"),AutoSize=true,Margin=new Padding(0,9,4,0)});
        bar.Controls.Add(addressBox);
        void Jump(string text,Func<Core,int> target) {
            var b=new Button{Text=text,AutoSize=true,Margin=new Padding(3,0,0,0)};
            b.Click+=(_,_)=>{if(read() is {} core){Memory.Jump(target(core));Render();}};
            bar.Controls.Add(b);
        }
        Jump("PC",c=>(int)c.Register(RegP));Jump("S",c=>(int)c.Register(RegS));
        Jump("X",c=>(int)c.Register(RegX));Jump("Y",c=>(int)c.Register(RegY));Jump("U",c=>(int)c.Register(RegU));
        Jump(L.T("内蔵RAM","Internal RAM"),c=>0xc0000-c.InternalKB*1024);Jump(L.T("カード","Card"),_=>0x40000);Jump("ROM",_=>0xc0000);
        var refresh=new Button{Text=L.T("更新","Refresh"),AutoSize=true,Margin=new Padding(12,0,0,0)};
        refresh.Click+=(_,_)=>Render();bar.Controls.Add(refresh);bar.Controls.Add(live);
        var load=new Button{Text=L.T("BIN書込み","Load BIN"),AutoSize=true,Margin=new Padding(12,0,0,0)};
        var save=new Button{Text=L.T("BIN書出し","Save BIN"),AutoSize=true,Margin=new Padding(3,0,0,0)};
        load.Click+=(_,_)=>Transfer(false);save.Click+=(_,_)=>Transfer(true);
        bar.Controls.Add(load);bar.Controls.Add(save);
        var checksum=new Button{Text=L.T("チェックサム","Checksum"),AutoSize=true,Margin=new Padding(12,0,0,0)};
        checksum.Click+=(_,_)=>ShowChecksum();bar.Controls.Add(checksum);

        var panes=new SplitContainer{Dock=DockStyle.Fill,Margin=new Padding(0,6,0,0),FixedPanel=FixedPanel.Panel1,Panel1MinSize=140,Panel2MinSize=140};
        main.Controls.Add(panes,0,1);
        panes.Panel1.Controls.Add(cpu);panes.Panel2.Controls.Add(Memory);
        panes.Panel2.Padding=new Padding(8,0,0,0);
        Load+=(_,_)=>panes.SplitterDistance=Math.Min(leftWidth,Math.Max(panes.Panel1MinSize,panes.Width-panes.Panel2MinSize-panes.SplitterWidth));

        addressBox.KeyDown+=(_,e)=>{
            if(e.KeyCode!=Keys.Enter)return;
            e.Handled=e.SuppressKeyPress=true;
            if(TryParseAddress(addressBox.Text,out int value))Memory.Jump(value);
            Render();
        };
        Memory.Moved+=()=>{addressBox.Text=(Memory.Caret>=0?Memory.Caret:Memory.Address).ToString("X5");UpdateRegion();};
        Memory.Note+=text=>note.Text=text;
        timer.Tick+=(_,_)=>{if(live.Checked)Render();};
        // Open on the machine own RAM rather than on its mirror below it.
        Shown+=(_,_)=>{if(read() is {} core)Memory.Jump(0xc0000-core.InternalKB*1024);Render();timer.Start();};
        FormClosed+=(_,_)=>timer.Stop();
        note.Text=L.T("メモリ欄をクリックして16進を入力すると、その場で書き換えます（内蔵RAM・RAMカードのみ）。灰色は未定義、紺色はROMです。","Click a byte and type hex digits to edit it in place (internal RAM and RAM card only). Grey is unmapped, navy is ROM.");
    }
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
    // Sums a range starting at the caret; the dialog leaves the caret alone.
    void ShowChecksum() {
        using var dialog=new ChecksumDialog(read,Memory.Caret>=0?Memory.Caret:Memory.Address);
        dialog.ShowDialog(this);
    }
    /// <summary>表示している範囲とカーソルの位置が、どの領域（ROM・RAM など）かを表示する。</summary>
    void UpdateRegion() {
        var core=read();
        if(core is null){region.Text="";return;}
        int rows=Memory.VisibleRows(),address=Memory.Address;
        string caret=Memory.Caret>=0?L.T($"   カーソル {Memory.Caret:X5} ({MemoryMap.Name(core,Memory.Caret)})",$"   Caret {Memory.Caret:X5} ({MemoryMap.Name(core,Memory.Caret)})"):"";
        region.Text=$"{address:X5}-{address+rows*Columns-1:X5}   {MemoryMap.Name(core,address)}{caret}";
    }
    /// <summary>レジスタ・内部メモリ・メモリの表示を読み直す。</summary>
    internal void Render() {
        var core=read();
        if(core is null){LiveText.Update(cpu,L.T("ROMが読み込まれていません。","No ROM is loaded."));Memory.Reload();region.Text="";return;}
        int status=Native.e500_status(core.Handle);
        uint ba=core.Register(RegBA),i=core.Register(RegI),f=core.Register(RegF);
        byte[] imem=core.InternalMemory();
        var text=new StringBuilder();
        text.AppendLine(L.T("== CPUレジスタ ==","== CPU registers =="));
        text.AppendLine($"{L.T("状態    ","State   ")}  {((status&4)!=0?L.T("エラー停止","Fault"):(status&1)!=0?L.T("電源OFF","Power OFF"):(status&2)!=0?"HALT":L.T("実行中","Running"))}");
        text.AppendLine(L.T($"サイクル  {core.Cycles:N0}",$"Cycles    {core.Cycles:N0}"));
        text.AppendLine();
        text.AppendLine($"A  = {core.Register(RegA):X2}        B  = {ba>>8:X2}");
        text.AppendLine($"BA = {ba:X4}      I  = {i:X4}");
        text.AppendLine($"IL = {core.Register(RegIL):X2}        IH = {i>>8:X2}");
        text.AppendLine($"X  = {core.Register(RegX):X5}     Y  = {core.Register(RegY):X5}");
        text.AppendLine($"U  = {core.Register(RegU):X5}     S  = {core.Register(RegS):X5}");
        text.AppendLine($"PC = {core.Register(RegP):X5}     F  = {f:X2} (C={f&1} Z={(f>>1)&1})");
        text.AppendLine();
        text.AppendLine(L.T("== 内部RAMのポインタ・I/Oレジスタ ==","== Internal RAM pointers and I/O registers =="));
        for(int p=0;p<Ports.Length;p+=4) {
            var line=new StringBuilder();
            for(int q=p;q<Math.Min(p+4,Ports.Length);q++)line.Append($"{Ports[q].Name,-3} {Ports[q].Address:X2}={imem[Ports[q].Address]:X2}  ");
            text.AppendLine(line.ToString().TrimEnd());
        }
        text.AppendLine();
        text.AppendLine(L.T("== 内部RAM 256バイト (SC62015) ==","== Internal RAM, 256 bytes (SC62015) =="));
        for(int row=0;row<256;row+=Columns) {
            var line=new StringBuilder($"{row:X2}  ");
            for(int c=0;c<Columns;c++)line.Append($"{imem[row+c]:X2} ");
            text.AppendLine(line.ToString().TrimEnd());
        }
        LiveText.Update(cpu,text.ToString());
        Memory.Reload();UpdateRegion();
    }
    // Test hooks: the caret and a typed hex digit, as the mouse and keyboard deliver them.
    internal void TestType(Keys key)=>Memory.TestKey(key);
}
