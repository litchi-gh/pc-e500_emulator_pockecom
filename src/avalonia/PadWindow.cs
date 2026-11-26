// SPDX-License-Identifier: GPL-2.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Part=PokeCom.PadLayout.Part;
using Shape=PokeCom.PadLayout.Shape;
namespace PokeCom;

/// <summary>コントローラーの絵。押されているボタンを光らせ、スティックの倒し具合とトリガーの引き具合を描く。</summary>
internal sealed class PadPicture : Control
{
    readonly List<Part> parts=[];
    PadDevice? device;
    bool live;
    readonly HashSet<string> pressed=[];
    readonly PadAxes axes=new();
    Dictionary<string,int> layout=PadConfig.StandardLayout();
    string? target,tipText;
    double scale=1;
    Point origin;
    /// <summary>操作の割り当て（マウスを重ねたときに出す。なければ null）。</summary>
    internal Func<string,string?>? BindingOf;
    internal event Action<string>? ControlClicked;
    internal PadPicture(){Height=262;Build();}
    internal void SetDevice(PadDevice? d,bool connected,Dictionary<string,int>? buttons=null){device=d;live=connected;layout=buttons??PadConfig.StandardLayout();target=null;pressed.Clear();axes.Clear();Build();}
    internal void SetTarget(string? pos,Dictionary<string,int> buttons){target=pos;layout=buttons;Build();}
    internal void SetState(HashSet<string> now,PadAxes a) {
        if(pressed.SetEquals(now) && axes.Same(a))return;
        pressed.Clear();pressed.UnionWith(now);a.CopyTo(axes);InvalidateVisual();
    }
    void Build(){parts.Clear();parts.AddRange(PadLayout.Parts(device,layout,target is not null));InvalidateVisual();}
    static Rect Circle(double x,double y,double r)=>new(x-r,y-r,r*2,r*2);
    static void Text(DrawingContext g,string text,Rect r,double size,bool bold,IBrush ink) {
        if(text.Length==0)return;
        var f=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(Paint.Ui,FontStyle.Normal,bold?FontWeight.Bold:FontWeight.Normal),size,ink);
        g.DrawText(f,new Point(r.X+(r.Width-f.Width)/2,r.Y+(r.Height-f.Height)/2));
    }
    public override void Render(DrawingContext g) {
        var bounds=new Rect(70,0,420,232);foreach(var p in parts)bounds=bounds.Union(Paint.Of(p.Area));bounds=bounds.Inflate(6);
        scale=Math.Min(Bounds.Width/bounds.Width,Bounds.Height/bounds.Height);
        origin=new Point((Bounds.Width-bounds.Width*scale)/2-bounds.X*scale,(Bounds.Height-bounds.Height*scale)/2-bounds.Y*scale);
        g.FillRectangle(Brushes.Transparent,new Rect(Bounds.Size));
        using(g.PushTransform(Matrix.CreateScale(scale,scale)*Matrix.CreateTranslation(origin.X,origin.Y)))Draw(g);
        if(live)return;
        // Not connected: the drawing stays, faded, with a word over it.
        g.FillRectangle(Paint.Solid(Color.FromArgb(150,Ui.Card.R,Ui.Card.G,Ui.Card.B)),new Rect(Bounds.Size));
        Text(g,L.T("つながっていません","Not connected"),new Rect(Bounds.Size),14*scale,true,Paint.Solid(Ui.SubText));
    }
    void Draw(DrawingContext g) {
        bool dark=Ui.Dark;
        Color body=dark?Color.FromRgb(62,62,68):Color.FromRgb(222,224,229),line=dark?Color.FromRgb(105,105,112):Color.FromRgb(160,163,170),face=dark?Color.FromRgb(36,36,40):Color.FromRgb(250,250,252);
        IBrush on=Paint.Solid(Ui.Accent),faceBrush=Paint.Solid(face),ink=Paint.Solid(Ui.Text),onInk=Brushes.White;IPen pen=Paint.Pen(line,1.5),ring=Paint.Pen(Ui.Accent,3.5);
        void One(Part p) {
            bool lit=p.Controls.Any(pressed.Contains);var r=Paint.Of(p.Area);
            switch(p.Shape) {
            case Shape.Round:g.DrawEllipse(lit?on:faceBrush,pen,r);Text(g,p.Label,r,r.Width<24?10:12,r.Width>=24,lit?onInk:ink);break;
            case Shape.Pill:case Shape.Arm:g.DrawRectangle(lit?on:faceBrush,pen,new RoundedRect(r,p.Shape==Shape.Pill?r.Height/2:4));Text(g,p.Label,r,10,false,lit?onInk:ink);break;
            case Shape.Hub:g.FillRectangle(faceBrush,r.Inflate(0.5));break;
            case Shape.Trigger: {
                var shape=new RoundedRect(r,7);g.DrawRectangle(faceBrush,null,shape);
                double pull=lit?1:p.Right?axes.RT:axes.LT;
                if(pull>0.02)using(g.PushClip(shape))g.FillRectangle(on,new Rect(r.X,r.Bottom-r.Height*pull,r.Width,r.Height*pull));
                g.DrawRectangle(null,pen,shape);Text(g,p.Label,r,10,false,lit?onInk:ink);break;
            }
            case Shape.Stick: {
                bool tilted=p.Controls.Take(4).Any(pressed.Contains),pushed=p.Controls.Skip(4).Any(pressed.Contains);
                g.DrawEllipse(faceBrush,tilted?ring:pen,r);
                double half=r.Width/2,dx=(p.Right?axes.RX:axes.LX)*half*0.45,dy=(p.Right?axes.RY:axes.LY)*half*0.45;
                var cap=Circle(r.Center.X+dx,r.Center.Y+dy,half*0.58);
                g.DrawEllipse(pushed?on:Paint.Solid(body),pen,cap);Text(g,p.Label,cap,10,false,pushed?onInk:ink);break;
            }
            }
        }
        foreach(var p in parts.Where(p=>p.Area.Y<40))One(p);
        var outline=Paint.Pen(line,3);var fill=Paint.Solid(body);var mid=new RoundedRect(new Rect(115,40,330,138),46);Rect left=new(70,82,150,150),right=new(340,82,150,150);
        g.DrawRectangle(null,outline,mid);g.DrawEllipse(null,outline,left);g.DrawEllipse(null,outline,right);
        g.DrawEllipse(fill,null,left);g.DrawEllipse(fill,null,right);g.DrawRectangle(fill,null,mid);
        foreach(var p in parts.Where(p=>p.Area.Y>=40))One(p);
        // The place asked for while learning: a thick orange ring around it.
        if(target is not null && parts.FirstOrDefault(p=>p.Pos==target) is {} asked) {
            var mark=Paint.Pen(Color.FromRgb(240,120,0),4);var r=Paint.Of(asked.Area).Inflate(6);
            if(asked.Shape is Shape.Round or Shape.Stick)g.DrawEllipse(null,mark,r);else g.DrawRectangle(null,mark,new RoundedRect(r,r.Height/2));
        }
    }
    Part? At(Point p){var d=new Point((p.X-origin.X)/scale,(p.Y-origin.Y)/scale);return parts.LastOrDefault(x=>x.Controls.Length>0 && Paint.Of(x.Area).Inflate(3).Contains(d));}
    protected override void OnPointerMoved(PointerEventArgs e) {
        base.OnPointerMoved(e);
        var p=At(e.GetPosition(this));Cursor=p is null?null:new Cursor(StandardCursorType.Hand);
        string? text=p is null?null:string.Join("\n",p.Controls.Where(c=>c.Length>0).Select(c=>$"{GamePad.ControlName(c)} → {BindingOf?.Invoke(c)??L.T("なし","none")}"));
        if(text!=tipText){tipText=text;ToolTip.SetTip(this,text);}
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);if(At(e.GetPosition(this)) is {} p)ControlClicked?.Invoke(p.Controls[0]);}
}

