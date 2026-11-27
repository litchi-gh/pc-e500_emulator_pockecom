// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PcKey=Avalonia.Input.Key;
namespace PokeCom;

/// <summary>
/// Avalonia 版のメインウィンドウ。本体の絵、ツールバー、ステータスバーを持ち、画面の書き換えごとに
/// エミュレーションを進める。データのフォルダと設定ファイルは Windows 版と同じ。
/// </summary>
internal sealed class MainWindow : Window
{
    protected override Type StyleKeyOverride=>typeof(Window);
    Core? core;
    readonly PocketView surface=new();
    /// <summary>キーを押したままにしている元：PC のキーか、コントローラーの割り当て。</summary>
    readonly record struct HoldBy(PcKey Key,string? Binding=null);
    readonly KeyInput<HoldBy> input;
    readonly ProgramLoader loader;
    readonly ISound audio;
    readonly PrinterPaper paper=new();
    readonly LinePrinter printOut=new();
    readonly PersistentWriter writer=new();
    readonly short[] soundBuffer=new short[48000];
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly DispatcherTimer timer=new(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(8)};
    readonly string root,programRoot;
    /// <summary>試験用（--shot・--script）。設定と自動保存を書かない。</summary>
    readonly bool testing;
    HardwareSettings settings=new();
    bool paused,settling,lastOff,saveOwed,fault;
    double previous,lastSaved,frameAt;
    ulong heldAt;
    int speedIndex=1,frames;
    static readonly string[] Speeds=["0.5x","1x","2x","4x"];
    static readonly double[] Rates=[0.5,1,2,4];
    WindowView view=WindowView.Normal;
    Size normalSize;
    PixelPoint normalPosition;
    bool normalMaximized;
    readonly DockPanel frame=new();
    readonly WrapPanel bar=new(){Margin=new Thickness(10,6,10,4)};
    readonly StatusStrip status=new(){ShortOrder=[3,4,6,5,2,1,0]};
    readonly Dictionary<string,Window> windows=[];
    TextBlock? pauseText;
    Viewbox? pauseIcon;
    Button? updateNotice;
    Release? available;
    string? notice;
    double noticeUntil;

    /// <summary>設定ファイルのテーマ（読めなければシステムに合わせる）。</summary>
    internal static string SavedTheme() {
        try {return HardwareSettings.Load(Path.Combine(AppFolders.Data(AppFolders.Program()),"state","settings.json")).Theme;}
        catch(Exception) {return "System";}
    }
    internal MainWindow(string[] args) {
        programRoot=AppFolders.Program();root=AppFolders.Data(programRoot);
        string? Option(string name){int i=Array.IndexOf(args,name);return i>=0 && i+1<args.Length?args[i+1]:null;}
        string? shot=Option("--shot"),script=Option("--script");
        testing=shot is not null || script is not null;
        input=new(()=>core,()=>paused,surface.SetPressed);
        loader=new(()=>core,input,()=>paused,()=>settings.DiskFolder,Notify,Draw,async (title,question,cancel)=>
            cancel?await Ask.YesNoCancel(this,question,title):await Ask.YesNo(this,question,title)?ProgramLoader.Answer.Yes:ProgramLoader.Answer.No);
        string? problem=null,kept=null;
        if(AppFolders.IsInstalled(programRoot))AppFolders.Prepare(root);else Directory.CreateDirectory(Path.Combine(root,"rom"));
        if(!testing){settings=HardwareSettings.LoadOrReset(Path.Combine(root,"state","settings.json"),out problem,out kept);padConfig=PadConfig.Load(PadConfigPath);}
        L.Set(settings.Language.Length>0?settings.Language:L.Default);
        if(string.IsNullOrWhiteSpace(settings.DiskFolder))settings=settings with{DiskFolder=Path.Combine(root,"drive-x")};
        audio=ISound.Open();audio.Muted=settings.Muted;audio.Volume=settings.Volume/100f;
        surface.SetModel(settings.Appearance);surface.SetLcd(settings.LcdDotLines,settings.LcdContrast);surface.SetAfterimage(settings.LcdAfterimage);
        Title=TitleOf(settings.Appearance);FontFamily=Paint.Ui;FontSize=13;
        Background=Paint.Solid(Ui.Window);
        if(typeof(MainWindow).Assembly.GetManifestResourceStream("pc-e500.png") is {} icon)using(icon)Icon=new WindowIcon(icon);
        Width=1180;Height=725;MinWidth=640;MinHeight=420;
        // GNOME maximises a window that opens over most of the screen.
        // GNOME は、画面の大半を占めて開くウィンドウを最大化してしまう。
        if(Screens.Primary is {} screen) {
            Width=Math.Min(Width,Math.Floor(screen.WorkingArea.Width/screen.Scaling*0.86));
            Height=Math.Min(Height,Math.Floor(screen.WorkingArea.Height/screen.Scaling*0.86));
        }
        var commands=Commands();
        BuildToolbar(commands);
        DockPanel.SetDock(bar,Dock.Top);DockPanel.SetDock(status,Dock.Bottom);
        frame.Children.AddRange([bar,status,surface]);
        Content=frame;
        // Built afresh each time, so its checks and its pause item say what is so now.
        surface.ContextMenu=new ContextMenu();
        surface.ContextMenu.Opening+=(_,_)=>FillMenu(surface.ContextMenu,commands);
        surface.KeyClicked+=(name,modifiers)=>{
            if(core is null || paused)return;
            if(name=="RESET"){Try(ResetPin);return;}
            input.Click(name,modifiers.HasFlag(KeyModifiers.Control));
        };
        surface.BackgroundDoubleClicked+=CycleView;
        surface.BackgroundDragged+=e=>{if(view!=WindowView.Normal)BeginMoveDrag(e);};
        surface.EdgeDragged+=StartSizing;
        // A .BAS or .BIN dropped on the window loads as "Load program" does.
        DragDrop.SetAllowDrop(this,true);
        AddHandler(DragDrop.DragOverEvent,(_,e)=>e.DragEffects=e.DataTransfer.TryGetFiles() is not null?DragDropEffects.Copy:DragDropEffects.None);
        AddHandler(DragDrop.DropEvent,(_,e)=>{if(e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath() is string path){Activate();_=Load(path,settings.LoadRun);}});
        KeyDown+=OnPcKeyDown;KeyUp+=(_,e)=>{downKeys.Remove(e.Key);input.Unhold(new(e.Key));};TextInput+=OnPcText;
        Deactivated+=(_,_)=>{input.UnholdAll();downKeys.Clear();};
        Closing+=OnClosing;
        Closed+=(_,_)=>{timer.Stop();foreach(var w in windows.Values.ToArray())w.Close();audio.Dispose();printOut.Dispose();core?.Dispose();};
        paper.LineAdded+=line=>printOut.Line(line,core?.Font??[]);
        if(!testing && settings.PrintTo is not null)printOut.Select(settings.PrintTo);
        // The machine runs between frames too, for the keys and the sound.
        timer.Tick+=(_,_)=>{Advance();if(clock.Elapsed.TotalSeconds-frameAt>0.1)Draw();};
        Opened+=async (_,_)=>{
            surface.Focus();
            string? rom=args.FirstOrDefault(a=>!a.StartsWith('-') && a!=shot && a!=script)??StartRom();
            if(problem is not null)await Ask.Message(this,L.T("設定ファイルを読めなかったため、既定の設定で起動します。\n","The settings file could not be read, so the emulator starts with the default settings.\n")+problem+(kept is null?"":"\n"+kept));
            if(rom is not null)Try(()=>SwitchRom(rom,!testing));else Notify(L.T($"ROM を {Path.Combine(root,"rom")} に置くか、「ROMを開く」で選んでください。",$"Put a ROM in {Path.Combine(root,"rom")}, or choose one with Open ROM."));
            Topmost=settings.TopMost && !testing;
            timer.Start();NextFrame();
            if(!testing)await RestoreView();
            // Once a day at most, in the background; nothing is said if it fails.
            if(!testing && settings.CheckUpdates && DateTime.UtcNow-settings.UpdateCheckedAt>=TimeSpan.FromDays(1))_=CheckForUpdate(false);
            if(testing)_=RunScript(shot,script);
        };
    }
    static string TitleOf(string model)=>$"{model} Emulator v{UpdateCheck.Current} | PokeCom";

