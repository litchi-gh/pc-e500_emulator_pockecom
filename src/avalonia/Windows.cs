// SPDX-License-Identifier: GPL-2.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
namespace PokeCom;

/// <summary>「設定」ウィンドウ（内蔵 RAM・RAM カード・CE-140F・音声・外観・言語）。</summary>
internal sealed class SettingsWindow : AppWindow
{
    readonly ComboBox internalRam=new(){Width=170},appearance=new(){Width=170},language=new(){Width=170},theme=new(){Width=220};
    readonly TextBox cardPath=new(){Width=380,IsReadOnly=true},folder=new(){Width=470};
    readonly CheckBox disk=new(){Content=L.T("CE-140Fを接続 (X:)","Connect CE-140F (X:)")},protect=new(){Content=L.T("ディスクを書込み禁止にする","Write-protect the disk")},
        muted=new(){Content=L.T("消音","Mute")},autoOff=new(){Content=L.T("自動電源OFF（実機と同じ）","Auto power off (as the real one)")},topMost=new(){Content=L.T("常に手前に表示","Always on top")},
        checkUpdates=new(){Content=L.T("起動時に新しいバージョンを確認する（1日に1回）","Check for a new version at start-up (once a day)")};
    readonly NumericUpDown volume=new(){Minimum=0,Maximum=100,Width=130,FormatString="0",Increment=5};
    readonly TextBlock ramNote=Ui.Note("",560);
    /// <summary>「適用」で呼ぶ処理。引数は今の設定にウィンドウの値を重ねる関数。false を返すとウィンドウを閉じない。</summary>
    internal Func<Func<HardwareSettings,HardwareSettings>,Task<bool>>? Apply;
    /// <summary>今、本体に装着しているカードのファイル（そのファイルには新しいカードを作らせない）。</summary>
    internal Func<string?>? InUse;
    /// <summary>本体の ROM が変わったときに、選べる内蔵 RAM の容量と説明を、その機種に合わせ直す。</summary>
    internal void SetRom(int standardKB,int internalKB) {
        int[] sizes=HardwareSettings.InternalSizes(standardKB);
        internalRam.ItemsSource=sizes;internalRam.SelectedItem=sizes.Contains(internalKB)?internalKB:sizes[0];
        ramNote.Text=standardKB==64
            ?L.T("実機は 64KB。128・256KB は増設改造に合わせた値です。容量ごとに内容を残し、変えると再起動します。","The machine has 64KB; 128 and 256KB match the expansion mod. Kept per capacity; a change restarts.")
            :L.T("容量ごとに内容を残し、変えると再起動します。8・16KB では BASIC の空きが少なくなります。","Kept per capacity; a change restarts. 8 and 16KB leave little room for BASIC.");
    }
    HardwareSettings Over(HardwareSettings current)=>current with{
        InternalKB=(int)internalRam.SelectedItem!,CardPath=(cardPath.Text??"").Trim(),DiskEnabled=disk.IsChecked==true,DiskReadOnly=protect.IsChecked==true,
        DiskFolder=folder.Text??"",Muted=muted.IsChecked==true,Volume=(int)(volume.Value??50),CheckUpdates=checkUpdates.IsChecked==true,AutoPowerOff=autoOff.IsChecked==true,TopMost=topMost.IsChecked==true,
        Appearance=(string)appearance.SelectedItem!,Language=L.Codes[Math.Max(0,language.SelectedIndex)],Theme=HardwareSettings.Themes[Math.Max(0,theme.SelectedIndex)]};
    internal SettingsWindow(HardwareSettings settings,int standardKB,string cardFolder) : base(L.T("ハードウェア・外観設定","Hardware and Appearance"),680,780) {
        MinWidth=560;MinHeight=360;
        appearance.ItemsSource=HardwareSettings.Appearances;appearance.SelectedItem=settings.Appearance;
        language.ItemsSource=(string[])["日本語","English"];language.SelectedIndex=Math.Max(0,Array.IndexOf(L.Codes,L.Codes.Contains(settings.Language)?settings.Language:L.Default));
        theme.ItemsSource=(string[])[L.T("システムに合わせる","As the system"),L.T("ライト","Light"),L.T("ダーク","Dark")];theme.SelectedIndex=Math.Max(0,Array.IndexOf(HardwareSettings.Themes,settings.Theme));
        SetRom(standardKB,settings.InternalKB);
        const double W=150;
        var body=new StackPanel{Spacing=10};
        body.Children.Add(Ui.CardOf(L.T("表示","Display"),Ui.Stack(Ui.Row(Ui.Label(L.T("言語 / Language","Language / 言語"),W),language),Ui.Row(Ui.Label(L.T("テーマ","Theme"),W),theme),
            Ui.Note(L.T("言語・テーマは、適用するとエミュレータを再起動します（状態は保存）。","A new language or theme restarts the emulator when applied (the state is saved)."),560))));
        cardPath.Text=settings.CardPath;
        var cardInfo=Ui.Note("",560);
        void Describe() {
            string p=(cardPath.Text??"").Trim();
            string state=p.Length==0?L.T("未装着","not attached"):!File.Exists(p)?L.T("ファイルが見つかりません","file not found"):$"{new FileInfo(p).Length/1024}KB";
            cardInfo.Text=L.T($"今: {state}。イメージファイル（8〜256KB）を装着し、5 秒ごと・OFF 時・終了時に書き込みます。装着・取り外しは適用時に再起動します。",$"Now: {state}. An image file (8-256KB), written every 5 s, at power off and at exit. Attaching or detaching restarts when applied.");
        }
        cardPath.TextChanged+=(_,_)=>Describe();Describe();
        var ramCards=Ui.Type(L.T("RAMカードイメージ","RAM card image"),"*.ram","*.bin");
        var create=Ui.Button(L.T("作成...","Create..."),async ()=>{
            if(await AskSize() is not int kb)return;
            if(await Ui.SaveFile(this,cardFolder,$"card-{kb}KB.ram","ram",ramCards) is not string path)return;
            if(InUse?.Invoke() is string used && string.Equals(Path.GetFullPath(used),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)) {
                await Ask.Message(this,L.T("本体に装着しているカードのファイルには、新しいカードを作れません。別の名前にするか、先に取り外して適用してください。","A new card cannot be made over the card in the machine. Choose another name, or detach it and apply first."),L.T("RAMカード作成","Create RAM card"));
                return;
            }
            try{Core.CreateCardImage(path,kb);cardPath.Text=path;}catch(Exception ex){await Ask.Message(this,ex.Message,L.T("RAMカード作成","Create RAM card"));}
        });
        var attach=Ui.Button(L.T("装着...","Attach..."),async ()=>{if(await Ui.OpenFile(this,cardFolder,ramCards) is string path)cardPath.Text=path;});
        var detach=Ui.Button(L.T("取り外す","Detach"),()=>cardPath.Text="");
        var cardButtons=Ui.Row(create,attach,detach);cardButtons.Margin=new Thickness(W+8,0,0,0);
        body.Children.Add(Ui.CardOf(L.T("本体","Machine"),Ui.Stack(Ui.Row(Ui.Label(L.T("本体の外観","Appearance"),W),appearance),Ui.Row(Ui.Label(L.T("内蔵RAM (KB)","Internal RAM (KB)"),W),internalRam),ramNote,
            Ui.Row(Ui.Label(L.T("RAMカード","RAM card"),W),cardPath),cardButtons,cardInfo)));
        disk.IsChecked=settings.DiskEnabled;protect.IsChecked=settings.DiskReadOnly;muted.IsChecked=settings.Muted;checkUpdates.IsChecked=settings.CheckUpdates;autoOff.IsChecked=settings.AutoPowerOff;topMost.IsChecked=settings.TopMost;volume.Value=settings.Volume;folder.Text=settings.DiskFolder;
        body.Children.Add(Ui.CardOf("CE-140F",Ui.Stack(disk,protect,Ui.Row(folder,Ui.Button(L.T("参照","Browse"),async ()=>{if(await Ui.OpenFolder(this,L.T("CE-140F専用の保存フォルダを選択","Select a folder dedicated to the CE-140F")) is string path)folder.Text=path;})))));
        var offNote=Ui.Note(L.T("キーを押さずに約10分で電源が切れます（外すと切れません）。","Switches off after about 10 minutes without a key (cleared, it stays on)."),540);offNote.Margin=new Thickness(28,0,0,4);
        body.Children.Add(Ui.CardOf(L.T("音・電源・その他","Sound, power and more"),Ui.Stack(Ui.Row(Ui.Label(L.T("音量 (%)","Volume (%)"),W),volume,muted),autoOff,offNote,topMost,checkUpdates)));
        var ok=Ui.Button(L.T("適用","Apply"),async ()=>{if(Apply is null || await Apply(Over))Close();},true);ok.IsDefault=true;
        Fill(body,Ui.Footer(null,ok,Ui.Button(L.T("閉じる","Close"),Close)),true);
    }
    /// <summary>新しい RAM カードの容量を聞く（やめたら null）。</summary>
    async Task<int?> AskSize() {
        int? answer=null;
        var window=new AppWindow(L.T("RAMカード作成","Create RAM card"),430);
        var size=new ComboBox{Width=120,ItemsSource=HardwareSettings.Sizes,SelectedItem=64};
        var ok=Ui.Button("OK",()=>{answer=(int)size.SelectedItem!;window.Close();},true);ok.IsDefault=true;
        var dock=new DockPanel();var footer=Ui.Footer(null,ok,Ui.Button(L.T("キャンセル","Cancel"),window.Close));DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        dock.Children.Add(new StackPanel{Margin=new Thickness(18,16),Spacing=8,Children={Ui.Row(Ui.Label(L.T("容量 (KB)","Capacity (KB)"),100),size),Ui.Note(L.T("全バイト FF の空カードを作ります。装着後、最初の PF1 で初期化されます。","A blank card of FF bytes. The first PF1 after attaching formats it."))}});
        window.Content=dock;await window.ShowDialog(this);return answer;
    }
}