/// <summary>「コントローラー」ウィンドウ：使うコントローラー、プリセット、ボタンごとの本体のキー。</summary>
internal sealed class PadWindow : AppWindow
{
    /// <summary>「適用」で呼ぶ処理（false を返すとウィンドウを閉じない）。</summary>
    internal Func<PadConfig,Task<bool>>? Apply;
    readonly PadConfig config;
    List<PadDevice> devices=[];
    PadDevice? shown;
    readonly CheckBox enabled=new(){Content=L.T("コントローラーを使う","Use a controller")};
    readonly ComboBox device=new(){Width=330},preset=new(){Width=250};
    readonly TextBlock deviceNote=Ui.Note("",580),pressedLine=new(){MinHeight=24,TextWrapping=TextWrapping.Wrap};
    readonly PadPicture view=new();
    readonly StackPanel rows=new(){Spacing=2};
    readonly Dictionary<string,(Border Row,ComboBox Modifier,ComboBox Key)> made=[];
    readonly ScrollViewer list=new();
    readonly PadAxes axes=new();
    readonly HashSet<string> pressed=[],before=[];
    readonly DispatcherTimer live=new(){Interval=TimeSpan.FromMilliseconds(16)},find=new(){Interval=TimeSpan.FromSeconds(2)};
    readonly Button learn,skip,stopLearn;
    Dictionary<string,int>? learning;
    int learnStep;
    bool filling,scanning;
    sealed record Item(string Value,string Text){public override string ToString()=>Text;}
    /// <summary>キーの一覧：割り当てない、と本体のキーすべて。</summary>
    static readonly Item[] KeyItems=[new("",L.T("（なし）","(none)")),..Keyboard.Matrix.SelectMany(r=>r).Where(k=>k.Length>0).Select(k=>new Item(k,k switch{"UP"=>"↑","DOWN"=>"↓","LEFT"=>"←","RIGHT"=>"→","ON/BRK"=>"ON (BREAK)",_=>k}))];
    static readonly Item[] Modifiers=[new("",""),new("SHIFT","SHIFT +"),new("CTRL","CTRL +")];
    internal PadWindow(PadConfig current) : base(L.T("コントローラー","Controller"),680,860) {
        config=current.Copy();MinWidth=600;MinHeight=520;
        learn=Ui.Button(L.T("ボタンの位置合わせ...","Match button places..."),StartLearn);skip=Ui.Button(L.T("とばす","Skip"),()=>{if(learning is null)return;learnStep++;ShowLearn();});stopLearn=Ui.Button(L.T("やめる","Stop"),()=>EndLearn(false));
        learn.IsVisible=skip.IsVisible=stopLearn.IsVisible=false;
        var top=Ui.CardOf(L.T("コントローラーと動作の確認","Controller and test"),Ui.Stack(Ui.Row(Ui.Label(L.T("使うもの","Use"),90),device,Ui.Button(L.T("再検索","Search again"),()=>_=Rescan())),view,pressedLine,Ui.Row(learn,skip,stopLearn),deviceNote));
        top.Margin=new Thickness(0,0,0,10);
        var buttons=Ui.Row(Ui.Button(L.T("追加...","Add..."),async ()=>{
                if(await AskName(L.T("プリセットの追加","Add a preset"),config.Current.Name+L.T(" のコピー"," copy")) is not string name)return;
                config.Presets.Add(config.Current.Copy(name));config.Active=name;FillPresets();
            }),Ui.Button(L.T("名前の変更...","Rename..."),async ()=>{
                var p=config.Current;if(await AskName(L.T("プリセットの名前の変更","Rename the preset"),p.Name,p.Name) is not string name)return;
                p.Name=name;config.Active=name;FillPresets();
            }),Ui.Button(L.T("削除","Delete"),async ()=>{
                if(config.Presets.Count<=1){await Ask.Message(this,L.T("最後のプリセットは削除できません。","The last preset cannot be deleted."),Title!);return;}
                var p=config.Current;
                if(!await Ask.YesNo(this,L.T($"プリセット「{p.Name}」を削除しますか？",$"Delete the preset \"{p.Name}\"?"),Title!))return;
                config.Presets.Remove(p);config.Active=config.Presets[0].Name;FillPresets();
            }));
        buttons.Margin=new Thickness(138,0,0,0);
        list.Content=new StackPanel{Spacing=10,Children={
            Ui.CardOf(L.T("プリセット","Presets"),Ui.Stack(Ui.Row(Ui.Label(L.T("使うプリセット","Preset in use"),130),preset),buttons,Ui.Note(L.T("「追加」は今のプリセットを写して新しく作ります。割り当ての変更は、表示中のプリセットに入ります。","Add copies the preset shown into a new one. Changes to the buttons go into the preset shown."),580))),
            Ui.CardOf(L.T("ボタンの割り当て","Buttons"),Ui.Stack(Ui.Note(L.T("コントローラーのボタンを押すと、その行が光ります。押している間、本体のキーを押したままにします。","Press a button on the controller and its row lights. The machine's key is held while the button is."),580),rows))}};
        var body=new DockPanel();DockPanel.SetDock(top,Dock.Top);body.Children.AddRange([top,list]);
        enabled.IsChecked=config.Enabled;
        var ok=Ui.Button(L.T("適用","Apply"),async ()=>{EndLearn(true);config.Enabled=enabled.IsChecked==true;if(Apply is null || await Apply(config.Copy()))Close();},true);
        Fill(body,Ui.Footer(enabled,ok,Ui.Button(L.T("閉じる","Close"),Close)));
        view.BindingOf=id=>config.Current.Map.GetValueOrDefault(id);
        view.ControlClicked+=Choose;
        device.SelectionChanged+=(_,_)=>{if(filling)return;config.Device=device.SelectedItem is Item i?i.Value:"";ShowDevice();};
        preset.SelectionChanged+=(_,_)=>{if(filling || preset.SelectedItem is not Item i)return;config.Active=i.Value;ShowBindings();};
        live.Tick+=(_,_)=>Poll();find.Tick+=(_,_)=>{if(shown is null)_=Rescan();};
        Opened+=(_,_)=>{FillPresets();_=Rescan();live.Start();find.Start();};
        Closed+=(_,_)=>{live.Stop();find.Stop();};
    }
    async Task<string?> AskName(string title,string suggestion,string? own=null) {
        while(await Ask.Name(this,title,suggestion) is string name) {
            if(name==own || !config.Presets.Any(p=>p.Name==name))return name;
            await Ask.Message(this,L.T("同じ名前のプリセットがあります。","A preset of that name exists."),title);
        }
        return null;
    }
    async Task Rescan() {
        if(scanning)return;
        scanning=true;
        try{devices=await Task.Run(GamePad.Scan);}catch(Exception){devices=[];}
        scanning=false;
        filling=true;
        var items=new List<Item>{new("",L.T("自動（最初に見つかったもの）","Automatic (the first one found)"))};
        items.AddRange(devices.Select(d=>new Item(d.Id,d.Name)));
        if(config.Device.Length>0 && !devices.Any(d=>d.Id==config.Device))items.Add(new(config.Device,L.T($"（つながっていない）{config.Device}",$"(not connected) {config.Device}")));
        device.ItemsSource=items;device.SelectedIndex=Math.Max(0,items.FindIndex(i=>i.Value==config.Device));
        filling=false;ShowDevice();
    }
    void ShowDevice() {
        var picked=GamePad.Pick(devices,config.Device);bool numbered=picked?.Kind==PadKind.DirectInput;
        string text=picked is not null?L.T($"接続中: {picked.Name}",$"Connected: {picked.Name}")
            :devices.Count==0?L.T("コントローラーが見つかりません。つなぐと自動で見つけます。絵と一覧は Xbox 系の並びです。","No controller found; one plugged in is found by itself. The picture and list show the Xbox layout.")
            :L.T("選んだコントローラーはつながっていません。絵と一覧は Xbox 系の並びです。","The chosen controller is not connected. The picture and list show the Xbox layout.");
        if(numbered)text+=L.T("　番号の位置が実物と違うときは「ボタンの位置合わせ」で合わせられます。","  If the numbers sit elsewhere on the pad, match them with Match button places.");
        deviceNote.Text=text;
        if(learning is not null && (shown?.Id!=picked?.Id || !numbered))EndLearn(false);
        learn.IsVisible=numbered && learning is null;
        view.SetDevice(picked,picked is not null,numbered?config.LayoutFor(picked):null);
        shown=picked;
        var controls=GamePad.Controls(picked);
        if(!controls.SequenceEqual(made.Keys))BuildRows(controls);
    }
    void BuildRows(string[] controls) {
        rows.Children.Clear();made.Clear();
        foreach(string id in controls) {
            var modifier=new ComboBox{ItemsSource=Modifiers,Width=110};var key=new ComboBox{ItemsSource=KeyItems,Width=170,MaxDropDownHeight=420};
            void Changed(){if(!filling)Set(id,(modifier.SelectedItem as Item)?.Value??"",(key.SelectedItem as Item)?.Value??"");}
            modifier.SelectionChanged+=(_,_)=>Changed();key.SelectionChanged+=(_,_)=>Changed();
            var row=new Border{Padding=new Thickness(8,2),CornerRadius=new CornerRadius(4),Child=Ui.Row(Ui.Label(GamePad.ControlName(id),220),modifier,key)};
            made[id]=(row,modifier,key);rows.Children.Add(row);
        }
        ShowBindings();
    }
    static (string Modifier,string Key) Split(string b)=>b.StartsWith("SHIFT+",StringComparison.Ordinal) && b.Length>6?("SHIFT",b[6..]):b.StartsWith("CTRL+",StringComparison.Ordinal) && b.Length>5?("CTRL",b[5..]):("",b);
    void ShowBindings() {
        filling=true;
        foreach(var (id,row) in made) {
            var (m,k)=Split(config.Current.Map.GetValueOrDefault(id)??"");
            row.Modifier.SelectedItem=Modifiers.First(i=>i.Value==m);row.Key.SelectedItem=KeyItems.FirstOrDefault(i=>i.Value==k)??KeyItems[0];
        }
        filling=false;
    }
    void Set(string id,string modifier,string key){var map=config.Current.Map;if(key.Length==0)map.Remove(id);else map[id]=PadConfig.Binding(modifier,key);}
    void Choose(string id){if(made.TryGetValue(id,out var row)){row.Row.BringIntoView();row.Key.Focus();}}
    void FillPresets() {
        filling=true;
        var items=config.Presets.Select(p=>new Item(p.Name,p.Name)).ToArray();
        preset.ItemsSource=items;preset.SelectedIndex=Math.Max(0,config.Presets.IndexOf(config.Current));
        filling=false;ShowBindings();
    }
    void Poll() {
        before.Clear();before.UnionWith(pressed);
        if(shown is null || !GamePad.Read(shown,pressed,axes)) {
            pressed.Clear();axes.Clear();
            if(shown is not null){shown=null;_=Rescan();}
        }
        view.SetState(pressed,axes);
        if(learning is not null) {
            // Learning: the first numbered button newly pressed belongs to the place asked for.
            if(pressed.Except(before).FirstOrDefault(c=>c.StartsWith("Button",StringComparison.Ordinal)) is string hit)Learn(int.Parse(hit[6..],CultureInfo.InvariantCulture));
            return;
        }
        if(pressed.SetEquals(before) && pressedLine.Text is {Length:>0})return;
        Highlight();
        if(pressed.Except(before).FirstOrDefault(made.ContainsKey) is string fresh)made[fresh].Row.BringIntoView();
    }
    void Highlight() {
        var order=GamePad.Controls(shown);
        var now=pressed.OrderBy(c=>{int i=Array.IndexOf(order,c);return i<0?int.MaxValue:i;}).Select(c=>$"{GamePad.ControlName(c)} → {config.Current.Map.GetValueOrDefault(c)??L.T("なし","none")}").ToArray();
        pressedLine.Text=now.Length==0?L.T("押しているボタン: なし","Pressed: nothing"):L.T("押しているボタン: ","Pressed: ")+string.Join(L.T("、",", "),now);
        pressedLine.Foreground=Paint.Solid(now.Length==0?Ui.SubText:Ui.Accent);
        foreach(var (id,row) in made)row.Row.Background=pressed.Contains(id)?Paint.Solid(Color.FromArgb(60,Ui.Accent.R,Ui.Accent.G,Ui.Accent.B)):Brushes.Transparent;
    }
    void StartLearn() {
        if(shown is not {Kind:PadKind.DirectInput})return;
        learning=[];learnStep=0;learn.IsVisible=false;skip.IsVisible=stopLearn.IsVisible=true;ShowLearn();
    }
    void ShowLearn() {
        if(learning is null)return;
        if(learnStep>=PadLayout.Positions.Length){EndLearn(true);return;}
        string pos=PadLayout.Positions[learnStep];view.SetTarget(pos,learning);
        pressedLine.Foreground=Paint.Solid(Color.FromRgb(200,100,0));
        pressedLine.Text=L.T($"位置合わせ {learnStep+1}/{PadLayout.Positions.Length}: 「{PadLayout.PositionName(pos)}」のボタンを押してください（ないときは「とばす」）",$"Matching {learnStep+1}/{PadLayout.Positions.Length}: press the button at the {PadLayout.PositionName(pos)} (or Skip)");
    }
    void Learn(int button) {
        if(learning is null)return;
        foreach(var key in learning.Where(x=>x.Value==button).Select(x=>x.Key).ToList())learning.Remove(key);
        learning[PadLayout.Positions[learnStep]]=button;learnStep++;ShowLearn();
    }
    void EndLearn(bool keep) {
        if(learning is null)return;
        if(keep && PadConfig.LayoutKey(shown) is string key)config.Layouts[key]=learning;
        learning=null;skip.IsVisible=stopLearn.IsVisible=false;learn.IsVisible=shown?.Kind==PadKind.DirectInput;
        view.SetDevice(shown,shown is not null,shown?.Kind==PadKind.DirectInput?config.LayoutFor(shown):null);
        Highlight();
        if(keep)pressedLine.Text=L.T("位置合わせが終わりました（「適用」で保存します）。","Places matched (Apply keeps them).");
    }
}
