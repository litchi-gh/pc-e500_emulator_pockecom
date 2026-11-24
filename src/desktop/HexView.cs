// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>メモリの領域の種類（色分けと、書き換えできるかの判断に使う）。</summary>
internal enum MemoryKind {Unmapped,Ram,Rom}

// What the bus has at an address, as the debugger colours and names it.
/// <summary>アドレスがどの領域か（ROM・内蔵 RAM・カード・未使用）を答える。</summary>
internal static class MemoryMap
{
    /// <summary>アドレスの領域の種類。</summary>
    internal static MemoryKind Kind(Core core,int a) {
        if(a>=0xc0000)return MemoryKind.Rom;
        if(a>=0x80000)return MemoryKind.Ram;
        if(a>=0x40000)return core.CardKB>0?MemoryKind.Ram:MemoryKind.Unmapped;
        if(a>=0x20000)return core.IsE650Family?MemoryKind.Rom:MemoryKind.Unmapped;
        return MemoryKind.Unmapped;
    }
    /// <summary>アドレスの領域の名前（表示用）。</summary>
    internal static string Name(Core core,int a) {
        if(a>=0xc0000)return L.T("内蔵ROM (CE2)","Internal ROM (CE2)");
        if(a>=0xc0000-core.InternalKB*1024)return L.T($"内蔵RAM {core.InternalKB}KB",$"Internal RAM {core.InternalKB}KB");
        if(a>=0x80000)return L.T($"内蔵RAM {core.InternalKB}KBのミラー",$"Mirror of internal RAM {core.InternalKB}KB");
        if(a>=0x40000)return core.CardKB>0?L.T($"RAMカード {core.CardKB}KB とそのミラー",$"RAM card {core.CardKB}KB and its mirrors"):L.T("カードスロット (未装着)","Card slot (empty)");
        if(a>=0x20000)return core.IsE650Family?L.T("拡張ROM (CE3)","Extension ROM (CE3)"):L.T("CE3 (未使用)","CE3 (unused)");
        if(a>=0x10000)return L.T("CE6 (未使用)","CE6 (unused)");
        if(a>=0x2000&&a<0x4000)return L.T("LCDコントローラの窓 (表示に影響しない生の値)","LCD controller window (raw bus, display unaffected)");
        return L.T("内部エリア","Internal area");
    }
}