/// <summary>「画面」ウィンドウ（LCD のリアル表示・残像・コントラスト・ウィンドウの表示）。変更はすぐに反映する。</summary>
internal sealed class DisplayWindow : AppWindow
{
    readonly RadioButton packed=new(){Content=L.T("標準（ドットを詰めて表示）","Standard (dots packed together)"),GroupName="lcd"},real=new(){Content=L.T("リアル（実機の液晶のようにドットの間をあける）","Real (dots apart, as on the real LCD)"),GroupName="lcd"};
    readonly CheckBox afterimage=new(){Content=L.T("残像（フレーム補完）","Afterimage (frame interpolation)")};
    readonly Slider contrast=new(){Minimum=0,Maximum=100,Width=250,TickFrequency=10,SmallChange=1,LargeChange=10,IsSnapToTickEnabled=false};
    readonly TextBlock value=Ui.Label("",30);
    readonly RadioButton[] views=[..((string[])[L.T("通常（タイトル・ボタンを表示）","Normal (title and buttons)"),L.T("本体だけ","Case only"),L.T("画面だけ","Screen only")]).Select(t=>new RadioButton{Content=t,GroupName="view"})];
    bool syncing;
    internal Action<bool,int>? LcdChanged;
    internal Action<bool>? AfterimageChanged;
    internal Action<WindowView>? ViewChanged;
    internal DisplayWindow() : base(L.T("画面設定","Display"),540) {
        var motionNote=Ui.Note(L.T("1ドットずつ動くものをなめらかに滑らせ、ちらつきも抑えます。","Things moving a dot at a time slide smoothly, without flicker."),440);motionNote.Margin=new Thickness(28,0,0,0);
        var body=new StackPanel{Spacing=10,Children={
            Ui.CardOf(L.T("液晶の表示","LCD"),Ui.Stack(packed,real,afterimage,motionNote)),
            Ui.CardOf(L.T("液晶のコントラスト","LCD contrast"),Ui.Stack(Ui.Row(contrast,value,Ui.Button(L.T("標準に戻す","Default"),()=>contrast.Value=LcdGlass.DefaultContrast)),
                Ui.Note(L.T("ドットと表示シンボルの濃さが一緒に変わります（実機のつまみと同じ）。","The dots and legends darken together, as with the real dial."),440))),
            Ui.CardOf(L.T("ウィンドウの表示","Window"),Ui.Stack([..views,Ui.Note(L.T("本体のキーのない所をダブルクリックしても切り替わります。右クリックでメニュー。","A double-click on the case off the keys switches too. Right-click for the menu."),440)]))}};
        Fill(body,Ui.Footer(null,Ui.Button(L.T("閉じる","Close"),Close)));
        void Lcd(){value.Text=((int)contrast.Value).ToString();if(!syncing)LcdChanged?.Invoke(real.IsChecked==true,(int)contrast.Value);}
        packed.IsCheckedChanged+=(_,_)=>{if(packed.IsChecked==true)Lcd();};real.IsCheckedChanged+=(_,_)=>{if(real.IsChecked==true)Lcd();};
        contrast.ValueChanged+=(_,_)=>Lcd();
        afterimage.IsCheckedChanged+=(_,_)=>{if(!syncing)AfterimageChanged?.Invoke(afterimage.IsChecked==true);};
        for(int i=0;i<views.Length;i++){var v=(WindowView)i;var b=views[i];b.IsCheckedChanged+=(_,_)=>{if(!syncing && b.IsChecked==true)ViewChanged?.Invoke(v);};}
    }
    /// <summary>表示を今の値に合わせる（メインウィンドウ側で変わったとき）。変更は知らせない。</summary>
    internal void Sync(bool realLcd,int lcdContrast,WindowView view,bool lcdAfterimage) {
        syncing=true;
        try{(realLcd?real:packed).IsChecked=true;afterimage.IsChecked=lcdAfterimage;contrast.Value=lcdContrast;value.Text=lcdContrast.ToString();views[(int)view].IsChecked=true;}
        finally{syncing=false;}
    }
}

