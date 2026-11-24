// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
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
    Core? core;
    readonly PocketView surface=new();
    readonly KeyInput<PcKey> input;
    readonly ISound? audio;
    readonly PrinterPaper paper=new();
    readonly PersistentWriter writer=new();
    readonly short[] soundBuffer=new short[48000];
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly DispatcherTimer timer=new(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(8)};
    readonly string root,programRoot;
    /// <summary>試験用（--shot・--script）。設定と自動保存を書かない。</summary>
    readonly bool testing;
    HardwareSettings settings=new();
    bool paused,settling,lastOff,saveOwed;
    double previous,lastSaved,frameAt;
    int speedIndex=1;
    static readonly double[] Rates=[0.5,1,2,4];
    WindowView view=WindowView.Normal;
    Size normalSize;
    PixelPoint normalPosition;
    bool normalMaximized;
    readonly DockPanel frame=new();
    readonly WrapPanel bar=new(){Margin=new Thickness(8,6,8,4)};
    readonly TextBlock status=new(){Margin=new Thickness(10,4,10,5),FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis};
    readonly Border statusBar=new();
    readonly Button pause=new();
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
        string? problem=null,kept=null;
        if(!testing){settings=HardwareSettings.LoadOrReset(Path.Combine(root,"state","settings.json"),out problem,out kept);}
        L.Set(settings.Language.Length>0?settings.Language:L.Default);
        if(string.IsNullOrWhiteSpace(settings.DiskFolder))settings=settings with{DiskFolder=Path.Combine(root,"drive-x")};
        audio=ISound.Open();audio.Muted=settings.Muted;audio.Volume=settings.Volume/100f;
        surface.SetModel(settings.Appearance);surface.SetLcd(settings.LcdDotLines,settings.LcdContrast);surface.SetAfterimage(settings.LcdAfterimage);
        Title=$"{settings.Appearance} Emulator (Avalonia) | PokeCom";
        Width=1180;Height=725;MinWidth=500;MinHeight=320;
        // GNOME maximises a window that opens over most of the screen.
        // GNOME は、画面の大半を占めて開くウィンドウを最大化してしまう。
        if(Screens.Primary is {} screen) {
            Width=Math.Min(Width,Math.Floor(screen.WorkingArea.Width/screen.Scaling*0.86));
            Height=Math.Min(Height,Math.Floor(screen.WorkingArea.Height/screen.Scaling*0.86));
        }
        FontFamily=Paint.Ui;
        BuildToolbar();
        DockPanel.SetDock(bar,Dock.Top);DockPanel.SetDock(statusBar,Dock.Bottom);
        statusBar.Child=status;statusBar.BorderThickness=new Thickness(0,1,0,0);statusBar.BorderBrush=new SolidColorBrush(Color.FromArgb(40,128,128,128));
        frame.Children.AddRange([bar,statusBar,surface]);
        Content=frame;
        surface.ContextMenu=BuildMenu();
        surface.KeyClicked+=(name,modifiers)=>{
            if(core is null || paused)return;
            if(name=="RESET"){Try(ResetPin);return;}
            input.Click(name,modifiers.HasFlag(KeyModifiers.Control));
        };
        surface.BackgroundDoubleClicked+=()=>SetView(view switch{WindowView.Normal=>WindowView.Body,WindowView.Body=>WindowView.Screen,_=>WindowView.Normal});
        surface.BackgroundDragged+=e=>{if(view!=WindowView.Normal)BeginMoveDrag(e);};
        surface.EdgeDragged+=StartSizing;
        KeyDown+=OnPcKeyDown;KeyUp+=(_,e)=>{downKeys.Remove(e.Key);input.Unhold(e.Key);};TextInput+=OnPcText;
        Deactivated+=(_,_)=>{input.UnholdAll();downKeys.Clear();};
        Closing+=OnClosing;
        Closed+=(_,_)=>{timer.Stop();audio?.Dispose();core?.Dispose();};
        // The machine runs between frames too, for the keys and the sound.
        timer.Tick+=(_,_)=>{Advance();if(clock.Elapsed.TotalSeconds-frameAt>0.1)Draw();};
        Opened+=async (_,_)=>{
            surface.Focus();
            string? rom=args.FirstOrDefault(a=>!a.StartsWith('-') && a!=shot && a!=script)??StartRom();
            if(problem is not null)await Message(L.T("設定ファイルを読めなかったため、既定の設定で起動します。\n","The settings file could not be read, so the emulator starts with the default settings.\n")+problem+(kept is null?"":"\n"+kept));
            if(rom is not null)Try(()=>SwitchRom(rom,!testing));else Notify(L.T($"ROM を {Path.Combine(root,"rom")} に置くか、「ROMを開く」で選んでください。",$"Put a ROM in {Path.Combine(root,"rom")}, or choose one with Open ROM."));
            Topmost=settings.TopMost && !testing;
            timer.Start();NextFrame();
            if(!testing)await RestoreView();
            if(testing)_=RunScript(shot,script);
        };
        Directory.CreateDirectory(Path.Combine(root,"rom"));
    }

    // --- Commands ----------------------------------------------------------------
    static Button Tool(string text,Action action) {
        var button=new Button{Content=text,Margin=new Thickness(2),Focusable=false,FontSize=13,Padding=new Thickness(10,5)};
        button.Click+=(_,_)=>action();return button;
    }
    void BuildToolbar() {
        pause.Content=L.T("一時停止","Pause");pause.Margin=new Thickness(2);pause.Focusable=false;pause.FontSize=13;pause.Padding=new Thickness(10,5);
        pause.Click+=(_,_)=>TogglePause();
        bar.Children.AddRange([
            Tool(L.T("ROMを開く","Open ROM"),()=>_=OpenRom()),pause,
            Tool(L.T("ステータス保存","Save state"),()=>_=SaveState()),Tool(L.T("ステータス読込","Load state"),()=>_=LoadState()),
            Tool(L.T("表示切替","View"),()=>SetView(view switch{WindowView.Normal=>WindowView.Body,WindowView.Body=>WindowView.Screen,_=>WindowView.Normal})),
            Tool(L.T("リアル表示","Real LCD"),()=>SetLcd(!surface.RealLcd,surface.Contrast)),
        ]);
    }
    ContextMenu BuildMenu() {
        var menu=new ContextMenu();
        MenuItem Item(string text,Action action,Func<bool>? isChecked=null) {
            var item=new MenuItem{Header=text,ToggleType=isChecked is null?MenuItemToggleType.None:MenuItemToggleType.CheckBox};
            item.Click+=(_,_)=>action();
            if(isChecked is not null)menu.Opening+=(_,_)=>item.IsChecked=isChecked();
            return item;
        }
        MenuItem Sub(string text,params MenuItem[] items){var m=new MenuItem{Header=text};foreach(var i in items)m.Items.Add(i);return m;}
        foreach(var item in (object[])[
            Item(L.T("ROMを開く","Open ROM"),()=>_=OpenRom()),Item(L.T("一時停止","Pause"),TogglePause,()=>paused),
            Item(L.T("ステータス保存","Save state"),()=>_=SaveState()),Item(L.T("ステータス読込","Load state"),()=>_=LoadState()),new Separator(),
            Sub(L.T("速度","Speed"),[..Rates.Select((r,i)=>Item($"{r}x",()=>{speedIndex=i;audio?.Flush();},()=>speedIndex==i))]),
            Sub(L.T("表示","View"),Item(L.T("通常","Normal"),()=>SetView(WindowView.Normal),()=>view==WindowView.Normal),Item(L.T("本体だけ","Case only"),()=>SetView(WindowView.Body),()=>view==WindowView.Body),Item(L.T("画面だけ","Screen only"),()=>SetView(WindowView.Screen),()=>view==WindowView.Screen)),
            Sub(L.T("液晶","LCD"),Item(L.T("リアル表示","Real"),()=>SetLcd(!surface.RealLcd,surface.Contrast),()=>surface.RealLcd),Item(L.T("残像","Afterimage"),()=>{bool on=!surface.Afterimage;surface.SetAfterimage(on);settings=settings with{LcdAfterimage=on};SaveSettings();},()=>surface.Afterimage),
                Item(L.T("濃く","Darker"),()=>SetLcd(surface.RealLcd,surface.Contrast+10)),Item(L.T("薄く","Lighter"),()=>SetLcd(surface.RealLcd,surface.Contrast-10))),
            Sub(L.T("外観","Appearance"),[..HardwareSettings.Appearances.Select(a=>Item(a,()=>{settings=settings with{Appearance=a};surface.SetModel(a);SaveSettings();if(StartRom() is string rom)Try(()=>SwitchRom(rom,!testing));},()=>settings.Appearance==a))]),
            Item(L.T("消音","Mute"),()=>{settings=settings with{Muted=!settings.Muted};if(audio is not null)audio.Muted=settings.Muted;SaveSettings();},()=>settings.Muted),
            Item(L.T("常に手前に表示","Always on top"),()=>{Topmost=!Topmost;settings=settings with{TopMost=Topmost};SaveSettings();},()=>Topmost),new Separator(),
            Item(L.T("最小化","Minimize"),()=>WindowState=WindowState.Minimized),Item(L.T("終了","Exit"),Close)])menu.Items.Add(item);
        return menu;
    }
    async void Try(Action action) {try{action();}catch(Exception ex){await Message(ex.Message);}}
    void SetLcd(bool real,int contrast){surface.SetLcd(real,contrast);settings=settings with{LcdDotLines=surface.RealLcd,LcdContrast=surface.Contrast};SaveSettings();}
    void SaveSettings(){if(testing)return;try{settings.Save(Path.Combine(root,"state","settings.json"));}catch(Exception ex){Notify(ex.Message);}}
    /// <summary>メッセージのウィンドウ。</summary>
    async Task Message(string text) {
        var ok=new Button{Content="OK",HorizontalAlignment=HorizontalAlignment.Right,IsDefault=true,IsCancel=true,MinWidth=80,HorizontalContentAlignment=HorizontalAlignment.Center};
        var window=new Window{Title="PC-E500",SizeToContent=SizeToContent.WidthAndHeight,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,
            Content=new StackPanel{Margin=new Thickness(20),Spacing=16,Children={new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,MaxWidth=560},ok}}};
        ok.Click+=(_,_)=>window.Close();
        await window.ShowDialog(this);
    }
    void Notify(string text){notice=text;noticeUntil=clock.Elapsed.TotalSeconds+5;}
    void TogglePause(){paused=!paused;pause.Content=paused?L.T("再開","Resume"):L.T("一時停止","Pause");ReleaseKeys();audio?.Flush();Draw();}
    void ReleaseKeys(){downKeys.Clear();input.Clear();surface.ReleaseKeys();}
    void ResetPin() {
        ReleaseKeys();audio?.Flush();
        // ON held through RESET reaches the ROM's own all-reset prompt; alone, the pin restarts the machine.
        Native.e500_reset(core!.Handle,0);paused=false;
    }
    static readonly FilePickerFileType RomType=new("ROM"){Patterns=["*.rom","*.ROM","*.bin"]},StateType=new("PC-E500 state"){Patterns=["*.e500state"]};
    async Task OpenRom() {
        var start=await StorageProvider.TryGetFolderFromPathAsync(Path.Combine(root,"rom"));
        var files=await StorageProvider.OpenFilePickerAsync(new(){FileTypeFilter=[RomType,FilePickerFileTypes.All],SuggestedStartLocation=start});
        if(files.Count>0 && files[0].TryGetLocalPath() is string path)Try(()=>{SwitchRom(path,!testing);settings=settings with{RomFile=path};SaveSettings();});
    }
    async Task SaveState() {
        if(core is null)return;
        var file=await StorageProvider.SaveFilePickerAsync(new(){FileTypeChoices=[StateType],DefaultExtension="e500state",SuggestedFileName="pc-e500.e500state"});
        if(file?.TryGetLocalPath() is string path)Try(()=>core.Save(path));
    }
    async Task LoadState() {
        if(core is null)return;
        var files=await StorageProvider.OpenFilePickerAsync(new(){FileTypeFilter=[StateType,FilePickerFileTypes.All]});
        if(files.Count>0 && files[0].TryGetLocalPath() is string path)Try(()=>{ReleaseKeys();core.Load(path);audio?.Flush();});
    }

    // --- The machine ---------------------------------------------------------------
    string AutoSavePath(Core of)=>Path.Combine(root,"state",$"{of.Hash}.i{of.InternalKB}-c{of.CardKB}.e500state");
    string MemoryDirectory(Core of)=>Path.Combine(root,"state","memory",of.RomHash);
    /// <summary>起動時に開く ROM：最後に開いたもの、なければ rom フォルダから外観に合うもの。</summary>
    string? StartRom() {
        if(settings.RomFile.Length>0 && File.Exists(settings.RomFile))return settings.RomFile;
        string folder=Path.Combine(root,"rom");
        if(!Directory.Exists(folder))return null;
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
    void SwitchRom(string path,bool restore) {
        Core next=new(path,settings.InternalKB,settings.CardPath.Length>0?settings.CardPath:null);
        try {
            if(core is not null){ReleaseKeys();if(!testing)writer.WriteNow(PersistentItems());}
            if(restore)next.MemoryFiles(MemoryDirectory(next),true);
        } catch {next.Dispose();throw;}
        core?.Dispose();core=next;surface.Core=core;audio?.Flush();
        core.PrinterConnected=settings.PrinterConnected;core.Paper=paper;
        settings=settings with{InternalKB=core.InternalKB};
        paused=false;pause.Content=L.T("一時停止","Pause");
        Exception? mount=null;
        if(!testing)try{core.MountDisk(settings.DiskEnabled?settings.DiskFolder:null,settings.DiskReadOnly);}catch(Exception ex){mount=ex;}
        if(restore) {
            string snapshot=AutoSavePath(core);
            string?[] banks=[core.InternalFile(MemoryDirectory(core)),core.CardPath];
            var newest=banks.Where(File.Exists).Select(f=>File.GetLastWriteTimeUtc(f!)).DefaultIfEmpty(DateTime.MinValue).Max();
            if(File.Exists(snapshot) && File.GetLastWriteTimeUtc(snapshot)>=newest)
                try{core.Load(snapshot);}catch(Exception ex){Notify(ex.Message);}
        }
        core.Run(1024000);
        Native.e500_audio(core.Handle,soundBuffer,soundBuffer.Length);previous=clock.Elapsed.TotalSeconds;
        surface.Redraw();Draw();
        if(mount is not null)Notify(mount.Message);
    }
    void NextFrame()=>RequestAnimationFrame(_=>{if(!IsVisible)return;Advance();Draw();NextFrame();});
    /// <summary>経過時間の分だけ本体を進め、音声と自動保存を行う。</summary>
    void Advance() {
        double now=clock.Elapsed.TotalSeconds,elapsed=Math.Clamp(now-previous,0,0.1);previous=now;
        if(core is null || paused)return;
        try {
            input.Run((int)(elapsed*KeyInput<PcKey>.CpuHz*Rates[speedIndex]));
            audio?.Submit(soundBuffer,Native.e500_audio(core.Handle,soundBuffer,soundBuffer.Length));
            bool off=(Native.e500_status(core.Handle)&1)!=0;
            if(!testing && (saveOwed || now-lastSaved>=5 || (off && !lastOff))){saveOwed=writer.Busy || !writer.Start(PersistentItems());if(!saveOwed)lastSaved=now;}
            lastOff=off;
            if(!settings.AutoPowerOff && core.Cycles-heldAt>=KeyInput<PcKey>.CpuHz){heldAt=core.Cycles;core.HoldPower();}
        } catch(Exception ex) {paused=true;pause.Content=L.T("再開","Resume");ReleaseKeys();_=Message(ex.Message);}
    }
    ulong heldAt;
    /// <summary>LCD とステータスバーを、変わっていれば描き直す。</summary>
    void Draw() {
        frameAt=clock.Elapsed.TotalSeconds;frames++;
        if(core is null){ShowStatus();return;}
        if(!paused || settling){core.Refresh();settling=surface.UpdateLcd();}
        ShowStatus();
    }
    int frames;
    void ShowStatus() {
        if(notice is not null && clock.Elapsed.TotalSeconds>=noticeUntil)notice=null;
        string text=notice??(core is null?"":string.Join("   ",
            lastOff?L.T("● 電源OFF（EscでON）","● Power OFF (Esc turns ON)"):paused?L.T("● 一時停止","● Paused"):L.T("● 実行中","● Running"),
            $"ROM Ver.{core.RomVersion}",$"RAM {core.InternalKB}KB"+(core.HasCard?$"+{core.CardKB}KB":""),
            "CE-140F "+((Native.e500_disk_status(core.Handle)&1)!=0?L.T("接続","on"):L.T("未接続","off")),
            "CE-126P "+(core.PrinterConnected?L.T("接続","on"):L.T("未接続","off")),
            audio?.Error??(settings.Muted?L.T("消音","Muted"):L.T("音声ON","Sound on")),
            L.T("速度 ","Speed ")+Rates[speedIndex]+"x")+(writer.Failure is {} f?"   "+L.T("自動保存できません: ","Autosave failed: ")+f.Message:""));
        if(status.Text!=text)status.Text=text;
    }
    void OnClosing(object? sender,WindowClosingEventArgs e) {
        if(core is null || testing)return;
        try{ReleaseKeys();writer.WriteNow(PersistentItems());RememberView();settings.Save(Path.Combine(root,"state","settings.json"));}
        catch(Exception ex){Console.Error.WriteLine(ex.Message);}
    }

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
        if(ctrl && e.Key==PcKey.V){e.Handled=true;if(!charRepeats)_=PasteClipboard();return;}
        if(Special.TryGetValue(e.Key,out string? name)) {
            e.Handled=true;
            if(!charRepeats)input.Hold(e.Key,Keyboard.Codes[name],ctrl?Keyboard.Codes["CTRL"]:e.KeyModifiers.HasFlag(KeyModifiers.Shift)?Keyboard.Codes["SHIFT"]:-1);
            return;
        }
        // Ctrl with a letter gives no text: the letter, with CTRL.
        if(ctrl && e.Key>=PcKey.A && e.Key<=PcKey.Z) {
            e.Handled=true;
            if(!charRepeats)input.Hold(e.Key,Keyboard.Codes[((char)('A'+(e.Key-PcKey.A))).ToString()],Keyboard.Codes["CTRL"]);
        }
    }
    void OnPcText(object? sender,TextInputEventArgs e) {
        if(e.Text is not {Length:>0} text)return;
        e.Handled=true;
        if(text.Length==1 && downKeys.Contains(lastDown)) {
            // The key last pressed holds its character until it comes up; its repeats are not used.
            if(charRepeats || char.IsControl(text[0]))return;
            if(KeyInput<PcKey>.CharacterKey(text[0],out int code,out int modifier))input.Hold(lastDown,code,modifier);
            else Notify(L.T($"未対応のPC文字: {text}",$"Unsupported PC character: {text}"));
            return;
        }
        foreach(char c in text)if(!char.IsControl(c) && !input.Character(c))Notify(L.T($"未対応のPC文字: {c}",$"Unsupported PC character: {c}"));
    }
    async Task PasteClipboard() {
        try {
            string? text=Clipboard is null?null:await Clipboard.TryGetTextAsync();
            Notify(string.IsNullOrEmpty(text)?L.T("クリップボードに文字列がありません。","The clipboard has no text."):input.Paste(text));
        } catch(Exception ex) {Notify(ex.Message);}
    }

    // --- The views -------------------------------------------------------------------
    const double MinZoom=0.75,MaxZoom=8;
    Size ViewSize(double zoom){zoom=Math.Clamp(zoom,MinZoom,MaxZoom);var s=surface.ShownSize;return new Size(Math.Ceiling(s.Width*zoom)+surface.Border,Math.Ceiling(s.Height*zoom)+surface.Border);}
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
        bar.IsVisible=statusBar.IsVisible=normal;
        // The case's corners are see-through, by the compositor rather than a colour.
        TransparencyLevelHint=next==WindowView.Body?[WindowTransparencyLevel.Transparent]:[];
        if(next==WindowView.Body)Background=Brushes.Transparent;else ClearValue(BackgroundProperty);
        WindowDecorations=normal?WindowDecorations.Full:WindowDecorations.None;
        MinWidth=normal?500:0;MinHeight=normal?320:0;
        if(normal) {
            Width=normalSize.Width;Height=normalSize.Height;Position=normalPosition;
            if(normalMaximized)WindowState=WindowState.Maximized;
        } else {
            _=Settle(zoom,anchor);
        }
        if(normal){RememberView();SaveSettings();}
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
    // real, shot file, top, info. Used to look at the program on a machine with no hands at it.
    /// <summary>試験用：スクリプトを1行ずつ実行して終わる（スクリプトがなければ3秒後の絵を保存する）。</summary>
    async Task RunScript(string? shot,string? script) {
        string[] lines=script is null?["wait 3",$"shot {shot}"]:File.ReadAllLines(script);
        try {
            foreach(string line in lines) {
                string[] part=line.Trim().Split(' ',2);string rest=part.Length>1?part[1]:"";
                switch(part[0]) {
                case "wait":await Task.Delay(TimeSpan.FromSeconds(double.Parse(rest,System.Globalization.CultureInfo.InvariantCulture)));break;
                case "key":input.Tap(Keyboard.Codes[rest]);break;
                case "type":input.Paste(rest);break;
                case "enter":input.Tap(Keyboard.Codes["ENTER"]);break;
                case "view":SetView(Enum.Parse<WindowView>(rest,true));break;
                case "real":SetLcd(!surface.RealLcd,surface.Contrast);break;
                case "model":surface.SetModel(rest);break;
                case "top":Topmost=!Topmost;break;
                case "press":surface.SetPressed(rest,true);break;
                case "shot": {
                    // The surface alone, as drawn, at the screen's own resolution.
                    var size=new PixelSize((int)(Bounds.Width*RenderScaling),(int)(Bounds.Height*RenderScaling));
                    using var bitmap=new RenderTargetBitmap(size,new Vector(96*RenderScaling,96*RenderScaling));
                    bitmap.Render((Visual)Content!);bitmap.Save(rest,PngBitmapEncoderOptions.Default);break;
                }
                case "info":
                    Console.WriteLine($"view={view} position={Position} size={ClientSize} scaling={RenderScaling} topmost={Topmost} transparency={ActualTransparencyLevel} frames={frames} at {clock.Elapsed.TotalSeconds:F1}s lcd={surface.LcdRepaints} sound={audio?.Error??"ok"} {(audio is AlsaSound a?$"delivered={a.Delivered} underruns={a.Underruns}":"")}");
                    break;
                }
            }
            if(core is not null)Console.WriteLine(core.ScreenText());
        } catch(Exception ex) {Console.Error.WriteLine(ex);Environment.ExitCode=1;}
        Close();
    }
}