    // --- Commands ----------------------------------------------------------------
    sealed record Command(string Text,string Icon,Action Action,Func<bool>? Checked=null,bool Pause=false);
    Command[] Commands()=>[
        new(L.T("ROMを開く","Open ROM"),Icons.OpenFolder,()=>_=OpenRom()),new(L.T("プログラム読込","Load program"),Icons.Import,()=>_=LoadFile()),new(L.T("一時停止","Pause"),Icons.Pause,TogglePause,null,true),
        new(L.T("ステータス保存","Save state"),Icons.Save,()=>_=SaveState()),new(L.T("ステータス読込","Load state"),Icons.Load,()=>_=LoadState()),
        new(L.T("設定","Settings"),Icons.Settings,ShowSettings),new(L.T("画面","Display"),Icons.Display,ShowDisplay),new("CE-140F",Icons.Drive,ShowDisk),new("CE-126P",Icons.Print,ShowPrinter),
        new(L.T("文字入出力","Text I/O"),Icons.Text,ShowText),new(L.T("コントローラー","Controller"),Icons.Game,ShowPad),new(L.T("オールリセット","All Reset"),Icons.Reset,()=>Try(AllReset)),new(L.T("デバッグ","Debug"),Icons.Bug,ShowDebugger),new(L.T("使い方","Help"),Icons.Help,()=>Try(Help))];
    Button Tool(string text,string icon,Action action,out TextBlock label,out Viewbox picture) {
        label=new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center};picture=(Viewbox)Ui.Icon(icon,16,Ui.Accent);
        var button=new Button{Content=Ui.Row(picture,label),Focusable=false,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(9,6),Margin=new Thickness(0,0,2,0)};
        button.Click+=(_,_)=>action();return button;
    }
    void BuildToolbar(Command[] commands) {
        foreach(var c in commands) {
            bar.Children.Add(Tool(c.Text,c.Icon,c.Action,out var label,out var picture));
            if(c.Pause){pauseText=label;pauseIcon=picture;}
        }
        updateNotice=Tool("",Icons.Download,()=>_=OpenUpdate(),out _,out _);updateNotice.IsVisible=false;bar.Children.Add(updateNotice);
    }
    void ShowPause() {
        if(pauseText is null || pauseIcon is null)return;
        pauseText.Text=paused?L.T("再開","Resume"):L.T("一時停止","Pause");
        var now=(Viewbox)Ui.Icon(paused?Icons.Play:Icons.Pause,16,Ui.Accent);var child=now.Child;now.Child=null;pauseIcon.Child=child;
    }
    void FillMenu(ContextMenu menu,Command[] commands) {
        MenuItem Item(string text,string? icon,Action action,bool? isChecked=null) {
            var item=new MenuItem{Header=text};
            if(icon is not null)item.Icon=Ui.Icon(icon,15);
            if(isChecked is bool on){item.ToggleType=MenuItemToggleType.CheckBox;item.IsChecked=on;}
            item.Click+=(_,_)=>action();return item;
        }
        MenuItem Sub(string text,string icon,IEnumerable<MenuItem> items){var m=new MenuItem{Header=text,Icon=Ui.Icon(icon,15)};foreach(var i in items)m.Items.Add(i);return m;}
        menu.Items.Clear();
        if(available is not null)menu.Items.Add(Item(L.T($"新しいバージョン {available.Tag}",$"New version {available.Tag}"),Icons.Download,()=>_=OpenUpdate()));
        foreach(var c in commands)menu.Items.Add(c.Pause?Item(paused?L.T("再開","Resume"):c.Text,paused?Icons.Play:c.Icon,c.Action):Item(c.Text,c.Icon,c.Action));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(L.T("新しいバージョンを確認...","Check for a new version..."),Icons.Download,()=>_=CheckForUpdate(true)));
        menu.Items.Add(Item(L.T("機械語エリア...","Machine-code area..."),Icons.Code,()=>Try(ShowMachineCode)));
        menu.Items.Add(Item(L.T("データフォルダを開く","Open the data folder"),Icons.Folder,()=>Try(()=>Ui.Launch(root))));
        menu.Items.Add(Sub(L.T("速度","Speed"),Icons.Speed,Speeds.Select((s,i)=>Item(s,null,()=>SetSpeed(i),speedIndex==i))));
        menu.Items.Add(Sub(L.T("ウィンドウの表示","Window"),Icons.View,((WindowView[])[WindowView.Normal,WindowView.Body,WindowView.Screen]).Select(v=>Item(v switch{WindowView.Normal=>L.T("通常","Normal"),WindowView.Body=>L.T("本体だけ","Case only"),_=>L.T("画面だけ","Screen only")},null,()=>SetView(v),view==v))));
        menu.Items.Add(Item(L.T("常に手前に表示","Always on top"),null,()=>{Topmost=!Topmost;settings=settings with{TopMost=Topmost};SaveSettings();},Topmost));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(L.T("最小化","Minimize"),Icons.Minimize,()=>WindowState=WindowState.Minimized));
        menu.Items.Add(Item(L.T("終了","Exit"),Icons.Close,Close));
    }
    async void Try(Action action) {try{action();}catch(OperationCanceledException ex){Notify(ex.Message);}catch(Exception ex){await Ask.Message(this,ex.Message);}}
    async Task Try(Func<Task> action) {try{await action();}catch(OperationCanceledException ex){Notify(ex.Message);}catch(Exception ex){await Ask.Message(this,ex.Message);}}
    void SaveSettings(){if(testing)return;try{settings.Save(Path.Combine(root,"state","settings.json"));}catch(Exception ex){Notify(ex.Message);}}
    void Notify(string text){notice=text;noticeUntil=clock.Elapsed.TotalSeconds+5;statusKey=default;ShowStatus();}
    void SetSpeed(int index){speedIndex=index;audio.Flush();statusKey=default;}
    void TogglePause(){paused=!paused;ShowPause();ReleaseKeys();audio.Flush();Draw();}
    void ReleaseKeys() {
        downKeys.Clear();charRepeats=false;
        if(input.Queue.Count>0)Notify(L.T("入力の残り（貼り付けの途中など）を取り消しました。","The keys still to be typed (a paste) were dropped."));
        input.Clear();surface.ReleaseKeys();
        if(loader.Drop() is {} name)Notify(L.T($"{name} の読み込みをやめました（機械語エリアの確保が終わる前に中断しました）。",$"{name} was not loaded (stopped before the machine-code area was reserved)."));
    }
    /// <summary>RESET 穴：RAM を保存してから CPU を再起動する（RAM は消さない）。</summary>
    void ResetPin() {
        if(core is null)return;
        ReleaseKeys();SavePersistent();Native.e500_reset(core.Handle,0);audio.Flush();core.Run(1024000);fault=false;Draw();
    }
    /// <summary>オールリセット（実機の ON＋RESET）：状態を残してからリセットし、ROM の確認画面を出す。</summary>
    void AllReset() {
        if(core is null)return;
        if(!core.CanSnapshot && !core.Faulted)throw new InvalidOperationException(L.T("リセットの前に状態を保存します。","All Reset saves the state first. ")+Core.DiskBusy);
        ReleaseKeys();SavePersistent();
        if(!testing && core.CanSnapshot)core.Save(Path.Combine(root,"state","before-reset-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".e500state"));
        int on=Keyboard.Codes["ON/BRK"];
        Native.e500_reset(core.Handle,0);core.Key(on,true);core.Run(2048000);core.Key(on,false);
        audio.Flush();core.Run(1024000);paused=false;ShowPause();Draw();
        Notify(L.T("オールリセット: 画面のY／Nで、全データを消去するか選んでください。","All reset: answer Y/N on the screen to choose whether to erase all data."));
    }
    void Help() {
        string manual=Path.Combine(programRoot,"manual",L.En?"index_en.html":"index.html");
        if(File.Exists(manual))Ui.Launch(manual);else Ui.Launch("https://github.com/litchi-gh/pc-e500_emulator_pockecom");
    }
    Task OpenRom()=>Try(async ()=>{
        if(await Ui.OpenFile(this,Path.Combine(root,"rom"),Ui.Type("ROM","*.rom","*.bin")) is not string path)return;
        SwitchRom(path,!testing);settings=settings with{RomFile=core!.RomPath};SaveSettings();
    });
    Task LoadFile()=>Try(async ()=>{
        if(core is null)throw new InvalidOperationException(L.T("ROMが読み込まれていません。","No ROM is loaded."));
        if(await Ui.OpenFile(this,settings.DiskFolder,Ui.Type(L.T("BASIC・機械語","BASIC and machine code"),"*.bas","*.bin","*.txt")) is not string path)return;
        // The system's own file window has no room for the check box of the Windows program, so a program that can run is asked about after.
        bool run=false;
        if(!Path.GetExtension(path).Equals(".bin",StringComparison.OrdinalIgnoreCase)) {
            run=await Ask.YesNo(this,L.T($"{Path.GetFileName(path)} を読み込んだあと、実行しますか？",$"Run {Path.GetFileName(path)} after loading it?"),L.T("プログラム読込","Load program"));
            if(run!=settings.LoadRun){settings=settings with{LoadRun=run};SaveSettings();}
        }
        await Load(path,run);
    });
    Task Load(string path,bool run)=>Try(async ()=>Notify(await loader.Load(path,run)));
    static FilePickerFileType StateType=>Ui.Type(L.T("PC-E500シリーズ state","PC-E500 series state"),"*.e500state");
    Task SaveState()=>Try(async ()=>{
        if(core is null)return;
        ReleaseKeys();
        if(await Ui.SaveFile(this,null,settings.Appearance+".e500state","e500state",StateType) is string path){core.Save(path);Notify(L.T("保存しました: ","Saved: ")+path);}
    });
    Task LoadState()=>Try(async ()=>{
        if(core is null)return;
        ReleaseKeys();
        if(!core.CanLoad)throw new InvalidOperationException(Core.CannotLoad);
        if(await Ui.OpenFile(this,null,StateType) is not string path)return;
        SavePersistent();core.Load(path);
        if(settings.InternalKB!=core.InternalKB){settings=settings with{InternalKB=core.InternalKB};SaveSettings();}
        audio.Flush();surface.Redraw();Draw();
    });

    // --- Windows -------------------------------------------------------------------
    // Modeless, one of each: opened again, it comes to the front.
    T ShowWindow<T>(string key,Func<T> create) where T : Window {
        if(windows.TryGetValue(key,out var open)){if(open.WindowState==WindowState.Minimized)open.WindowState=WindowState.Normal;open.Activate();return (T)open;}
        var window=create();windows[key]=window;window.Closed+=(_,_)=>windows.Remove(key);
        // Not owned by the case-only and screen-only windows, or they would hide with it.
        window.Show(this);return window;
    }
    void ShowSettings() {
        Directory.CreateDirectory(Path.Combine(root,"ram"));
        var w=ShowWindow("settings",()=>new SettingsWindow(settings,core?.StandardRamKB??32,Path.Combine(root,"ram")){InUse=()=>core?.CardPath});
        w.Apply=async edit=>{try{ReleaseKeys();ApplySettings(edit(settings));return true;}catch(Exception ex){await Ask.Message(w,ex.Message);return false;}};
    }
    void ShowDisplay() {
        var w=ShowWindow("display",()=>new DisplayWindow());
        w.LcdChanged=SetLcd;w.AfterimageChanged=on=>{surface.SetAfterimage(on);settings=settings with{LcdAfterimage=on};SaveSettings();};w.ViewChanged=SetView;
        w.Sync(surface.RealLcd,surface.Contrast,view,settings.LcdAfterimage);
    }
    void SetLcd(bool real,int contrast){surface.SetLcd(real,contrast);settings=settings with{LcdDotLines=surface.RealLcd,LcdContrast=surface.Contrast};}
    void ShowDisk()=>ShowWindow("disk",()=>new DiskWindow(()=>core,()=>settings.DiskFolder));
    void ShowText()=>ShowWindow("text",()=>new TextWindow(()=>core?.ScreenText()??"",input.Paste));
    void ShowDebugger()=>ShowWindow("debug",()=>new DebugWindow(()=>core));
    void ShowPrinter()=>ShowWindow("printer",()=>new PrinterWindow(paper,()=>core?.Font??[],settings.PrinterConnected,printOut,
        to=>{printOut.Select(to);settings=settings with{PrintTo=to};SaveSettings();},
        on=>{settings=settings with{PrinterConnected=on};if(core is not null)core.PrinterConnected=on;Draw();SaveSettings();}));
    void ShowMachineCode() {
        if(core is null)throw new InvalidOperationException(L.T("ROMが読み込まれていません。","No ROM is loaded."));
        var area=MachineCodeArea.Read(core);int free=BasicImage.FreeBytes(core);
        if(area is not {} a || free<0)throw new InvalidOperationException(L.T("今の RAM からは機械語エリアを読めません（初期化の前など）。","The machine-code area cannot be read from the RAM now (before it is set up, say)."));
        var w=ShowWindow("machine-code",()=>new MachineCodeWindow(RamLayout.Internal(core),a.Size,MachineCodeArea.Largest(a.Size,free)));
        w.Apply=async size=>{try{Notify(loader.Reserve(size));return true;}catch(Exception ex){await Ask.Message(w,ex.Message,ProgramLoader.MachineCodeTitle);return false;}};
    }
    /// <summary>設定を適用する。RAM の構成が変われば ROM を開き直し、言語・テーマが変われば再起動する。</summary>
    void ApplySettings(HardwareSettings next) {
        if(next.DiskEnabled && string.IsNullOrWhiteSpace(next.DiskFolder))throw new InvalidDataException(L.T("CE-140Fの保存先を指定してください。","Specify the CE-140F folder."));
        bool memory=next.InternalKB!=settings.InternalKB || !string.Equals(next.CardPath,settings.CardPath,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
        if(core is not null && !core.CanSnapshot && !core.Faulted)throw new InvalidOperationException(L.T("設定を変える前に状態を保存します。","Settings save the state first. ")+Core.DiskBusy);
        // A folder or a card that cannot be opened refuses the settings and leaves the machine as it was.
        if(memory && core is not null)using(var trial=new Core(core.RomPath,next.InternalKB,next.CardPath))Mount(trial,next);
        if(!memory)SavePersistent();
        if(core is not null && !memory)Mount(core,next);
        bool restart=next.Language!=settings.Language || next.Theme!=settings.Theme;
        var before=settings;settings=next;
        if(restart && !testing) {
            SaveSettings();
            // The new one starts once this one has saved and let go of the machine's files.
            Closed+=(_,_)=>{if(Environment.ProcessPath is string self)Process.Start(new ProcessStartInfo(self){UseShellExecute=false});};
            Close();return;
        }
        if(memory && core is not null)try{SwitchRom(core.RomPath,!testing);}catch{settings=before;throw;}
        SaveSettings();
        bool body=view!=WindowView.Normal;double zoom=surface.Zoom;
        surface.SetModel(settings.Appearance);Title=TitleOf(settings.Appearance);
        if(body)_=Settle(zoom,null);
        Topmost=settings.TopMost;audio.Muted=settings.Muted;audio.Volume=settings.Volume/100f;
        statusKey=default;previous=clock.Elapsed.TotalSeconds;Draw();
    }
    static void Mount(Core target,HardwareSettings s)=>target.MountDisk(s.DiskEnabled?s.DiskFolder:null,s.DiskReadOnly);

    // --- A new version ---------------------------------------------------------------
    async Task CheckForUpdate(bool manual) {
        Release? release;
        try{release=await UpdateCheck.FetchAsync();}
        catch(Exception ex){if(manual)await Ask.Message(this,L.T("新しいバージョンを確認できませんでした。\n","Could not check for a new version.\n")+ex.Message);return;}
        settings=settings with{UpdateCheckedAt=DateTime.UtcNow};SaveSettings();
        if(release is null || !UpdateCheck.IsNewer(release)) {
            if(manual)await Ask.Message(this,L.T($"最新のバージョンです（{UpdateCheck.Current}）。",$"This is the latest version ({UpdateCheck.Current})."));
            return;
        }
        if(!manual && release.Tag==settings.SkippedUpdate)return;
        available=release;
        if(updateNotice?.Content is StackPanel row && row.Children[1] is TextBlock label){label.Text=L.T($"新しいバージョン {release.Tag}",$"New version {release.Tag}");updateNotice.IsVisible=true;}
        if(manual)await OpenUpdate();
    }
    async Task OpenUpdate() {
        if(available is null)return;
        var w=new UpdateWindow(available);await w.ShowDialog(this);
        if(!w.Skip)return;
        settings=settings with{SkippedUpdate=available.Tag};SaveSettings();available=null;updateNotice!.IsVisible=false;
    }

    // --- The machine ---------------------------------------------------------------
    string AutoSavePath(Core of)=>Path.Combine(root,"state",$"{of.Hash}.i{of.InternalKB}-c{of.CardKB}.e500state");
    string MemoryDirectory(Core of)=>Path.Combine(root,"state","memory",of.RomHash);
    /// <summary>起動時に開く ROM：最後に開いたもの、なければ rom フォルダから外観に合うもの。</summary>
    string? StartRom() {
        if(settings.RomFile.Length>0 && File.Exists(settings.RomFile))return settings.RomFile;
        string folder=Path.Combine(root,"rom");
        static long Bytes(string f){long n=new FileInfo(f).Length;return n==256*1024+16?256*1024:n;}
        var all=Directory.GetFiles(folder).Where(f=>Path.GetExtension(f).Equals(".rom",StringComparison.OrdinalIgnoreCase)).ToArray();
        bool e500=settings.Appearance=="PC-E500";long size=e500?256*1024:384*1024;
        string[] wanted=e500?["E500","E550","1480","1490"]:["E650","U6000"];
        return wanted.Select(w=>all.Where(f=>Bytes(f)==size && Path.GetFileName(f).Contains(w,StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Path.GetFileName,StringComparer.OrdinalIgnoreCase).FirstOrDefault()).FirstOrDefault(f=>f is not null)??(all.Length==1?all[0]:null);
    }
    List<PersistentWriter.Item> PersistentItems() {
        var items=new List<PersistentWriter.Item>{core!.InternalItem(MemoryDirectory(core))};
        if(core.CardItem() is {} card)items.Add(card);
        if(core.CanSnapshot)items.Add(new(AutoSavePath(core),core.Snapshot(),core.Hash));
        return items;
    }
    void SavePersistent(){if(core is not null && !testing)writer.WriteNow(PersistentItems());}
    void SwitchRom(string path,bool restore) {
        Core next=new(path,settings.InternalKB,settings.CardPath.Length>0?settings.CardPath:null);
        try {
            if(core is not null){ReleaseKeys();SavePersistent();}
            if(restore)next.MemoryFiles(MemoryDirectory(next),true);
        } catch {next.Dispose();throw;}
        core?.Dispose();core=next;surface.Core=core;audio.Flush();
        core.PrinterConnected=settings.PrinterConnected;core.Paper=paper;
        if(settings.InternalKB!=core.InternalKB){settings=settings with{InternalKB=core.InternalKB};SaveSettings();}
        if(windows.TryGetValue("settings",out var open) && open is SettingsWindow s)s.SetRom(core.StandardRamKB,settings.InternalKB);
        paused=fault=false;ShowPause();statusKey=default;
        // The drive goes in before the snapshot, which may hold its place in a device code.
        Exception? mount=null;
        if(!testing)try{Mount(core,settings);}catch(Exception ex){mount=ex;}
        if(restore) {
            string snapshot=AutoSavePath(core);
            string?[] banks=[core.InternalFile(MemoryDirectory(core)),core.CardPath];
            var newest=banks.Where(File.Exists).Select(f=>File.GetLastWriteTimeUtc(f!)).DefaultIfEmpty(DateTime.MinValue).Max();
            if(File.Exists(snapshot) && File.GetLastWriteTimeUtc(snapshot)>=newest)
                try{core.Load(snapshot);}catch(Exception ex){_=Ask.Message(this,L.T("状態を復元できなかったため、保存RAMから起動します。\n","Could not restore the snapshot, starting from the saved RAM.\n")+ex.Message);}
        }
        core.Run(1024000);
        Native.e500_audio(core.Handle,soundBuffer,soundBuffer.Length);previous=clock.Elapsed.TotalSeconds;
        surface.Redraw();Draw();
        if(mount is not null)_=Ask.Message(this,L.T("CE-140Fを接続できないため、外した状態で続けます。\n","The CE-140F could not be connected, and stays disconnected.\n")+mount.Message);
    }
    void NextFrame()=>RequestAnimationFrame(_=>{if(!IsVisible)return;Advance();Draw();NextFrame();});
    /// <summary>経過時間の分だけ本体を進め、音声と自動保存を行う。</summary>
    void Advance() {
        double now=clock.Elapsed.TotalSeconds,elapsed=Math.Clamp(now-previous,0,0.1);previous=now;
        if(core is null || paused)return;
        try {
            PollPad(now);
            // At 1x the pace follows the sound, so nothing buffered for it is ever cut (AudioPace).
            double rate=Rates[speedIndex];if(rate==1)rate=audio.Pace();
            input.Run((int)(elapsed*KeyInput<HoldBy>.CpuHz*rate));
            loader.Finish();
            audio.Submit(soundBuffer,Native.e500_audio(core.Handle,soundBuffer,soundBuffer.Length));
            bool off=(Native.e500_status(core.Handle)&1)!=0;
            // A round that finds the last write still going is owed, and tried again on the next tick.
            if(!testing && (saveOwed || now-lastSaved>=5 || (off && !lastOff))){saveOwed=writer.Busy || !writer.Start(PersistentItems());if(!saveOwed)lastSaved=now;}
            lastOff=off;
            if(!settings.AutoPowerOff && core.Cycles-heldAt>=KeyInput<HoldBy>.CpuHz){heldAt=core.Cycles;core.HoldPower();}
        } catch(Exception ex) {
            // A fault stops the machine with one message, not one a frame.
            paused=true;ShowPause();ReleaseKeys();
            if(!fault){fault=true;_=Ask.Message(this,ex.Message,L.T("実行停止","Execution stopped"));}
        }
    }
    /// <summary>LCD とステータスバーを、変わっていれば描き直す。</summary>
    void Draw() {
        frameAt=clock.Elapsed.TotalSeconds;frames++;
        if(core is not null){core.Refresh();settling=surface.UpdateLcd();}
        ShowStatus();
    }
    readonly record struct StatusKey(Core? Core,int Status,int Speed,int MachineCode,bool Paused,int Disk,bool Printer,string? Audio,bool Muted,Exception? Saving,string? Notice,int InternalKB,string? Card,string? Pad);
    StatusKey statusKey;
    static string Bytes(int n)=>n%1024==0?$"{n/1024}KB":$"{n}B";
    void ShowStatus() {
        if(notice is not null && clock.Elapsed.TotalSeconds>=noticeUntil)notice=null;
        var ml=core is null?null:MachineCodeArea.Read(core);
        string? pad=!padConfig.Enabled?null:padDevice is not null?L.T("コントローラー 接続中","Pad connected"):L.T("コントローラー 未接続","Pad not connected");
        var key=new StatusKey(core,core is null?0:Native.e500_status(core.Handle),speedIndex,ml?.Start??-1,paused,core is null?0:Native.e500_disk_status(core.Handle),core?.PrinterConnected==true,audio.Error,settings.Muted,writer.Failure,notice,core?.InternalKB??0,core?.CardPath,pad);
        if(key==statusKey)return;
        statusKey=key;
        bool off=(key.Status&1)!=0;
        var state=core is null?new StatusStrip.Part("",L.T("ROMなし","No ROM"))
            :off?new StatusStrip.Part("",L.T("電源OFF（EscでON）","Power OFF (Esc turns ON)"),Color.FromRgb(140,140,140))
            :paused?new StatusStrip.Part("",L.T("一時停止","Paused"),Color.FromRgb(232,160,0),TogglePause,L.T("押すと再開します","Click to resume"))
            :new StatusStrip.Part("",L.T("実行中","Running"),Color.FromRgb(16,160,60),TogglePause,L.T("押すと一時停止します","Click to pause"));
        var message=notice is not null?new StatusStrip.Part(Icons.Info,notice):writer.Failure is {} failed?new StatusStrip.Part(Icons.Warning,L.T("自動保存できません: ","Autosave failed: ")+failed.Message,Warn:true):null;
        if(core is null){status.SetParts(state,message,[]);return;}
        string card=core.HasCard?$"+{core.CardKB}KB":"",family=core.IsE500S?"E500S":core.IsE650Family?"E650":"E500";
        int d=key.Disk;
        string disk=(d&1)==0?L.T("未接続","off"):(d&8)!=0?L.T("エラー","error"):(d&4)!=0?L.T("通信中","busy"):(d&2)!=0?L.T("読取専用","read-only"):L.T("接続","on");
        string on=L.T("接続","on"),none=L.T("未接続","off");
        status.ShortOrder=pad is null?[3,4,6,5,2,1,0]:[3,4,6,7,5,2,1,0];
        var fields=new List<StatusStrip.Part> {
            new(Icons.Chip,$"ROM Ver.{core.RomVersion} ({family})",Tip:$"{core.RomKB}KB ({family})\n{core.RomPath}",Short:$"Ver.{core.RomVersion}"),
            new(Icons.Memory,$"RAM {core.InternalKB}KB{card}",Click:ShowSettings,Tip:L.T($"内蔵RAM {core.InternalKB}KB / ",$"Internal RAM {core.InternalKB}KB / ")+(core.HasCard?L.T($"RAMカード {core.CardKB}KB ({core.CardName})",$"RAM card {core.CardKB}KB ({core.CardName})"):L.T("RAMカードなし","no RAM card"))+L.T("\n押すと「設定」を開きます","\nClick to open Settings"),Short:$"{core.InternalKB}KB{card}"),
            new(Icons.Code,ml is not {Size:>0} m?L.T("機械語 なし","ML none"):L.T($"機械語 {Bytes(m.Size)}",$"ML {Bytes(m.Size)}"),Click:()=>Try(ShowMachineCode),Tip:L.T("押すと大きさを変えられます","Click to change its size"),Short:ml is not {Size:>0} s0?L.T("なし","none"):Bytes(s0.Size)),
            new(Icons.Drive,$"CE-140F {disk}",Click:ShowDisk,Tip:L.T("押すと「CE-140F」を開きます","Click to open the CE-140F window"),Warn:(d&1)!=0 && (d&8)!=0,Short:disk),
            new(Icons.Print,$"CE-126P {(core.PrinterConnected?on:none)}",Click:ShowPrinter,Tip:L.T("押すと「CE-126P」を開きます","Click to open the CE-126P window"),Short:core.PrinterConnected?on:none),
            new(settings.Muted || audio.Error is not null?Icons.Mute:Icons.Volume,audio.Error??(settings.Muted?L.T("消音","Muted"):L.T("音声ON","Sound on")),Click:()=>{settings=settings with{Muted=!settings.Muted};audio.Muted=settings.Muted;SaveSettings();},
                Tip:settings.Muted?L.T("押すと音を出します","Click to unmute"):L.T("押すと消音します","Click to mute"),Warn:audio.Error is not null,Short:audio.Error is null && !settings.Muted?"ON":null)};
        if(pad is not null)fields.Add(new(Icons.Game,pad,Click:ShowPad,Tip:(padDevice is not null?padDevice.Name+L.T($"\nプリセット: {padConfig.Current.Name}",$"\nPreset: {padConfig.Current.Name}"):L.T("コントローラーが見つかりません","No controller found"))+L.T("\n押すと「コントローラー」を開きます","\nClick to open the Controller window"),Short:padDevice is not null?L.T("接続中","on"):none));
        fields.Add(new(Icons.Speed,L.T("速度 ","Speed ")+Speeds[speedIndex],Click:()=>SetSpeed((speedIndex+1)%Speeds.Length),Tip:L.T("押すと速度を切り替えます（0.5x → 1x → 2x → 4x）","Click for the next speed (0.5x → 1x → 2x → 4x)"),Short:Speeds[speedIndex]));
        status.SetParts(state,message,fields);
    }
    bool closing;
    async void OnClosing(object? sender,WindowClosingEventArgs e) {
        if(core is null || testing || closing)return;
        try {
            ReleaseKeys();SavePersistent();if(view!=WindowView.Normal)RememberView();settings.Save(Path.Combine(root,"state","settings.json"));
            printOut.Flush(15000);
        } catch(Exception ex) {
            e.Cancel=true;
            if(await Ask.YesNo(this,L.T("状態を保存できませんでした。終了しますか？\n","Could not save the state. Quit anyway?\n")+ex.Message,"PC-E500")){closing=true;Close();}
        }
    }

    // --- The controller --------------------------------------------------------------
    PadConfig padConfig=PadConfig.Defaults();
    PadDevice? padDevice;
    double padScanAt;
    Task<List<PadDevice>>? padScan;
    readonly HashSet<string> padPressed=[],padWanted=[];
    string PadConfigPath=>Path.Combine(root,"state","controller.json");
    void ShowPad() {
        var w=ShowWindow("controller",()=>new PadWindow(padConfig));
        w.Apply=async next=>{
            try {
                if(padConfig.Unread && File.Exists(PadConfigPath)){await Ask.Message(w,L.T("起動時に controller.json を読めなかったため、上書きせずに残しています。読めるようになってからエミュレータを起動し直してください。","controller.json could not be read at start, so it is kept as it is. Start the emulator again once it can be read."),w.Title!);return false;}
                if(!testing)next.Save(PadConfigPath);
                ReleasePad();padConfig=next;padDevice=null;padScanAt=0;statusKey=default;return true;
            } catch(Exception ex) {await Ask.Message(w,ex.Message,w.Title!);return false;}
        };
    }
    // Looked for every three seconds while none is found, off this thread; read on every tick while the window has the focus.
    void PollPad(double now) {
        if(testing)return;
        if(!padConfig.Enabled){ReleasePad();return;}
        if(padScan is {IsCompleted:true}) {
            var found=GamePad.Pick(padScan.IsCompletedSuccessfully?padScan.Result:[],padConfig.Device);padScan=null;padScanAt=now+3;
            if(found?.Id!=padDevice?.Id || found?.Index!=padDevice?.Index){ReleasePad();padDevice=found;statusKey=default;}
        }
        if(padScan is null && padDevice is null && now>=padScanAt)padScan=Task.Run(GamePad.Scan);
        if(padDevice is null)return;
        if(!GamePad.Read(padDevice,padPressed)){padDevice=null;padScanAt=now+1;ReleasePad();statusKey=default;return;}
        if(!IsActive){ReleasePad();return;}
        padWanted.Clear();
        foreach(var c in padPressed)if(padConfig.Current.Map.TryGetValue(c,out string? b))padWanted.Add(b);
        foreach(var by in input.Holding.Keys.Where(h=>h.Binding is not null && !padWanted.Contains(h.Binding)).ToArray())input.Unhold(by);
        foreach(var b in padWanted)if(PadConfig.Parse(b,out int code,out int modifier))input.Hold(new(PcKey.None,b),code,modifier);
    }
    void ReleasePad(){foreach(var by in input.Holding.Keys.Where(h=>h.Binding is not null).ToArray())input.Unhold(by);}

    // --- The PC's keyboard -----------------------------------------------------------
    static readonly Dictionary<PcKey,string> Special=new() {
        [PcKey.Enter]="ENTER",[PcKey.Back]="BS",[PcKey.Delete]="DEL",[PcKey.Insert]="INS",
        [PcKey.Left]="LEFT",[PcKey.Right]="RIGHT",[PcKey.Up]="UP",[PcKey.Down]="DOWN",
        [PcKey.Escape]="ON/BRK",[PcKey.F1]="F1",[PcKey.F2]="F2",[PcKey.F3]="F3",[PcKey.F4]="F4",[PcKey.F5]="F5",
        [PcKey.F6]="MENU",[PcKey.F7]="BASIC",[PcKey.F8]="2ndF"
    };
    readonly HashSet<PcKey> downKeys=[];
    PcKey lastDown;
    bool charRepeats;
    void OnPcKeyDown(object? sender,KeyEventArgs e) {
        if(e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))return;
        charRepeats=!downKeys.Add(e.Key);
        if(!charRepeats)lastDown=e.Key;
        bool ctrl=e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if(ctrl && (e.Key is PcKey.V or PcKey.C or PcKey.D)) {
            e.Handled=true;if(charRepeats)return;
            if(e.Key==PcKey.V)_=PasteClipboard();else if(e.Key==PcKey.C)ShowText();else ShowDebugger();
            return;
        }
        if(Special.TryGetValue(e.Key,out string? name)) {
            e.Handled=true;
            if(!charRepeats)input.Hold(new(e.Key),Keyboard.Codes[name],ctrl?Keyboard.Codes["CTRL"]:e.KeyModifiers.HasFlag(KeyModifiers.Shift)?Keyboard.Codes["SHIFT"]:-1);
            return;
        }
        // Ctrl with a letter gives no text: the letter, with CTRL.
        if(ctrl && e.Key>=PcKey.A && e.Key<=PcKey.Z) {
            e.Handled=true;
            if(!charRepeats)input.Hold(new(e.Key),Keyboard.Codes[((char)('A'+(e.Key-PcKey.A))).ToString()],Keyboard.Codes["CTRL"]);
        }
    }
    void OnPcText(object? sender,TextInputEventArgs e) {
        if(e.Text is not {Length:>0} text)return;
        e.Handled=true;
        if(text.Length==1 && downKeys.Contains(lastDown)) {
            // The key last pressed holds its character until it comes up; its repeats are not used.
            if(charRepeats || char.IsControl(text[0]))return;
            if(KeyInput<HoldBy>.CharacterKey(text[0],out int code,out int modifier))input.Hold(new(lastDown),code,modifier);
            else Unsupported(text);
            return;
        }
        foreach(char c in text)if(!char.IsControl(c) && !input.Character(c))Unsupported(c.ToString());
    }
    void Unsupported(string text)=>Notify(L.T($"未対応のPC文字: {text}（画面のSHIFTとポケコンのキーで入力してください）",$"Unsupported PC character: {text} (use the on-screen SHIFT and pocket computer keys)"));
    async Task PasteClipboard() {
        try {
            string? text=Clipboard is null?null:await Clipboard.TryGetTextAsync();
            Notify(string.IsNullOrEmpty(text)?L.T("クリップボードに文字列がありません。","The clipboard has no text."):input.Paste(text));
        } catch(Exception ex) {Notify(ex.Message);}
    }

    // --- The views -------------------------------------------------------------------
    const double MinZoom=0.75,MaxZoom=8;
    Size ViewSize(double zoom){zoom=Math.Clamp(zoom,MinZoom,MaxZoom);var s=surface.ShownSize;return new Size(Math.Ceiling(s.Width*zoom)+surface.Border,Math.Ceiling(s.Height*zoom)+surface.Border);}
    void CycleView()=>SetView(view switch{WindowView.Normal=>WindowView.Body,WindowView.Body=>WindowView.Screen,_=>WindowView.Normal});
    /// <summary>ウィンドウの表示を変える。絵の大きさと、画面上の LCD の位置は変えず、ウィンドウがそれを囲む。</summary>
    void SetView(WindowView next) {
        if(next==view)return;
        var anchor=surface.PointToScreen(surface.LcdArea.TopLeft);double zoom=surface.Zoom;
        if(view==WindowView.Normal) {
            normalMaximized=WindowState==WindowState.Maximized;
            if(normalMaximized)WindowState=WindowState.Normal;
            normalSize=ClientSize;normalPosition=Position;
        }
        view=next;
        bool normal=next==WindowView.Normal;
        surface.SetScreenOnly(next==WindowView.Screen);surface.Resizable=!normal;
        bar.IsVisible=status.IsVisible=normal;
        // The case's corners are see-through, by the compositor rather than a colour.
        TransparencyLevelHint=next==WindowView.Body?[WindowTransparencyLevel.Transparent]:[];
        Background=next==WindowView.Body?Brushes.Transparent:Paint.Solid(Ui.Window);
        WindowDecorations=normal?WindowDecorations.Full:WindowDecorations.None;
        MinWidth=normal?640:0;MinHeight=normal?420:0;
        if(normal) {
            Width=normalSize.Width;Height=normalSize.Height;Position=normalPosition;
            if(normalMaximized)WindowState=WindowState.Maximized;
            RememberView();SaveSettings();
        } else _=Settle(zoom,anchor);
        if(windows.TryGetValue("display",out var open) && open is DisplayWindow d)d.Sync(surface.RealLcd,surface.Contrast,view,settings.LcdAfterimage);
        // Without or with its frame the window is a new one to the window manager, which may put it behind.
        Activate();surface.Focus();
    }
    // The window manager takes the frame off and moves the window in its own
    // time, so where the LCD came out is looked at again until it stays.
    /// <summary>枠のない表示の大きさにして、LCD を画面の元の位置に置く（ウィンドウの変更が落ち着くまで、何度か合わせ直す）。</summary>
    /// <param name="anchor">LCD の左上を置く画面の位置（null なら位置は変えない）</param>
    async Task Settle(double zoom,PixelPoint? anchor) {
        int mine=++settleNumber;
        foreach(int wait in (int[])[0,60,120,200,300]) {
            if(wait>0)await Task.Delay(wait);
            if(mine!=settleNumber || view==WindowView.Normal)return;
            if(WindowState!=WindowState.Normal)WindowState=WindowState.Normal;
            var size=ViewSize(zoom);
            if(ClientSize!=size){Width=size.Width;Height=size.Height;continue;}
            var now=surface.PointToScreen(surface.LcdArea.TopLeft);
            if(anchor is {} to && now!=to)Position=new PixelPoint(Position.X+to.X-now.X,Position.Y+to.Y-now.Y);
        }
        RememberView();SaveSettings();
    }
    int settleNumber;
    void RememberView() {
        settings=view==WindowView.Normal?settings with{View=view}:settings with{View=view,ViewZoom=(float)surface.Zoom,ViewLeft=Position.X,ViewTop=Position.Y};
    }
    /// <summary>保存してあるウィンドウの表示に戻す（位置がどの画面にも入らなければ、位置だけは戻さない）。</summary>
    async Task RestoreView() {
        var s=settings;
        if(s.View==WindowView.Normal)return;
        // The window is on the screen, and where the window manager puts it, only a while after it opens.
        await Task.Delay(200);
        SetView(s.View);
        await Settle(s.ViewZoom>0?s.ViewZoom:surface.Zoom,null);
        var at=new PixelPoint(s.ViewLeft,s.ViewTop);
        if(view==s.View && Screens.All.Any(screen=>screen.WorkingArea.Contains(new PixelPoint(at.X+40,at.Y+40)))){Position=at;settings=settings with{ViewLeft=at.X,ViewTop=at.Y};SaveSettings();}
    }
    // The system's own sizing would stretch the window to any shape; here the
    // edge dragged sets the zoom and the other side stays where it is.
    /// <summary>枠のない表示で、絵の端をつかんで大きさを変える（縦横の比を保つ）。</summary>
    void StartSizing(WindowEdge edge,PointerPressedEventArgs e) {
        var from=surface.PointToScreen(e.GetPosition(surface));var startSize=ClientSize;var startAt=Position;double scaling=RenderScaling;
        e.Pointer.Capture(surface);
        void Move(object? sender,PointerEventArgs m) {
            var now=surface.PointToScreen(m.GetPosition(surface));
            double dx=(now.X-from.X)/scaling,dy=(now.Y-from.Y)/scaling;
            bool west=edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest,north=edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast;
            var shown=surface.ShownSize;double border=surface.Border;
            double zoom=edge is WindowEdge.North or WindowEdge.South?(startSize.Height+(north?-dy:dy)-border)/shown.Height:(startSize.Width+(west?-dx:dx)-border)/shown.Width;
            var size=ViewSize(zoom);
            Width=size.Width;Height=size.Height;
            Position=new PixelPoint(west?startAt.X+(int)Math.Round((startSize.Width-size.Width)*scaling):startAt.X,north?startAt.Y+(int)Math.Round((startSize.Height-size.Height)*scaling):startAt.Y);
        }
        void End(object? sender,PointerReleasedEventArgs r) {
            surface.PointerMoved-=Move;surface.PointerReleased-=End;e.Pointer.Capture(null);RememberView();SaveSettings();
        }
        surface.PointerMoved+=Move;surface.PointerReleased+=End;
    }

    // --- Trying it out -----------------------------------------------------------------
    // One command a line: wait 秒, key NAME, type text, enter, view normal|body|screen,
    // real, open 窓, shot file, top, info, load file. Used to look at the program on a machine with no hands at it.
    /// <summary>試験用：スクリプトを1行ずつ実行して終わる（スクリプトがなければ3秒後の絵を保存する）。</summary>
    async Task RunScript(string? shot,string? script) {
        string[] lines=script is null?["wait 3",$"shot {shot}"]:File.ReadAllLines(script);
        try {
            foreach(string line in lines) {
                string[] part=line.Trim().Split(' ',2);string rest=part.Length>1?part[1]:"";
                switch(part[0]) {
                case "wait":await Task.Delay(TimeSpan.FromSeconds(double.Parse(rest,CultureInfo.InvariantCulture)));break;
                case "key":input.Tap(Keyboard.Codes[rest]);break;
                case "type":input.Paste(rest);break;
                case "enter":input.Tap(Keyboard.Codes["ENTER"]);break;
                case "view":SetView(Enum.Parse<WindowView>(rest,true));break;
                case "real":SetLcd(!surface.RealLcd,surface.Contrast);break;
                case "model":surface.SetModel(rest);break;
                case "top":Topmost=!Topmost;break;
                case "press":surface.SetPressed(rest,true);break;
                case "disk":core?.MountDisk(rest,false);settings=settings with{DiskFolder=rest};break;
                case "load":Console.WriteLine(await loader.Load(rest,false));break;
                case "run":Console.WriteLine(await loader.Load(rest,true));break;
                case "reserve":Console.WriteLine(loader.Reserve(int.Parse(rest,NumberStyles.HexNumber)));break;
                case "reset":AllReset();break;
                case "pause":TogglePause();break;
                case "save":core?.Save(rest);break;
                case "restore":core?.Load(rest);break;
                case "open":switch(rest){case "settings":ShowSettings();break;case "display":ShowDisplay();break;case "disk":ShowDisk();break;case "printer":ShowPrinter();break;case "text":ShowText();break;case "pad":ShowPad();break;case "debug":ShowDebugger();break;case "code":ShowMachineCode();break;
                    case "update":windows["update"]=new UpdateWindow(new Release(new Version(9,9),"v9.9","Test",UpdateCheck.Releases,"- A\n- B"));windows["update"].Show(this);break;}break;
                case "shot": {
                    // "shot file [window]": the main window, or one opened, as drawn.
                    var parts=rest.Split(' ');Window w=parts.Length>1?windows[parts[1]]:this;
                    var size=new PixelSize((int)(w.ClientSize.Width*w.RenderScaling),(int)(w.ClientSize.Height*w.RenderScaling));
                    using var bitmap=new RenderTargetBitmap(size,new Vector(96*w.RenderScaling,96*w.RenderScaling));
                    bitmap.Render(w);bitmap.Save(parts[0],PngBitmapEncoderOptions.Default);break;
                }
                case "info":
                    Console.WriteLine($"view={view} position={Position} size={ClientSize} scaling={RenderScaling} topmost={Topmost} transparency={ActualTransparencyLevel} frames={frames} at {clock.Elapsed.TotalSeconds:F1}s lcd={surface.LcdRepaints} sound={audio.Error??"ok"} {(audio is AlsaSound a?$"delivered={a.Delivered} underruns={a.Underruns}":"")}");
                    Console.WriteLine("status: "+status.Text);
                    break;
                case "pads":foreach(var d in GamePad.Scan()){GamePad.Read(d,padPressed);Console.WriteLine($"{d.Id} {d.Name} {d.Kind} pressed={string.Join(",",padPressed)}");}break;
                case "printed":Console.WriteLine(paper.Text());break;
                }
            }
            if(core is not null)Console.WriteLine(core.ScreenText());
        } catch(Exception ex) {Console.Error.WriteLine(ex);Environment.ExitCode=1;}
        Close();
    }
}