/// <summary>CE-140F のトレース（コマンドとエラーの記録）のウィンドウ。</summary>
internal sealed class DiskWindow : AppWindow
{
    readonly TextBox text=new(){IsReadOnly=true,AcceptsReturn=true,FontFamily=Ui.Mono,FontSize=12,BorderThickness=new Thickness(0),TextWrapping=TextWrapping.NoWrap};
    readonly CheckBox live=new(){Content=L.T("自動更新","Auto refresh"),IsChecked=true};
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    readonly Func<Core?> read;
    readonly Func<string> folder;
    internal DiskWindow(Func<Core?> read,Func<string> folder) : base("CE-140F / X:",760,500) {
        this.read=read;this.folder=folder;MinWidth=460;MinHeight=300;
        Fill(Ui.CardOf(L.T("通信の記録","Exchange log"),text),Ui.Footer(live,
            Ui.Button(L.T("保存フォルダを開く","Open folder"),async ()=>{try{Directory.CreateDirectory(folder());Ui.Launch(folder());}catch(Exception ex){await Ask.Message(this,ex.Message);}}),
            Ui.Button(L.T("更新","Refresh"),Render),Ui.Button(L.T("閉じる","Close"),Close)));
        timer.Tick+=(_,_)=>{if(live.IsChecked==true)Render();};
        Opened+=(_,_)=>{Render();timer.Start();};Closed+=(_,_)=>timer.Stop();
    }
    void Render() {
        string now=L.T("保存先: ","Folder: ")+folder()+"\n\n"+(read()?.DiskTrace()??L.T("ROMが読み込まれていません。","No ROM is loaded."));
        // A selection being made (to copy) is not swept away.
        if(text.Text==now || text.SelectionStart!=text.SelectionEnd)return;
        text.Text=now;text.CaretIndex=now.Length;
    }
}

