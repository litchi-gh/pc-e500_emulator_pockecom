// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

internal enum MemoryKind {Unmapped,Ram,Rom}

// What the bus has at an address, as the debugger colours and names it.
internal static class MemoryMap
{
    internal static MemoryKind Kind(Core core,int a) {
        if(a>=0xc0000)return MemoryKind.Rom;
        if(a>=0x80000)return MemoryKind.Ram;
        if(a>=0x40000)return core.CardKB>0?MemoryKind.Ram:MemoryKind.Unmapped;
        if(a>=0x20000)return core.IsE650Family?MemoryKind.Rom:MemoryKind.Unmapped;
        return MemoryKind.Unmapped;
    }
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
// Owner-drawn rather than a TextBox so that each byte can carry its own colour
// and a periodic refresh repaints without scrolling or flicker.
internal sealed class HexView : Control
{
    internal const int Columns=16;
    const int TotalRows=0x100000/Columns;
    const int HexStart=7,AsciiStart=HexStart+Columns*3+1,LineChars=AsciiStart+Columns;
    static readonly Color RomColor=Color.FromArgb(0,0,140),UnmappedColor=Color.FromArgb(160,160,160),AddressColor=Color.FromArgb(90,90,90);
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
    internal event Action<string>? Note;
    internal event Action? Moved;
    internal int Address=>address;
    internal int Caret=>cursor;
    internal int Nibble=>nibble;
    internal int PreferredWidth=>charWidth*(LineChars+1)+SystemInformation.VerticalScrollBarWidth+8;
    internal int LineHeight=>lineHeight;
    internal HexView(Func<Core?> read)
    {
        this.read=read;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);
        Font=new Font("Consolas",10);BackColor=Color.White;Cursor=Cursors.IBeam;TabStop=true;
        var probe=TextRenderer.MeasureText(new string('0',40),Font,Size.Empty,TextFormatFlags.NoPadding);
        charWidth=Math.Max(6,probe.Width/40);lineHeight=Math.Max(12,probe.Height);
        scroll.Minimum=0;scroll.Maximum=TotalRows-1;scroll.SmallChange=1;scroll.LargeChange=1;
        scroll.Scroll+=(_,e)=>{if(e.NewValue!=address/Columns)Goto(e.NewValue*Columns);};
        Controls.Add(scroll);
        Resize+=(_,_)=>{Goto(address);};
    }
    internal int VisibleRows()=>Math.Max(4,Math.Min(TotalRows,(ClientSize.Height-4)/lineHeight));
    // Scrolls the top row to (the row of) value and rereads the machine.
    internal void Goto(int value) {
        int rows=VisibleRows();
        address=Math.Clamp(value,0,0x100000-rows*Columns)&~(Columns-1);
        scroll.LargeChange=rows;
        int row=address/Columns;if(scroll.Value!=row)scroll.Value=row;
        Reload();Moved?.Invoke();
    }
    // Puts the caret on an address and scrolls it into view.
    internal void Jump(int value) {
        cursor=Math.Clamp(value,0,0xfffff);nibble=0;
        Goto(cursor);
    }
    void EnsureVisible() {
        if(cursor<0)return;
        int rows=VisibleRows();
        if(cursor<address)Goto(cursor);
        else if(cursor>=address+rows*Columns)Goto(cursor-(rows-1)*Columns);
    }
    internal void Reload() {
        var core=read();shown=core;
        int rows=VisibleRows();
        if(core is null){data=[];kinds=[];Invalidate();return;}
        int count=Math.Min(rows*Columns,0x100000-address);
        byte[] next=core.Dump(address,count);
        var nextKinds=new MemoryKind[count];
        for(int i=0;i<count;i++)nextKinds[i]=MemoryMap.Kind(core,address+i);
        data=next;kinds=nextKinds;Invalidate();
    }
    protected override bool IsInputKey(Keys keyData) {
        switch(keyData&Keys.KeyCode) {
        case Keys.Left:case Keys.Right:case Keys.Up:case Keys.Down:case Keys.PageUp:case Keys.PageDown:case Keys.Home:case Keys.End:return true;
        }
        return base.IsInputKey(keyData);
    }
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
            int digit=HexDigit(e.KeyCode);
            if(digit<0)return;
            Edit(digit);break;
        }
        e.Handled=e.SuppressKeyPress=true;
        base.OnKeyDown(e);
    }
    static int HexDigit(Keys key) {
        if(key>=Keys.D0&&key<=Keys.D9)return key-Keys.D0;
        if(key>=Keys.NumPad0&&key<=Keys.NumPad9)return key-Keys.NumPad0;
        if(key>=Keys.A&&key<=Keys.F)return key-Keys.A+10;
        return -1;
    }
    void MoveCursor(int delta) {
        if(cursor<0)cursor=address;
        cursor=Math.Clamp(cursor+delta,0,0xfffff);nibble=0;
        EnsureVisible();Reload();Moved?.Invoke();
    }
    // Types one hex digit into the byte under the caret. RAM only: the machine
    // refuses the rest, and the note says why nothing changed.
    void Edit(int digit) {
        var core=read();
        if(core is null||cursor<0){Note?.Invoke(L.T("書き換える位置をクリックしてください。","Click the byte to edit."));return;}
        if(MemoryMap.Kind(core,cursor)!=MemoryKind.Ram){Note?.Invoke(L.T($"{cursor:X5} は {MemoryMap.Name(core,cursor)} のため書き換えできません。内蔵RAM・RAMカードのみ書き換えられます。",$"{cursor:X5} is {MemoryMap.Name(core,cursor)} and cannot be edited. Only internal RAM and the RAM card are writable."));return;}
        byte old=core.Dump(cursor,1)[0];
        byte value=nibble==0?(byte)((digit<<4)|(old&0x0f)):(byte)((old&0xf0)|digit);
        try {core.Write(cursor,[value]);}
        catch(Exception ex){Note?.Invoke(ex.Message);return;}
        Note?.Invoke(L.T($"{cursor:X5}: {old:X2} → {value:X2}",$"{cursor:X5}: {old:X2} → {value:X2}"));
        if(nibble==0)nibble=1;
        else {nibble=0;if(cursor<0xfffff)cursor++;}
        EnsureVisible();Reload();Moved?.Invoke();
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        Focus();
        if(e.Button!=MouseButtons.Left||data.Length==0){base.OnMouseDown(e);return;}
        int row=(e.Y-2)/lineHeight,column=(e.X-4)/charWidth;
        if(row<0||column<0){base.OnMouseDown(e);return;}
        int index=-1,part=0;
        if(column>=HexStart&&column<HexStart+Columns*3){int c=column-HexStart;if(c%3<2){index=c/3;part=c%3;}}
        else if(column>=AsciiStart&&column<AsciiStart+Columns)index=column-AsciiStart;
        int offset=row*Columns+index;
        if(index>=0&&offset<data.Length){cursor=address+offset;nibble=part;Invalidate();Moved?.Invoke();}
        base.OnMouseDown(e);
    }
    protected override void OnMouseWheel(MouseEventArgs e) {
        Goto(address-e.Delta/120*Columns*3);
        base.OnMouseWheel(e);
    }
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(BackColor);
        if(shown is null||data.Length==0) {
            TextRenderer.DrawText(g,L.T("ROMが読み込まれていません。","No ROM is loaded."),Font,new Point(4,2),Color.Gray,TextFormatFlags.NoPadding);return;
        }
        const TextFormatFlags flags=TextFormatFlags.NoPadding|TextFormatFlags.SingleLine;
        int rows=(data.Length+Columns-1)/Columns;
        for(int row=0;row<rows;row++) {
            int y=2+row*lineHeight;
            if(y>ClientSize.Height)break;
            int rowAddress=address+row*Columns;
            TextRenderer.DrawText(g,rowAddress.ToString("X5"),Font,new Point(4,y),AddressColor,flags);
            for(int c=0;c<Columns;c++) {
                int i=row*Columns+c;if(i>=data.Length)break;
                int a=rowAddress+c;
                Color color=kinds[i] switch{MemoryKind.Ram=>ForeColor,MemoryKind.Rom=>RomColor,_=>UnmappedColor};
                int hx=4+(HexStart+c*3)*charWidth,ax=4+(AsciiStart+c)*charWidth;
                if(a==cursor) {
                    Color back=Focused?Color.FromArgb(200,222,255):Color.FromArgb(225,225,225);
                    using var brush=new SolidBrush(back);
                    g.FillRectangle(brush,hx,y,charWidth*2,lineHeight);g.FillRectangle(brush,ax,y,charWidth,lineHeight);
                    if(Focused) {
                        using var pen=new Pen(Color.FromArgb(0,90,200),2);
                        int ux=hx+nibble*charWidth;g.DrawLine(pen,ux,y+lineHeight-1,ux+charWidth,y+lineHeight-1);
                    }
                }
                TextRenderer.DrawText(g,data[i].ToString("X2"),Font,new Point(hx,y),color,flags);
                byte v=data[i];string ch=v>=0x20&&v<0x7f?((char)v).ToString():".";
                TextRenderer.DrawText(g,ch,Font,new Point(ax,y),color,flags);
            }
        }
    }
    internal void TestKey(Keys key)=>OnKeyDown(new KeyEventArgs(key));
    internal int TestScrollValue=>scroll.Value;
    internal int TestScrollMax=>scroll.Maximum-scroll.LargeChange+1;
    protected override void Dispose(bool disposing){if(disposing)scroll.Dispose();base.Dispose(disposing);}
}
