// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// The table of bindings drawn by one control. As fifty-odd labels and drop-down
// boxes it took a second or more to make (the window stood still each time the
// pad changed); here a row is only painted, and the one drop-down list, made
// once, is put over a row's key only while that key is being chosen.
/// <summary>ボタンの割り当ての表（1つの部品で描く）。行は描くだけで、キーを選ぶときだけ共有のリストをその上に出す。</summary>
internal sealed class BindingGrid : Control
{
    /// <summary>キーの一覧の1項目。</summary>
    internal sealed record Choice(string Value,string Text){public override string ToString()=>Text;}
    string[] ids=[];
    readonly HashSet<string> lit=new();
    string? selected;
    /// <summary>行の値（一緒に押す SHIFT・CTRL と、キー。なければ ""）。</summary>
    internal Func<string,(string Modifier,string Key)>? Get;
    /// <summary>行の値を変えたとき（操作、SHIFT・CTRL、キー）。</summary>
    internal Action<string,string,string>? Set;
    /// <summary>行の名前。</summary>
    internal Func<string,string> NameOf=id=>id;
    /// <summary>キーボードで行を選んだとき（その行を見える所へ動かすため）。</summary>
    internal Action<string>? Selected;
    readonly ComboBox editor=new(){DropDownStyle=ComboBoxStyle.DropDownList,Visible=false,MaxDropDownItems=20};
    string? editing;
    readonly ContextMenuStrip modifiers=new();
    internal BindingGrid(IEnumerable<Choice> keys) {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        TabStop=true;Width=584;
        editor.Items.AddRange(keys.ToArray<object>());
        editor.BackColor=Theme.Input;editor.ForeColor=Theme.Text;
        Controls.Add(editor);
        editor.SelectionChangeCommitted+=(_,_)=>{
            if(editing is not null && editor.SelectedItem is Choice c && Get is not null)Set?.Invoke(editing,Get(editing).Modifier,c.Value);
            Invalidate();
        };
        editor.DropDownClosed+=(_,_)=>BeginInvoke(()=>{editor.Visible=false;editing=null;Focus();});
        FluentMenuRenderer.Use(modifiers);
    }
    float S=>DeviceDpi/96f;
    // Across, the columns share out the width the table is given (the window keeps its
    // width at any DPI); only the heights follow the DPI.
    // 横は表に与えられた幅を列で分ける（ウィンドウの幅は DPI で変わらない）。高さだけ DPI に合わせる。
    float X=>Width/584f;
    int Head=>(int)(26*S);
    int RowH=>(int)(34*S);
    // Columns: the control's name, SHIFT/CTRL, the key.
    Rectangle NameBox(int row)=>new(0,Head+row*RowH+(int)(3*S),(int)(246*X),RowH-(int)(6*S));
    Rectangle ModBox(int row)=>new((int)(254*X),Head+row*RowH+(int)(3*S),(int)(96*X),RowH-(int)(6*S));
    Rectangle KeyBox(int row)=>new((int)(360*X),Head+row*RowH+(int)(3*S),(int)(170*X),RowH-(int)(6*S));
    void Fit()=>Height=Head+ids.Length*RowH+(int)(4*S);
    protected override void OnDpiChangedAfterParent(EventArgs e){base.OnDpiChangedAfterParent(e);Fit();}

    /// <summary>行（操作）を入れ替える。</summary>
    internal void SetRows(string[] controls){ids=controls;lit.Clear();selected=null;editor.Visible=false;editing=null;Fit();Invalidate();}
    /// <summary>行の操作。</summary>
    internal string[] Ids=>ids;
    /// <summary>押されている操作の行を光らせる（変わったときだけ描き直す）。</summary>
    internal void SetLit(IEnumerable<string> now){var set=now.Where(ids.Contains).ToHashSet();if(set.SetEquals(lit))return;lit.Clear();lit.UnionWith(set);Invalidate();}
    internal bool IsLit(string id)=>lit.Contains(id);
    /// <summary>行を選ぶ（枠で示す）。</summary>
    internal void Choose(string id){if(!ids.Contains(id))return;selected=id;Invalidate();}
    /// <summary>行の場所（この部品の中の座標。なければ空）。</summary>
    internal Rectangle RowBounds(string id){int r=Array.IndexOf(ids,id);return r<0?Rectangle.Empty:new Rectangle(0,Head+r*RowH,Width,RowH);}