/// <summary>LCD 文字のコピー・貼り付けのウィンドウ。</summary>
internal sealed class TextWindow : AppWindow
{
    readonly TextBox text=new(){IsReadOnly=true,AcceptsReturn=true,FontFamily=Ui.Mono,FontSize=17,TextWrapping=TextWrapping.NoWrap,MinHeight=120,BorderThickness=new Thickness(0)};
    readonly TextBlock result=new(){FontSize=12,VerticalAlignment=VerticalAlignment.Center,Foreground=Paint.Solid(Ui.SubText)};
    internal TextWindow(Func<string> read,Func<string,string> paste) : base(L.T("LCD文字のコピー・貼り付け","LCD text copy and paste"),760,400) {
        MinWidth=600;MinHeight=320;
        void Refresh(){text.Text=read();result.Text=L.T("画面を読み取りました。","Screen read.");}
        var tools=Ui.Row(Ui.Button(L.T("画面を更新","Refresh"),Refresh),Ui.Button(L.T("全選択","Select all"),()=>{text.Focus();text.SelectAll();}),
            Ui.Button(L.T("選択範囲をコピー","Copy selection"),async ()=>{
                string value=text.SelectedText is {Length:>0} s?s:text.Text??"";
                if(value.Length==0){result.Text=L.T("コピーする文字がありません。","Nothing to copy.");return;}
                try{if(Clipboard is not null)await Clipboard.SetTextAsync(value);result.Text=L.T($"{value.Length}文字をコピーしました。",$"Copied {value.Length} characters.");}
                catch(Exception ex){result.Text=L.T("コピーできません: ","Could not copy: ")+ex.Message;}
            }),
            Ui.Button(L.T("クリップボードから貼り付け","Paste from clipboard"),async ()=>{
                try {
                    string? value=Clipboard is null?null:await Clipboard.TryGetTextAsync();
                    result.Text=string.IsNullOrEmpty(value)?L.T("クリップボードに文字列がありません。","The clipboard has no text."):paste(value);
                } catch(Exception ex){result.Text=L.T("貼り付けできません: ","Could not paste: ")+ex.Message;}
            }));
        tools.Margin=new Thickness(0,0,0,8);
        var card=new DockPanel();DockPanel.SetDock(tools,Dock.Top);card.Children.AddRange([tools,text]);
        var note=Ui.Note(L.T("LCD を 40 桁 × 4 行の文字で読み取ります。貼り付けは ASCII と改行をキー入力として送ります。","Reads the LCD as 40 × 4 characters. Paste types ASCII and line breaks as keys."));note.Margin=new Thickness(0,0,0,8);
        var body=new DockPanel();DockPanel.SetDock(note,Dock.Top);body.Children.AddRange([note,Ui.CardOf(L.T("LCD の文字","LCD text"),card)]);
        Fill(body,Ui.Footer(result,Ui.Button(L.T("閉じる","Close"),Close)));
        Opened+=(_,_)=>Refresh();
    }
}