// The memory pane of the debugger: a hex dump of the whole 1MB address space
// with a scrollbar, colours by region, and a caret that edits bytes in place.
// A row is 16 bytes (or 8), in groups of eight each followed by its checksum,
// the low byte of the eight bytes' sum, as machine-code listings print them:
//   XX XX XX XX XX XX XX XX : XX  - XX XX XX XX XX XX XX XX : XX
// 1行は 16 バイト（または 8）。8 バイトごとに、その和の下位 8 ビット（機械語のリストのチェックサム）を付ける。
// Owner-drawn rather than a TextBox so that each byte can carry its own colour
// and a periodic refresh repaints without scrolling or flicker.
/// <summary>
/// デバッガのメモリ表示：1MB 全体の HEX ダンプ。領域ごとに色を分け、
/// カーソルの位置のバイトを16進のキー入力でその場で書き換えられる（RAM のみ）。
/// </summary>
internal sealed class HexView : Control
{
    /// <summary>1行のバイト数（16 か 8）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int Columns {get=>columns;set{if(value is not (8 or 16) || value==columns)return;columns=value;scroll.Maximum=TotalRows-1;int keep=cursor>=0?cursor:address;Goto(keep);Invalidate();}}
    int columns=16;
    int TotalRows=>0x100000/columns;
    /// <summary>8 バイトの組の幅（文字）："XX ×8 : XX" の 28 文字と、次の組の前の "  - " の 4 文字。</summary>
    const int HexStart=7,GroupChars=32,Group=8;
    int LineChars=>HexStart+columns/Group*GroupChars-4;
    /// <summary>i バイト目の HEX の桁の位置（文字）。</summary>
    static int ByteColumn(int i)=>HexStart+i/Group*GroupChars+i%Group*3;
    /// <summary>8 バイトのチェックサム（和の下位 8 ビット）。</summary>
    internal static int GroupSum(ReadOnlySpan<byte> bytes){int sum=0;foreach(byte v in bytes)sum+=v;return sum&0xff;}
    static Color SumColor=>Theme.NoteText;
    // Light or dark with the theme.
    static Color RomColor=>Theme.Dark?Color.FromArgb(130,170,255):Color.FromArgb(0,0,140);
    static Color UnmappedColor=>Theme.Dark?Color.FromArgb(110,110,110):Color.FromArgb(160,160,160);
    static Color AddressColor=>Theme.SubText;
    readonly Func<Core?> read;
    readonly VScrollBar scroll=new(){Dock=DockStyle.Right};
    readonly int charWidth,lineHeight;
    int address;
    int cursor=-1,nibble;
    byte[] data=[];
    MemoryKind[] kinds=[];
    Core? shown;
    // Raised with a one-line note for the status area: a refused write, a
    // successful edit, the region under the caret.
    /// <summary>知らせたいメッセージ（書き換えた値、書き換えられない理由など）。</summary>
    internal event Action<string>? Note;
    /// <summary>表示位置かカーソルが動いたときに知らせる。</summary>
    internal event Action? Moved;
    /// <summary>表示の先頭アドレス、カーソルのアドレス（なければ -1）、カーソルが上位・下位のどちらの桁か。</summary>
    internal int Address=>address;
    internal int Caret=>cursor;
    internal int Nibble=>nibble;
    internal int PreferredWidth=>charWidth*(LineChars+1)+SystemInformation.VerticalScrollBarWidth+8;
    internal int LineHeight=>lineHeight;
    internal HexView(Func<Core?> read)
    {
        this.read=read;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);
        Font=Fonts.Mono;BackColor=Theme.Input;ForeColor=Theme.Text;Cursor=Cursors.IBeam;TabStop=true;
        var probe=TextRenderer.MeasureText(new string('0',40),Font,Size.Empty,TextFormatFlags.NoPadding);
        charWidth=Math.Max(6,probe.Width/40);lineHeight=Math.Max(12,probe.Height);
        scroll.Minimum=0;scroll.Maximum=0x100000/16-1;scroll.SmallChange=1;scroll.LargeChange=1;
        scroll.Scroll+=(_,e)=>{if(e.NewValue!=address/Columns)Goto(e.NewValue*Columns);};
        Controls.Add(scroll);
        Resize+=(_,_)=>{Goto(address);};
    }
    /// <summary>表示できる行数。</summary>
    internal int VisibleRows()=>Math.Max(4,Math.Min(TotalRows,(ClientSize.Height-4)/lineHeight));
    // Scrolls the top row to (the row of) value and rereads the machine.
    /// <summary>value を含む行が先頭に来るように表示を動かし、読み直す。</summary>
    /// <param name="value">アドレス</param>
    internal void Goto(int value) {
        int rows=VisibleRows();
        address=Math.Clamp(value,0,0x100000-rows*Columns)&~(Columns-1);
        scroll.LargeChange=rows;
        int row=address/Columns;if(scroll.Value!=row)scroll.Value=row;
        Reload();Moved?.Invoke();
    }
    // Puts the caret on an address and scrolls it into view.
    /// <summary>カーソルを value に置き、見える位置まで動かす。</summary>
    /// <param name="value">アドレス</param>
    internal void Jump(int value) {
        cursor=Math.Clamp(value,0,0xfffff);nibble=0;pending=-1;
        Goto(cursor);Invalidate();
    }
    /// <summary>カーソルが見えていなければ、見える位置まで表示を動かす。</summary>
    void EnsureVisible() {
        if(cursor<0)return;
        int rows=VisibleRows();
        if(cursor<address)Goto(cursor);
        else if(cursor>=address+rows*Columns)Goto(cursor-(rows-1)*Columns);
    }
    /// <summary>表示している範囲のメモリを読み直す。</summary>
    (int,int,Core,int,int) kindsFor;
    internal void Reload() {
        var core=read();shown=core;
        int rows=VisibleRows();
        if(core is null){data=[];kinds=[];kindsFor=default;Invalidate();return;}
        int count=Math.Min(rows*Columns,0x100000-address);
        byte[] next=core.Dump(address,count);
        // The kinds follow the address and the RAM sizes only, and the view is
        // repainted only when something it shows has changed: every refresh made
        // a call per byte and some 1300 text draws for a page that stood still.
        // 領域の種類は表示位置と RAM の容量が変わったときだけ求め、表示が変わったときだけ描き直す。
        var at=(address,count,core,core.InternalKB,core.CardKB);
        if(at!=kindsFor) {
            var nextKinds=new MemoryKind[count];
            for(int i=0;i<count;i++)nextKinds[i]=MemoryMap.Kind(core,address+i);
            kinds=nextKinds;kindsFor=at;data=next;Invalidate();return;
        }
        if(next.AsSpan().SequenceEqual(data))return;
        data=next;Invalidate();
    }
    protected override bool IsInputKey(Keys keyData) {
        switch(keyData&Keys.KeyCode) {
        case Keys.Left:case Keys.Right:case Keys.Up:case Keys.Down:case Keys.PageUp:case Keys.PageDown:case Keys.Home:case Keys.End:return true;
        }
        return base.IsInputKey(keyData);
    }
    /// <summary>キー操作：矢印・PageUp/Down・Home/End でカーソルを動かし、0〜9・A〜F で書き換える。</summary>
    protected override void OnKeyDown(KeyEventArgs e) {
        int rows=VisibleRows();
        switch(e.KeyCode) {
        case Keys.Left:MoveCursor(-1);break;
        case Keys.Right:MoveCursor(1);break;
        case Keys.Up:MoveCursor(-Columns);break;
        case Keys.Down:MoveCursor(Columns);break;
        case Keys.PageUp:if(cursor>=0)MoveCursor(-rows*Columns);else Goto(address-rows*Columns);break;
        case Keys.PageDown:if(cursor>=0)MoveCursor(rows*Columns);else Goto(address+rows*Columns);break;
        case Keys.Home:if(e.Control)Jump(0);else if(cursor>=0)MoveCursor(-(cursor%Columns));break;
        case Keys.End:if(e.Control)Jump(0xfffff);else if(cursor>=0)MoveCursor(Columns-1-cursor%Columns);break;
        default:
            // A digit with Ctrl, Alt or Shift is a shortcut or a symbol, not a nibble:
            // Ctrl+C wrote C into the memory.
            // Ctrl・Alt・Shift と一緒の数字は書き換えに使わない（Ctrl+C で C を書いていた）。
            int digit=e.Control||e.Alt||e.Shift?-1:HexDigit(e.KeyCode);
            if(digit<0)return;
            Edit(digit);break;
        }
        e.Handled=e.SuppressKeyPress=true;
        base.OnKeyDown(e);
    }
    /// <summary>キーを16進の数字にする（数字でなければ -1）。</summary>
    static int HexDigit(Keys key) {
        if(key>=Keys.D0&&key<=Keys.D9)return key-Keys.D0;
        if(key>=Keys.NumPad0&&key<=Keys.NumPad9)return key-Keys.NumPad0;
        if(key>=Keys.A&&key<=Keys.F)return key-Keys.A+10;
        return -1;
    }
    /// <summary>カーソルを delta バイト動かす。</summary>
    void MoveCursor(int delta) {
        if(cursor<0)cursor=address;
        cursor=Math.Clamp(cursor+delta,0,0xfffff);nibble=0;pending=-1;
        EnsureVisible();Reload();Invalidate();Moved?.Invoke();
    }
    // Types one hex digit into the byte under the caret. RAM only: the machine
    // refuses the rest, and the note says why nothing changed. The high digit
    // waits here for the low one, and the byte is written once, whole: written
    // straight away, a running program read the half-typed value (C0 for C3).
    // 上の桁は下の桁がそろうまでここに持ち、バイトは1回で書く（すぐ書くと、動いているプログラムが途中の値を読んだ）。
    /// <summary>入力済みで、まだ書いていない上の桁（なければ -1）。</summary>
    int pending=-1;
    internal int Pending=>pending;
    /// <summary>カーソルの位置のバイトの、今の桁を digit に書き換え、次の桁へ進む。</summary>
    /// <param name="digit">0〜15</param>
    void Edit(int digit) {
        var core=read();
        if(core is null||cursor<0){Note?.Invoke(L.T("書き換える位置をクリックしてください。","Click the byte to edit."));return;}
        if(MemoryMap.Kind(core,cursor)!=MemoryKind.Ram){Note?.Invoke(L.T($"{cursor:X5} は {MemoryMap.Name(core,cursor)} のため書き換えできません。内蔵RAM・RAMカードのみ書き換えられます。",$"{cursor:X5} is {MemoryMap.Name(core,cursor)} and cannot be edited. Only internal RAM and the RAM card are writable."));return;}
        byte old=core.Dump(cursor,1)[0];
        if(nibble==0) {
            pending=digit;nibble=1;
            Note?.Invoke(L.T($"{cursor:X5}: {old:X2} → {digit:X}_（下の桁を入力してください）",$"{cursor:X5}: {old:X2} → {digit:X}_ (type the low digit)"));
            Invalidate();Moved?.Invoke();return;
        }
        byte value=pending>=0?(byte)((pending<<4)|digit):(byte)((old&0xf0)|digit);
        pending=-1;
        try {core.Write(cursor,[value]);}
        catch(Exception ex){nibble=0;Note?.Invoke(ex.Message);return;}
        Note?.Invoke(L.T($"{cursor:X5}: {old:X2} → {value:X2}",$"{cursor:X5}: {old:X2} → {value:X2}"));
        nibble=0;if(cursor<0xfffff)cursor++;
        EnsureVisible();Reload();Invalidate();Moved?.Invoke();
    }
    /// <summary>クリックした位置のバイト（と桁）にカーソルを置く。</summary>
    protected override void OnMouseDown(MouseEventArgs e) {
        Focus();
        if(e.Button!=MouseButtons.Left||data.Length==0){base.OnMouseDown(e);return;}
        int row=(e.Y-2)/lineHeight,column=(e.X-4)/charWidth;
        if(row<0||column<0){base.OnMouseDown(e);return;}
        int index=-1,part=0;
        if(column>=HexStart&&column<LineChars){int c=column-HexStart,group=c/GroupChars,within=c%GroupChars;if(within<Group*3-1 && within%3<2){index=group*Group+within/3;part=within%3;}}
        int offset=row*Columns+index;
        if(index>=0&&offset<data.Length){cursor=address+offset;nibble=part;pending=-1;Invalidate();Moved?.Invoke();}
        base.OnMouseDown(e);
    }
    // Three rows a notch (120); a touchpad's finer steps add up to it, and turning back drops the remainder.
    // 1ノッチ（120）で3行。タッチパッドの細かい量は持ち越し、向きが変われば捨てる。
    int wheel;
    protected override void OnMouseWheel(MouseEventArgs e) {
        if(wheel!=0 && Math.Sign(wheel)!=Math.Sign(e.Delta))wheel=0;
        wheel+=e.Delta;int rows=wheel/40;wheel-=rows*40;
        if(rows!=0)Goto(address-rows*Columns);
        base.OnMouseWheel(e);
    }
    internal void TestWheel(int delta)=>OnMouseWheel(new MouseEventArgs(MouseButtons.None,0,0,0,delta));
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    /// <summary>アドレス・HEX（領域の色）と、8 バイトごとのチェックサムを描く。</summary>
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(BackColor);
        if(shown is null||data.Length==0) {
            TextRenderer.DrawText(g,L.T("ROMが読み込まれていません。","No ROM is loaded."),Font,new Point(4,2),Color.Gray,TextFormatFlags.NoPadding);return;
        }
        const TextFormatFlags flags=TextFormatFlags.NoPadding|TextFormatFlags.SingleLine;
        int rows=(data.Length+Columns-1)/Columns;
        int X(int column)=>4+column*charWidth;
        for(int row=0;row<rows;row++) {
            int y=2+row*lineHeight;
            if(y>ClientSize.Height)break;
            int rowAddress=address+row*Columns;
            TextRenderer.DrawText(g,rowAddress.ToString("X5"),Font,new Point(4,y),AddressColor,flags);
            for(int c=0;c<Columns;c++) {
                int i=row*Columns+c;if(i>=data.Length)break;
                int a=rowAddress+c;
                Color color=kinds[i] switch{MemoryKind.Ram=>ForeColor,MemoryKind.Rom=>RomColor,_=>UnmappedColor};
                int hx=X(ByteColumn(c));
                if(a==cursor) {
                    Color back=Theme.Dark?(Focused?Color.FromArgb(38,79,120):Color.FromArgb(60,60,60)):(Focused?Color.FromArgb(200,222,255):Color.FromArgb(225,225,225));
                    using var brush=new SolidBrush(back);
                    g.FillRectangle(brush,hx,y,charWidth*2,lineHeight);
                    if(Focused) {
                        using var pen=new Pen(Theme.Dark?Color.FromArgb(120,180,255):Color.FromArgb(0,90,200),2);
                        int ux=hx+nibble*charWidth;g.DrawLine(pen,ux,y+lineHeight-1,ux+charWidth,y+lineHeight-1);
                    }
                }
                // The high digit typed and not yet written shows in its place.
                string hex=a==cursor && pending>=0?$"{pending:X}{data[i]&15:X}":data[i].ToString("X2");
                TextRenderer.DrawText(g,hex,Font,new Point(hx,y),color,flags);
            }
            // Each group of eight: ": sum", and "-" before the next group.
            for(int group=0;group<Columns/Group;group++) {
                int first=row*Columns+group*Group;if(first+Group>data.Length)break;
                int at=HexStart+group*GroupChars;
                TextRenderer.DrawText(g,":",Font,new Point(X(at+24),y),AddressColor,flags);
                TextRenderer.DrawText(g,GroupSum(data.AsSpan(first,Group)).ToString("X2"),Font,new Point(X(at+26),y),SumColor,flags);
                if(group+1<Columns/Group)TextRenderer.DrawText(g,"-",Font,new Point(X(at+30),y),AddressColor,flags);
            }
        }
    }
    /// <summary>テスト用：表示の row 行目を文字にしたもの（描いているのと同じ並び）。</summary>
    internal string TestRowText(int row) {
        var text=new System.Text.StringBuilder($"{address+row*Columns:X5}  ");
        for(int group=0;group<Columns/Group;group++) {
            var bytes=data.AsSpan(row*Columns+group*Group,Group);
            if(group>0)text.Append("  - ");
            text.Append(string.Join(" ",bytes.ToArray().Select(v=>v.ToString("X2")))).Append(" : ").Append(GroupSum(bytes).ToString("X2"));
        }
        return text.ToString();
    }
    internal void TestKey(Keys key)=>OnKeyDown(new KeyEventArgs(key));
    internal int TestScrollValue=>scroll.Value;
    internal int TestScrollMax=>scroll.Maximum-scroll.LargeChange+1;
    protected override void Dispose(bool disposing){if(disposing)scroll.Dispose();base.Dispose(disposing);}
}