    protected override void OnPaint(PaintEventArgs e) {
        var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var left=TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
        string[] heads=[L.T("コントローラー","Controller"),L.T("同時に押す","With"),L.T("本体のキー","Machine key")];
        int[] x=[(int)(6*S),ModBox(0).X,KeyBox(0).X];
        for(int c=0;c<3;c++)TextRenderer.DrawText(g,heads[c],Font,new Rectangle(x[c],0,(int)(200*X),Head),Theme.SubText,left);
        using var border=new Pen(Theme.Border);using var accent=new Pen(Theme.Accent,2*S);
        using var input=new SolidBrush(Theme.Input);using var on=new SolidBrush(Theme.Accent);
        for(int r=0;r<ids.Length;r++) {
            if(e.ClipRectangle.Top>Head+(r+1)*RowH || e.ClipRectangle.Bottom<Head+r*RowH)continue;
            string id=ids[r];var (mod,key)=Get?.Invoke(id)??("","");
            var name=NameBox(r);bool isLit=lit.Contains(id);
            if(isLit){using var path=Theme.Rounded(name,4*S);g.FillPath(on,path);}
            if(id==selected){using var path=Theme.Rounded(RectangleF.Inflate(name,-1,-1),4*S);g.DrawPath(accent,path);}
            TextRenderer.DrawText(g,NameOf(id),Font,Rectangle.FromLTRB(name.X+(int)(6*S),name.Y,name.Right,name.Bottom),isLit?Color.White:Theme.Text,left);
            Box(g,ModBox(r),mod.Length==0?"—":mod+"+",border,input,left);
            if(editing!=id || !editor.Visible)Box(g,KeyBox(r),key.Length==0?L.T("（なし）","(none)"):key,border,input,left);
        }
    }
    void Box(Graphics g,Rectangle r,string text,Pen border,Brush fill,TextFormatFlags flags) {
        using(var path=Theme.Rounded(r,4*S)){g.FillPath(fill,path);g.DrawPath(border,path);}
        TextRenderer.DrawText(g,text,Font,Rectangle.FromLTRB(r.X+(int)(8*S),r.Y,r.Right-(int)(24*S),r.Bottom),Theme.Text,flags);
        TextRenderer.DrawText(g,Theme.Icons.ChevronDown.ToString(),Theme.SmallIconFont,Rectangle.FromLTRB(r.Right-(int)(24*S),r.Y,r.Right-(int)(4*S),r.Bottom),Theme.SubText,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.NoPadding);
    }
    int RowAt(Point p)=>p.Y<Head?-1:Math.Min(ids.Length,(p.Y-Head)/RowH) is int r && r<ids.Length?r:-1;
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);int r=RowAt(e.Location);
        Cursor=r>=0 && (ModBox(r).Contains(e.Location)||KeyBox(r).Contains(e.Location))?Cursors.Hand:Cursors.Default;
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);Focus();
        int r=RowAt(e.Location);if(r<0)return;
        selected=ids[r];Invalidate();
        if(ModBox(r).Contains(e.Location))OpenModifier(r);
        else if(KeyBox(r).Contains(e.Location))OpenKey(r);
    }
    // The keyboard: up and down choose a row, Enter or Space opens its key, M its SHIFT/CTRL.
    protected override bool IsInputKey(Keys k)=>k is Keys.Up or Keys.Down or Keys.Enter or Keys.Space || base.IsInputKey(k);
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);if(ids.Length==0)return;
        int r=Math.Max(0,Array.IndexOf(ids,selected??ids[0]));
        if(e.KeyCode==Keys.Down)r=Math.Min(ids.Length-1,r+1);else if(e.KeyCode==Keys.Up)r=Math.Max(0,r-1);
        // The row comes into sight first, so the list opens where it can be seen.
        else if(e.KeyCode is Keys.Enter or Keys.Space){selected=ids[r];Selected?.Invoke(selected);OpenKey(r);e.Handled=true;return;}
        else if(e.KeyCode==Keys.M){selected=ids[r];Selected?.Invoke(selected);OpenModifier(r);return;}
        else return;
        selected=ids[r];Invalidate();e.Handled=true;Selected?.Invoke(selected);
    }
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    void OpenModifier(int r) {
        string id=ids[r];var (mod,key)=Get?.Invoke(id)??("","");
        modifiers.Items.Clear();
        foreach(var (value,text) in new[]{("","—"),("SHIFT","SHIFT+"),("CTRL","CTRL+")}) {
            var item=new ToolStripMenuItem(text){Checked=value==mod};
            item.Click+=(_,_)=>{Set?.Invoke(id,value,key);Invalidate();};
            modifiers.Items.Add(item);
        }
        var box=ModBox(r);modifiers.Show(this,new Point(box.X,box.Bottom));
    }
    void OpenKey(int r) {
        string id=ids[r];var (_,key)=Get?.Invoke(id)??("","");
        editing=id;
        var box=KeyBox(r);editor.Bounds=new Rectangle(box.X,box.Y+(box.Height-editor.Height)/2,box.Width,editor.Height);
        editor.SelectedIndex=Math.Max(0,editor.Items.Cast<Choice>().ToList().FindIndex(c=>c.Value==key));
        editor.Visible=true;editor.Focus();editor.DroppedDown=true;
    }
    protected override void Dispose(bool disposing){if(disposing)modifiers.Dispose();base.Dispose(disposing);}
}