/// <summary>機械語エリアの大きさを決めるウィンドウ。</summary>
internal sealed class MachineCodeWindow : AppWindow
{
    /// <summary>「適用」で呼ぶ処理（新しい大きさ）。false ならウィンドウを閉じない。</summary>
    internal Func<int,Task<bool>>? Apply;
    internal MachineCodeWindow(List<Region> layout,int current,int largest) : base(L.T("機械語エリア","Machine-code area"),640) {
        var now=new MemoryBar{Emphasis=RegionKind.MachineCode,Legend=false};var after=new MemoryBar{Emphasis=RegionKind.MachineCode};
        var title=Ui.Heading("");var bytes=Ui.Label("");
        var size=new TextBox{Width=100,FontFamily=Ui.Mono,Text=current.ToString("X")};
        int Value()=>int.TryParse(size.Text,System.Globalization.NumberStyles.HexNumber,null,out int v)?Math.Clamp(v,0,Math.Max(current,largest)):current;
        void Show() {
            int value=Value();
            bytes.Text=L.T($"= {value:N0} バイト（{RamLayout.Size(value)}）",$"= {value:N0} bytes ({RamLayout.Size(value)})");
            title.Text=value==current?L.T("適用後（変わりません）","After (no change)"):value==0?L.T("適用後（機械語エリアなし）","After (no machine-code area)"):L.T($"適用後（機械語エリア {0xbfc00-value:X5}〜BFBFF）",$"After (machine code {0xbfc00-value:X5}-BFBFF)");
            after.SetRegions(RamLayout.WithMachineCode(layout,value));
        }
        void Step(int by){size.Text=Math.Clamp((Value()+by)/0x100*0x100,0,Math.Max(current,largest)).ToString("X");}
        size.TextChanged+=(_,_)=>Show();
        now.SetRegions(layout);Show();
        var body=new StackPanel{Spacing=12,Children={
            Ui.CardOf(L.T("内蔵 RAM の並び","Internal RAM"),Ui.Stack(Ui.Heading(L.T("今","Now")),now,title,after)),
            Ui.Row(Ui.Label(L.T("大きさ（16進）","Size (hex)")),size,Ui.Button("−100",()=>Step(-0x100)),Ui.Button("+100",()=>Step(0x100)),bytes),
            Ui.Note(L.T($"BFBFF から下に取ります。0 で返します（最大 {largest:X}h）。BASIC の RUN・PRO の表示で使います。プログラムと変数は残ります。",$"Taken below BFBFF; 0 gives it back (at most {largest:X}h). Use it at BASIC's RUN or PRO prompt; the program and variables stay."))}};
        var ok=Ui.Button(L.T("適用","Apply"),async ()=>{if(Apply is null || await Apply(Value()))Close();},true);ok.IsDefault=true;
        Fill(body,Ui.Footer(null,ok,Ui.Button(L.T("閉じる","Close"),Close)));
    }
}

