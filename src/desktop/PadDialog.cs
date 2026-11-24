// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// Edits a copy of the settings; Apply hands the copy over. A button pressed on
// the controller lights its row and moves to it, which is how a DirectInput
// pad's "Button 7" is found without a chart.
/// <summary>「コントローラー」ウィンドウ：使うコントローラー、プリセット（追加・名前変更・削除）、ボタンごとの本体のキー。</summary>
internal sealed class PadDialog : ThemedForm
{
    /// <summary>「適用」で呼ぶ処理（false を返すとウィンドウを閉じない）。</summary>
    internal Func<PadConfig,bool>? Apply;
    readonly PadConfig config;
    List<PadDevice> devices=new();
    PadDevice? shown;
    readonly CheckBox enabled=new(){Text=L.T("コントローラーを使う","Use a controller"),AutoSize=true};
    readonly ComboBox device=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=380};
    readonly ComboBox preset=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=250};
    readonly NoteLabel deviceNote=new("",560);
    readonly PadView view=new(){Size=new Size(584,262),Margin=new Padding(0,6,0,2)};
    readonly Label pressedLine=new(){AutoSize=true,MinimumSize=new Size(584,26),MaximumSize=new Size(584,0),TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0,0,0,2)};
    readonly PadAxes padAxes=new();
    // Looking for pads takes a while (winmm reloads its list), so it runs off the
    // window's thread; Windows sends several device changes for one plug, and the
    // search starts once they have stopped for half a second.
    /// <summary>裏で動いているコントローラーの検索（なければ null）と、終わったらもう一度探すか。</summary>
    Task<List<PadDevice>>? scanning;
    bool scanAgain,testOnly;
    readonly System.Windows.Forms.Timer settle=new(){Interval=500};
    /// <summary>ボタンの位置合わせ：覚えている途中の位置（していなければ null）と、尋ねている位置の番号。</summary>
    Dictionary<string,int>? learning;
    int learnStep;
    readonly FluentButton learn=new(){Text=L.T("ボタンの位置合わせ...","Match button places..."),AutoSize=true,Margin=new Padding(0,0,6,0)};
    readonly FluentButton skip=new(){Text=L.T("とばす","Skip"),AutoSize=true,Margin=new Padding(0,0,6,0),Visible=false};
    readonly FluentButton stopLearn=new(){Text=L.T("やめる","Stop"),AutoSize=true,Margin=new Padding(0,0,6,0),Visible=false};
    /// <summary>ボタンの割り当ての表。</summary>
    readonly BindingGrid grid=new(new[]{new BindingGrid.Choice("",L.T("（なし）","(none)"))}.Concat(KeyNames.Select(k=>new BindingGrid.Choice(k,k)))){Margin=new Padding(0,4,0,0)};
    readonly StillPanel panel=new(){Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16,16,16,8)};
    readonly HashSet<string> pressed=new(),before=new();
    readonly System.Windows.Forms.Timer live=new(){Interval=33};
    bool filling;
    /// <summary>キーの一覧（本体の行列の順）。</summary>
    static readonly string[] KeyNames=Keyboard.Matrix.SelectMany(r=>r).Where(k=>k.Length>0).ToArray();
    sealed record Item(string Value,string Text){public override string ToString()=>Text;}

    internal PadDialog(PadConfig current)
    {
        config=current.Copy();
        Text=L.T("コントローラー","Controller");ClientSize=new(660,860);MinimumSize=new Size(560,600);StartPosition=FormStartPosition.CenterScreen;
        Font=Fonts.Ui;
        FlowLayoutPanel Card(string title){var card=new CardPanel{Title=title,Margin=new Padding(0,0,0,10)};panel.Controls.Add(card);return card.Stack(584);}
        const int Label=130;
        // The pad and what is pressed stay in sight above the list, which scrolls.
        // コントローラーの絵と押しているボタンは上に固定し、割り当ての一覧だけを動かす。
        var top=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16,16,16,0)};
        var topCard=new CardPanel{Title=L.T("コントローラーと動作の確認","Controller and test"),Margin=new Padding(0)};top.Controls.Add(topCard);
        var pad=topCard.Stack(584);
        device.Width=330;
        var rescan=new FluentButton{Text=L.T("再検索","Search again"),AutoSize=true};
        pad.Controls.Add(Row(L.T("使うもの","Use"),Label-40,device,rescan));
        view.BackColor=Theme.Card;pad.Controls.Add(view);
        pad.Controls.Add(pressedLine);
        var learnRow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,BackColor=Theme.Card,Margin=new Padding(0,0,0,2)};
        learnRow.Controls.AddRange([learn,skip,stopLearn]);pad.Controls.Add(learnRow);
        pad.Controls.Add(deviceNote);
        panel.Padding=new Padding(16,10,16,8);
        var presets=Card(L.T("プリセット","Presets"));
        presets.Controls.Add(Row(L.T("使うプリセット","Preset in use"),Label,preset));
        var add=new FluentButton{Text=L.T("追加...","Add..."),AutoSize=true};var rename=new FluentButton{Text=L.T("名前の変更...","Rename..."),AutoSize=true};var remove=new FluentButton{Text=L.T("削除","Delete"),AutoSize=true};
        var buttons=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(Label,0,0,4),BackColor=Theme.Card};
        foreach(var b in new[]{add,rename,remove}){b.Margin=new Padding(0,0,6,0);buttons.Controls.Add(b);}
        presets.Controls.Add(buttons);
        presets.Controls.Add(new NoteLabel(L.T("「追加」は今のプリセットを写して新しく作ります。割り当ての変更は、表示中のプリセットに入ります。","Add copies the preset shown into a new one. Changes below go into the preset shown."),560));
        var map=Card(L.T("ボタンの割り当て","Buttons"));
        map.Controls.Add(new NoteLabel(L.T("コントローラーのボタンを押すと、その行が光ります。押している間、本体のキーを押したままにします。","Press a button on the controller to light its row. The machine's key is held while the button is."),560));
        grid.BackColor=Theme.Card;grid.NameOf=GamePad.ControlName;
        grid.Get=id=>Split(config.Current.Map.GetValueOrDefault(id)??"");
        grid.Set=SetBinding;
        map.Controls.Add(grid);
        var footer=new FooterBar();
        var close=footer.Add(L.T("閉じる","Close"),(_,_)=>Close());
        var ok=footer.Add(L.T("適用","Apply"),(_,_)=>{EndLearn(true);config.Enabled=enabled.Checked;if(Apply is null||Apply(config.Copy()))Close();});
        enabled.Checked=config.Enabled;footer.AddLeft(enabled);
        Controls.Add(panel);Controls.Add(top);Controls.Add(footer);AcceptButton=ok;CancelButton=close;
        view.BindingOf=id=>config.Current.Map.GetValueOrDefault(id);
        view.ControlClicked+=id=>{ScrollTo(id);grid.Choose(id);grid.Focus();};
        grid.Selected=ScrollTo;

        rescan.Click+=(_,_)=>Rescan();
        settle.Tick+=(_,_)=>{settle.Stop();Rescan();};
        learn.Click+=(_,_)=>StartLearn();
        skip.Click+=(_,_)=>{if(learning is null)return;learnStep++;ShowLearn();};
        stopLearn.Click+=(_,_)=>EndLearn(false);
        device.SelectedIndexChanged+=(_,_)=>{if(filling)return;config.Device=device.SelectedItem is Item i?i.Value:"";ShowDevice();};
        preset.SelectedIndexChanged+=(_,_)=>{if(filling||preset.SelectedItem is not Item i)return;config.Active=i.Value;grid.Invalidate();};
        add.Click+=(_,_)=>{
            if(AskName(L.T("プリセットの追加","Add a preset"),config.Current.Name+L.T(" のコピー"," copy")) is not string name)return;
            config.Presets.Add(config.Current.Copy(name));config.Active=name;FillPresets();
        };
        rename.Click+=(_,_)=>{
            var p=config.Current;
            if(AskName(L.T("プリセットの名前の変更","Rename the preset"),p.Name,p.Name) is not string name)return;
            p.Name=name;config.Active=name;FillPresets();
        };
        remove.Click+=(_,_)=>{
            if(config.Presets.Count<=1){MessageBox.Show(this,L.T("最後のプリセットは削除できません。","The last preset cannot be deleted."),Text,MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            var p=config.Current;
            if(MessageBox.Show(this,L.T($"プリセット「{p.Name}」を削除しますか？",$"Delete the preset \"{p.Name}\"?"),Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            config.Presets.Remove(p);config.Active=config.Presets[0].Name;FillPresets();
        };
        live.Tick+=(_,_)=>Poll();
        Load+=(_,_)=>{
            // Never taller than the screen it opens on (a scaled-up display has fewer rows).
            var area=Screen.FromControl(this).WorkingArea;
            if(Height>area.Height){Height=area.Height;CenterToScreen();}
            FillPresets();Rescan();live.Start();
            LockWidth();
        };
        FormClosed+=(_,_)=>{live.Dispose();settle.Dispose();};
    }
    // A pad plugged in or out while the window is open is found at once.
    protected override void WndProc(ref Message m) {
        const int WM_DEVICECHANGE=0x219;
        base.WndProc(ref m);
        if(m.Msg==WM_DEVICECHANGE && IsHandleCreated){settle.Stop();settle.Start();}
    }
    /// <summary>コントローラーを裏で探し直す（終わったら一覧を作る）。</summary>
    void Rescan() {
        if(testOnly)return;
        if(scanning is not null){scanAgain=true;return;}
        if(devices.Count==0)deviceNote.Text=L.T("コントローラーを探しています...","Looking for controllers...");
        scanning=Task.Run(GamePad.Scan);
        scanning.ContinueWith(t=>{
            try {if(!IsDisposed && IsHandleCreated)BeginInvoke(()=>Found(t.IsCompletedSuccessfully?t.Result:[]));}
            catch(InvalidOperationException) {}
        },TaskScheduler.Default);
    }
    /// <summary>探し終えたコントローラーで一覧を作る。</summary>
    void Found(List<PadDevice> list) {
        scanning=null;
        if(IsDisposed || testOnly)return;
        if(scanAgain){scanAgain=false;Rescan();return;}
        devices=list;
        FillDevices();
    }
    /// <summary>「使うもの」の一覧を、見つかったコントローラーで作り直し、選んだものを出す。</summary>
    void FillDevices() {
        filling=true;
        device.Items.Clear();
        device.Items.Add(new Item("",L.T("自動（最初に見つかったもの）","Automatic (the first one found)")));
        foreach(var d in devices)device.Items.Add(new Item(d.Id,d.Name));
        if(config.Device.Length>0 && !devices.Any(d=>d.Id==config.Device))device.Items.Add(new Item(config.Device,L.T($"（つながっていない）{config.Device}",$"(not connected) {config.Device}")));
        device.SelectedIndex=Math.Max(0,device.Items.Cast<Item>().ToList().FindIndex(i=>i.Value==config.Device));
        filling=false;
        ShowDevice();
    }
    /// <summary>選んだコントローラーの説明と、そのボタンの行を出す。</summary>
    void ShowDevice() {
        var picked=GamePad.Pick(devices,config.Device);
        deviceNote.Text=picked is not null?L.T($"接続中: {picked.Name}",$"Connected: {picked.Name}")
            :devices.Count==0?L.T("コントローラーが見つかりません。つないでから「再検索」を押してください。絵と一覧は Xbox 系の並びです。","No controller found. Connect one and press Search again. The picture and list show the Xbox layout.")
            :L.T("選んだコントローラーはつながっていません。絵と一覧は Xbox 系の並びです。","The chosen controller is not connected. The picture and list show the Xbox layout.");
        bool numbered=picked?.Kind==PadKind.DirectInput;
        if(numbered)deviceNote.Text+=L.T("　番号の位置が実物と違うときは「ボタンの位置合わせ」で合わせられます。","  If the numbers sit elsewhere on the pad, match them with Match button places.");
        if(learning is not null && (shown?.Id!=picked?.Id || !numbered))EndLearn(false);
        learn.Visible=numbered && learning is null;
        view.SetDevice(picked,picked is not null,numbered?config.LayoutFor(picked):null);
        var controls=GamePad.Controls(picked);
        shown=picked;
        if(!controls.SequenceEqual(grid.Ids))grid.SetRows(controls);
    }
    /// <summary>割り当ての文字を、一緒に押す SHIFT・CTRL とキーに分ける。</summary>
    static (string Modifier,string Key) Split(string b)=>
        b.StartsWith("SHIFT+",StringComparison.Ordinal) && b.Length>6?("SHIFT",b[6..]):b.StartsWith("CTRL+",StringComparison.Ordinal) && b.Length>5?("CTRL",b[5..]):("",b);
    /// <summary>表示中のプリセットの、操作の割り当てを変える（キーが空なら外す）。</summary>
    void SetBinding(string id,string modifier,string key) {
        var map=config.Current.Map;
        if(key.Length==0)map.Remove(id);else map[id]=PadConfig.Binding(modifier,key);
        grid.Invalidate();
    }
    /// <summary>行が見える所まで、割り当ての一覧を動かす。</summary>
    void ScrollTo(string id) {
        var r=grid.RowBounds(id);if(r.IsEmpty || !grid.IsHandleCreated)return;
        int top=panel.PointToClient(grid.PointToScreen(r.Location)).Y;
        if(top>=0 && top+r.Height<=panel.ClientSize.Height)return;
        panel.AutoScrollPosition=new Point(0,Math.Max(0,-panel.AutoScrollPosition.Y+top-panel.ClientSize.Height/2));
    }
    void FillPresets() {
        filling=true;
        preset.Items.Clear();
        foreach(var p in config.Presets)preset.Items.Add(new Item(p.Name,p.Name));
        preset.SelectedIndex=Math.Max(0,config.Presets.IndexOf(config.Current));
        filling=false;
        grid.Invalidate();
    }
    /// <summary>コントローラーを読み、押されているボタンの行を光らせる。新しく押されたボタンの行へ移る。</summary>
    void Poll() {
        before.Clear();before.UnionWith(pressed);
        if(learning is not null && scanning is null && shown is not null && GamePad.Read(shown,pressed,padAxes)) {
            // Learning: the first numbered button newly pressed belongs to the place asked for.
            view.SetState(pressed,padAxes);
            if(pressed.Except(before).FirstOrDefault(c=>c.StartsWith("Button",StringComparison.Ordinal)) is string hit)Learn(int.Parse(hit[6..]));
            return;
        }
        if(shown is null || !GamePad.Read(shown,pressed,padAxes)) {
            pressed.Clear();padAxes.Clear();
            if(shown is not null){shown=null;Rescan();}
        }
        view.SetState(pressed,padAxes);
        Highlight(before);
    }
    void Highlight(HashSet<string> was) {
        if(learning is not null)return;
        // In words too: each control pressed and the key it holds.
        var order=GamePad.Controls(shown);
        var now=pressed.OrderBy(c=>{int i=Array.IndexOf(order,c);return i<0?int.MaxValue:i;})
            .Select(c=>$"{GamePad.ControlName(c)} → {(config.Current.Map.TryGetValue(c,out var b)?b:L.T("なし","none"))}").ToArray();
        string line=now.Length==0?L.T("押しているボタン: なし","Pressed: nothing"):L.T("押しているボタン: ","Pressed: ")+string.Join(L.T("、",", "),now);
        if(pressedLine.Text!=line){pressedLine.Text=line;pressedLine.ForeColor=now.Length==0?Theme.SubText:Theme.Accent;}
        grid.SetLit(pressed);
        // A button newly pressed: its row comes into sight and is chosen.
        if(pressed.Except(was).FirstOrDefault(grid.Ids.Contains) is string fresh){ScrollTo(fresh);grid.Choose(fresh);}
    }
    /// <summary>ボタンの位置合わせを始める（DirectInput のパッドだけ）。</summary>
    void StartLearn() {
        if(shown is not {Kind:PadKind.DirectInput})return;
        learning=new();learnStep=0;
        learn.Visible=false;skip.Visible=stopLearn.Visible=true;
        ShowLearn();
    }
    /// <summary>次に尋ねる位置を絵と文字で示す（全部尋ねたら終える）。</summary>
    void ShowLearn() {
        if(learning is null)return;
        if(learnStep>=PadView.Positions.Length){EndLearn(true);return;}
        string pos=PadView.Positions[learnStep];
        view.SetTarget(pos,learning);
        pressedLine.ForeColor=Color.FromArgb(200,100,0);
        pressedLine.Text=L.T($"位置合わせ {learnStep+1}/{PadView.Positions.Length}: 「{PadView.PositionName(pos)}」のボタンを押してください（ないときは「とばす」）",
            $"Matching {learnStep+1}/{PadView.Positions.Length}: press the button at the {PadView.PositionName(pos)} (or Skip)");
    }
    /// <summary>尋ねている位置に、押された番号のボタンを置く（ほかの位置にあれば外す）。</summary>
    void Learn(int button) {
        if(learning is null)return;
        foreach(var key in learning.Where(x=>x.Value==button).Select(x=>x.Key).ToList())learning.Remove(key);
        learning[PadView.Positions[learnStep]]=button;
        learnStep++;ShowLearn();
    }
    /// <summary>位置合わせを終える（keep なら覚えた位置をこのパッドのものにする）。</summary>
    void EndLearn(bool keep) {
        if(learning is null)return;
        if(keep && PadConfig.LayoutKey(shown) is string key)config.Layouts[key]=learning;
        learning=null;
        skip.Visible=stopLearn.Visible=false;learn.Visible=shown?.Kind==PadKind.DirectInput;
        view.SetDevice(shown,shown is not null,shown?.Kind==PadKind.DirectInput?config.LayoutFor(shown):null);
        pressedLine.Text="";Highlight(new HashSet<string>());
        if(keep)pressedLine.Text=L.T("位置合わせが終わりました（「適用」で保存します）。","Places matched (Apply keeps them).");
    }
    /// <summary>プリセットの名前を聞く（空・重なる名前は受け付けない）。</summary>
    /// <returns>名前（やめたら null）</returns>
    string? AskName(string title,string suggestion,string? own=null) {
        using var ask=new NameDialog(title,suggestion);
        while(ask.ShowDialog(this)==DialogResult.OK) {
            string name=ask.Value.Trim();
            if(name.Length==0)continue;
            if(name!=own && config.Presets.Any(p=>p.Name==name)){MessageBox.Show(this,L.T("同じ名前のプリセットがあります。","A preset of that name exists."),title,MessageBoxButtons.OK,MessageBoxIcon.Information);continue;}
            return name;
        }
        return null;
    }
    // For the tests: the rows a pad shows, and a press as if read from it.
    internal string[] TestRows=>grid.Ids;
    internal PadConfig TestConfig=>config;
    internal void TestPress(params string[] ids){var was=new HashSet<string>(pressed);pressed.Clear();pressed.UnionWith(ids);view.SetState(pressed,padAxes);Highlight(was);}
    internal void TestPress(PadAxes a,params string[] ids){a.CopyTo(padAxes);TestPress(ids);}
    internal PadView TestView=>view;
    internal string TestPressedLine=>pressedLine.Text;
    internal bool TestLit(string id)=>grid.IsLit(id);
    internal void TestSet(string id,string modifier,string key)=>SetBinding(id,modifier,key);
    internal void TestLearn(params int[] presses){StartLearn();foreach(int b in presses)if(b>0)Learn(b);else{learnStep++;ShowLearn();}}
    internal bool TestLearning=>learning is not null;
    internal void TestShow(PadDevice? d){live.Stop();testOnly=true;shown=null;grid.SetRows([]);devices=d is null?[]:[d];config.Device="";FillDevices();panel.AutoScrollPosition=Point.Empty;}
}

/// <summary>名前を1つ聞く小さなウィンドウ。</summary>
internal sealed class NameDialog : ThemedForm
{
    readonly TextBox box=new(){Width=320};
    internal string Value=>box.Text;
    internal NameDialog(string title,string value)
    {
        Text=title;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterParent;Font=Fonts.Ui;ClientSize=new Size(420,10);
        box.Text=value;
        var body=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16,16,16,8)};
        var card=new CardPanel{Margin=new Padding(0)};var stack=card.Stack(364);body.Controls.Add(card);
        stack.Controls.Add(Row(L.T("名前","Name"),60,box));
        var footer=new FooterBar();
        var cancel=footer.Add(L.T("キャンセル","Cancel"),null,DialogResult.Cancel);
        var ok=footer.Add("OK",null,DialogResult.OK);
        Controls.Add(body);Controls.Add(footer);AcceptButton=ok;CancelButton=cancel;
        Load+=(_,_)=>{FitHeight(body,footer);box.SelectAll();box.Focus();};
    }
}

// The list does not jump to the top of the table each time the table takes the
// focus (choosing a key, a click on the pad picture): the rows are brought into
// sight by ScrollTo instead.
// 表がフォーカスを受けるたびに表の先頭へ戻らないようにする（行は ScrollTo で見える所へ動かす）。
/// <summary>割り当ての表にフォーカスが移っても、勝手に動かない一覧。</summary>
internal sealed class StillPanel : FlowLayoutPanel
{
    protected override Point ScrollToControl(Control active)=>active is BindingGrid || active.Parent is BindingGrid?DisplayRectangle.Location:base.ScrollToControl(active);
}