/// <summary>新しい版のお知らせ（変更点、ダウンロードのページ）。</summary>
internal sealed class UpdateWindow : AppWindow
{
    /// <summary>「この版は通知しない」を押したか。</summary>
    internal bool Skip {get;private set;}
    internal UpdateWindow(Release release) : base(L.T("新しいバージョンのお知らせ","New version"),620,480) {
        var heading=Ui.Heading(L.T($"新しいバージョン {release.Tag} が公開されています（今のバージョンは {UpdateCheck.Current}）。",$"Version {release.Tag} is out (this is {UpdateCheck.Current})."));heading.Margin=new Thickness(0,0,0,10);
        var notes=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Text=(release.Notes.Length>0?release.Notes:release.Name).Replace("\r\n","\n")};
        var how=Ui.Note(L.T("更新：エミュレータを終了して、ダウンロードしたものを今のフォルダに上書きで展開します。ROM・RAM・設定はそのまま残ります。","To update: quit the emulator and unpack the download over its folder. ROMs, RAM and settings stay."));how.Margin=new Thickness(0,8,0,0);
        var body=new DockPanel();DockPanel.SetDock(heading,Dock.Top);DockPanel.SetDock(how,Dock.Bottom);body.Children.AddRange([heading,how,Ui.CardOf(L.T("変更点","Changes"),notes)]);
        var open=Ui.Button(L.T("ダウンロードのページを開く","Open the download page"),async ()=>{try{UpdateCheck.Open(release.Url);Close();}catch(Exception ex){await Ask.Message(this,ex.Message);}},true);open.IsDefault=true;
        Fill(body,Ui.Footer(null,open,Ui.Button(L.T("このバージョンは通知しない","Skip this version"),()=>{Skip=true;Close();}),Ui.Button(L.T("閉じる","Close"),Close)));
    }
}

/// <summary>RAM のスロット1つを横の帯で描く（左が低い番地）。押すとその区画を知らせる。</summary>
internal sealed class MemoryBar : Control
{
    IReadOnlyList<Region> regions=[];
    readonly List<(Rect Area,Region Region)> laid=[];
    Region? hover;
    int marker=-1;
    string title="";
    internal string BarTitle {get=>title;set{if(title==value)return;title=value;InvalidateMeasure();InvalidateVisual();}}
    internal RegionKind? Emphasis;
    internal bool Legend=true;
    /// <summary>帯の下に印を付ける番地（-1 なら付けない）。</summary>
    internal int Marker {get=>marker;set{if(marker!=value){marker=value;InvalidateVisual();}}}
    internal event Action<Region>? RegionClicked;
    internal void SetRegions(IReadOnlyList<Region> value){if(regions.SequenceEqual(value))return;regions=value;InvalidateMeasure();InvalidateVisual();}
    const double Line=17,BarHeight=30;
    static FormattedText Form(string text,Color color,bool bold=false)=>new(text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(Paint.Ui,FontStyle.Normal,bold?FontWeight.SemiBold:FontWeight.Normal),12,Paint.Solid(color));
    IEnumerable<RegionKind> Kinds()=>regions.Select(r=>r.Kind).Distinct().OrderBy(k=>k);
    static double LegendWidth(RegionKind k)=>28+Form(RamLayout.KindName(k),Colors.Black).Width;
    int LegendLines(double width){int lines=1;double x=0;foreach(var k in Kinds()){double w=LegendWidth(k);if(x>0 && x+w>width){lines++;x=0;}x+=w;}return lines;}
    protected override Size MeasureOverride(Size available) {
        double width=double.IsInfinity(available.Width)?560:available.Width;
        return new Size(width,(title.Length>0?Line+4:0)+BarHeight+10+Line+6+(Legend?LegendLines(width)*(Line+3):0));
    }
    public override void Render(DrawingContext g) {
        laid.Clear();g.FillRectangle(Brushes.Transparent,new Rect(Bounds.Size));if(regions.Count==0)return;
        double y=0;
        if(title.Length>0){g.DrawText(Form(title,Ui.Text,true),new Point(0,0));y=Line+4;}
        var bar=new Rect(0,y,Math.Max(10,Bounds.Width-1),BarHeight);
        var widths=RamLayout.Widths(regions,(int)bar.Width,6);
        using(g.PushClip(new RoundedRect(bar,6))) {
            double x=0;
            for(int i=0;i<regions.Count;i++) {
                var r=regions[i];var area=new Rect(x,bar.Y,widths[i],BarHeight);x+=widths[i];laid.Add((area,r));
                var color=Paint.Of(RamLayout.Color(r.Kind,Ui.Dark));
                if(Emphasis is RegionKind em && r.Kind!=em && r.Kind!=RegionKind.Free)color=Ui.Mix(color,Ui.Card,0.45);
                g.FillRectangle(Paint.Solid(color),area);
                if(r==hover)g.FillRectangle(Paint.Solid(Color.FromArgb(50,255,255,255)),area);
                g.DrawLine(Paint.Pen(Ui.Card,1),area.TopRight,area.BottomRight);
                var ink=r.Kind==RegionKind.Free?Ui.SubText:Colors.White;
                var text=Form(r.Name+"  "+RamLayout.Size(r.Size),ink);
                if(text.Width+8>area.Width)text=Form(r.Name,ink);
                if(text.Width+8<=area.Width)g.DrawText(text,new Point(area.X+(area.Width-text.Width)/2,area.Y+(BarHeight-text.Height)/2));
            }
        }
        g.DrawRectangle(null,Paint.Pen(Ui.Border,1),new RoundedRect(bar.Deflate(0.5),6));
        y=bar.Bottom+10;
        g.DrawText(Form($"{regions[0].Start:X5}",Ui.SubText),new Point(0,y));
        var end=Form($"{regions[^1].End-1:X5}",Ui.SubText);g.DrawText(end,new Point(Bounds.Width-end.Width-2,y));
        if(marker>=0 && laid.FirstOrDefault(l=>marker>=l.Region.Start && marker<l.Region.End) is {Region: not null} hit) {
            double mx=hit.Area.X+hit.Area.Width*(marker-hit.Region.Start)/Math.Max(1,hit.Region.Size);
            var arrow=new StreamGeometry();using(var c=arrow.Open()){c.BeginFigure(new Point(mx,bar.Bottom+1),true);c.LineTo(new Point(mx-5,bar.Bottom+8));c.LineTo(new Point(mx+5,bar.Bottom+8));c.EndFigure(true);}
            g.DrawGeometry(Paint.Solid(Ui.Text),null,arrow);
        }
        if(!Legend)return;
        y+=Line+6;double lx=0;
        foreach(var k in Kinds()) {
            double w=LegendWidth(k);
            if(lx>0 && lx+w>Bounds.Width){lx=0;y+=Line+3;}
            g.DrawRectangle(Paint.Solid(Paint.Of(RamLayout.Color(k,Ui.Dark))),null,new RoundedRect(new Rect(lx,y+3,10,10),2));
            g.DrawText(Form(RamLayout.KindName(k),Ui.SubText),new Point(lx+14,y));lx+=w;
        }
    }
    Region? At(Point p)=>laid.FirstOrDefault(l=>l.Area.Contains(p)).Region;
    protected override void OnPointerMoved(PointerEventArgs e) {
        base.OnPointerMoved(e);
        var now=At(e.GetPosition(this));if(now==hover)return;
        hover=now;InvalidateVisual();
        Cursor=now is not null && RegionClicked is not null?new Cursor(StandardCursorType.Hand):null;
        ToolTip.SetTip(this,now is null?null:$"{now.Name}\n{now.Start:X5}〜{now.End-1:X5}  ({now.Size:X}h = {now.Size:N0} {L.T("バイト","bytes")})");
    }
    protected override void OnPointerExited(PointerEventArgs e){base.OnPointerExited(e);hover=null;InvalidateVisual();}
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);if(e.InitialPressMouseButton==MouseButton.Left && At(e.GetPosition(this)) is {} r)RegionClicked?.Invoke(r);}
}
