// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;

namespace PokeCom;

/// <summary>アプリケーションの入口。</summary>
internal static class Program
{
    [STAThread]
    /// <summary>
    /// 起動する。引数で動きが変わる。
    /// --smoke-test ROM 出力先：自動テストを実行する。
    /// --manual-shots ROM 出力先 [言語]：マニュアル用の画像を撮る。
    /// --open-folder [rom]：データのフォルダ（または rom フォルダ）を開く（スタートメニューとインストーラーから使う）。
    /// ROM のパス（省略可）：その ROM で起動する。
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try {
            // The tests and the manual's pictures always draw light (the manual may ask for dark).
            if(args.Length>0 && args[0]=="--smoke-test") {Theme.Init("Light");SmokeTest.Run(args[1],args[2]);return;}
            if(args.Length>0 && args[0]=="--manual-shots") {Theme.Init(args.Length>4?args[4]:"Light");ManualShots.Run(args[1],args[2],args.Length>3?args[3]:"ja");return;}
            if(args.Length>4 && args[0]=="--demo-video") {Theme.Init("Light");DemoVideo.Run(args[1],args[2],args[3],args[4]);return;}
            if(args.Length>4 && args[0]=="--compare-video") {Theme.Init("Light");CompareVideo.Run(args[1],args[2],args[3],args[4],args.Length>5?double.Parse(args[5],System.Globalization.CultureInfo.InvariantCulture):30);return;}
            if(args.Length>0 && args[0]=="--open-folder") {AppFolders.Open(args.Length>1?args[1]:"");return;}
            Theme.Init(MainForm.SavedTheme());
            Application.Run(new MainForm(args.FirstOrDefault()));
        } catch(Exception ex) {MessageBox.Show(ex.ToString(),"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Error);Environment.ExitCode=1;}
    }
}

/// <summary>
/// メインウィンドウ。本体の絵（PocketSurface）、ツールバー、ステータスバーを持ち、
/// タイマーでエミュレーションを進め、キー入力・音声・自動保存・各ウィンドウを扱う。
/// </summary>
internal sealed class MainForm : Form
{
    /// <summary>今動かしているエミュレーションコア（ROM を開くまでは null）。</summary>
    Core? core;
    /// <summary>本体の絵（キーと LCD）。</summary>
    readonly PocketSurface surface=new();
    /// <summary>音声の出力（テスト中は null）。</summary>
    readonly AudioOutput? audio;
    readonly short[] soundBuffer=new short[48000];
    /// <summary>ハードウェアと外観の設定（state/settings.json）。</summary>
    HardwareSettings settings=new();
    /// <summary>自動テスト・画像の撮影中か（ファイルの保存やタイマーを止める）。</summary>
    readonly bool testing;
    /// <summary>最後に自動保存した時刻（秒）と、前回の電源 OFF の状態。</summary>
    double lastSaved;
    bool lastOff;
    /// <summary>ウィンドウの下のステータスバー（通常の表示のときだけ見せる）。</summary>
    // Fields go short CE-140F, CE-126P, speed, sound, ML, RAM, ROM first to last.
    readonly StatusBar status=new(){ShortOrder=[3,4,6,5,2,1,0]};
    // 8 ms so a keystroke and the click it makes are not held back by the frame
    // they land in. The LCD and the status line are looked at on every other tick
    // (about 62 times a second; timeBeginPeriod makes the timer really 8 ms), and
    // only what changed is repainted.
    /// <summary>エミュレーションを進めるタイマー（8ms ごと）。</summary>
    readonly System.Windows.Forms.Timer timer=new(){Interval=8};
    /// <summary>画面の書き換えごとに描かせる役（テスト中は null で、タイマーが1回おきに描く）。</summary>
    FramePacer? pacer;
    int tick;
    /// <summary>LCD の残像がまだ変わっている途中か（一時停止中も描き直すため）。</summary>
    bool lcdSettling;
    /// <summary>自動電源 OFF の残り時間を最後に戻したときのサイクル数。</summary>
    ulong heldAt;
    /// <summary>自動保存の番が来たのに、前の書き込みが終わっておらず始められなかったか（次の回にすぐ試す）。</summary>
    bool saveOwed;
    readonly Stopwatch clock=Stopwatch.StartNew();
    /// <summary>キー入力の待ち行列：キーコード、押すか離すか、その状態を保つサイクル数。</summary>
    readonly Queue<(int Code,bool Down,int Cycles)> inputQueue=new();
    
    /// <summary>画面でクリックして押したままの SHIFT・CTRL。</summary>
    readonly HashSet<int> held=new();
    // PC keys physically down. A KEYDOWN for one already in here is Windows
    // repeating the key, not a second keystroke.
    readonly HashSet<Keys> downKeys=new();
    bool charRepeats;
    /// <summary>最後に押された PC のキー（続く文字をこのキーの長押しとして扱う）。</summary>
    Keys lastDown;
    // A PC key held down holds the machine's key down until it comes up, as a
    // finger on the real keys would: the ROM's own repeat (BS, DEL, the cursor
    // keys) and games that read a held key then work as on the machine.
    // Windows' repeats are not used; they only ever were taps.
    /// <summary>PC のキーごとに、押したままにしている本体のキーと、一緒に押している SHIFT・CTRL（なければ -1）。</summary>
    readonly Dictionary<HoldBy,(int Code,int Modifier)> holding=new();
    // A key held by the controller is held under its binding ("UP", "SHIFT+A"),
    // so two buttons on one key (the D-pad and the stick) hold it once, until both are up.
    /// <summary>キーを押したままにしている元：PC のキーか、コントローラーの割り当て（Binding）。</summary>
    readonly record struct HoldBy(Keys Key,string? Binding=null);
    /// <summary>コントローラーの設定（state/controller.json）。</summary>
    PadConfig padConfig=PadConfig.Defaults();
    /// <summary>今読んでいるコントローラー（見つかっていなければ null）と、次に探す時刻（秒）。</summary>
    PadDevice? padDevice;
    double padScanAt;
    readonly HashSet<string> padPressed=new(),padWanted=new();
    // Every sub-window is modeless so the machine stays usable while one is open.
    // One instance each: reopening brings the existing window forward.
    /// <summary>開いているサブウィンドウ（種類ごとに1つ）。</summary>
    readonly Dictionary<string,Form> windows=new();
    // The CE-126P's paper outlives ROM switches and its window.
    readonly PrinterPaper paper=new();
    /// <summary>CE-126P の印字を、選んだプリンタ（USB のサーマルプリンタか Windows のプリンタ）へ送る。</summary>
    readonly PrintOutput printOut=new();
    readonly ToolButton pause=new(){Text=PauseCommand,AutoSize=true,Glyph=Theme.Icons.Pause,Margin=new Padding(0,0,2,0)};
    int speedIndex=1;
    /// <summary>データを置くフォルダ（rom・ram・drive-x・state がある）と、プログラムのフォルダ（manual がある）。
    /// zip 版では同じ、インストール版では別（AppFolders を参照）。</summary>
    readonly string root,programRoot;
    /// <summary>今のキーの状態を保つ残りのサイクル数（0 なら次のキーへ）。</summary>
    int inputWait;
    double previous;
    bool paused;
    /// <summary>ツールバー・本体の絵・案内・ステータスバーを並べる表（通常の表示のときだけ見せる）。</summary>
    readonly TableLayoutPanel main=new(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(8,6,12,8)};
    /// <summary>通常の表示でのウィンドウの最小の大きさ。</summary>
    static readonly Size NormalMinimum=new(1040,625);
    /// <summary>右クリックのメニュー（ツールバーと同じ操作と、ウィンドウの表示）。</summary>
    readonly ContextMenuStrip menu=new();
    /// <summary>メニューの一時停止の項目（文字とアイコンをボタンに合わせる）。</summary>
    ToolStripMenuItem? pauseItem;
    /// <summary>メニューの項目のアイコン（なければ null）。</summary>
    Image? MenuIcon(char glyph)=>glyph=='\0'?null:Theme.Glyph(glyph,(int)Math.Round(16*DeviceDpi/96.0),Theme.Text);
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Theme.SetTitleBar(Handle);}
    /// <summary>新しい版があるときだけ出す、ツールバーのボタンとメニューの項目。</summary>
    readonly ToolButton updateNotice=new(){AutoSize=true,Visible=false,Glyph=Theme.Icons.Download,Highlight=Color.FromArgb(238,204,121),Margin=new Padding(8,0,0,0)};
    readonly ToolStripMenuItem updateItem=new(){Available=false};
    /// <summary>見つかった新しい版（なければ null）。</summary>
    Release? available;
    /// <summary>今のウィンドウの表示と、通常の表示に戻すときの位置と大きさ。</summary>
    WindowView view=WindowView.Normal;
    Rectangle normalBounds;
    bool normalMaximized;
    /// <summary>一時停止のボタン・メニューの文字（押すと「再開」に変わる）。</summary>
    static string PauseCommand=>L.T("一時停止","Pause");
    /// <summary>ウィンドウを作り、設定を読み、ROM を開いて動かし始める。</summary>
    /// <param name="romPath">開く ROM（null なら rom フォルダから外観に合うものを探す）</param>
    /// <param name="testing">自動テスト用（設定・音声・自動保存・タイマーを使わない）</param>
    internal MainForm(string? romPath=null, bool testing=false)
    {
        programRoot=AppFolders.Program();root=AppFolders.Data(programRoot);
        this.testing=testing;
        if(!testing && AppFolders.IsInstalled(programRoot))AppFolders.Prepare(root);
        if(!testing) {
            settings=HardwareSettings.LoadOrReset(Path.Combine(root,"state","settings.json"),out string? problem,out string? kept);
            L.Set(settings.Language);
            if(problem is not null)MessageBox.Show(L.T("設定ファイルを読めなかったため、既定の設定で起動します。\n","The settings file could not be read, so the emulator starts with the default settings.\n")+problem+"\n\n"+
                (kept is not null?L.T("元のファイルは別名で残しました: ","The file was kept under another name: ")+kept
                    :L.T("元のファイルを別名にできませんでした。次に設定を保存すると上書きされます。","The file could not be renamed; it will be overwritten when the settings are next saved.")),
                "PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            audio=new AudioOutput();audio.Muted=settings.Muted;audio.Volume=settings.Volume/100f;
            padConfig=PadConfig.Load(PadConfigPath);
        }
        if(string.IsNullOrWhiteSpace(settings.DiskFolder))settings=settings with{DiskFolder=Path.Combine(root,"drive-x")};
        surface.SetModel(settings.Appearance);
        surface.SetLcd(settings.LcdDotLines,settings.LcdContrast);surface.SetAfterimage(settings.LcdAfterimage && !testing);
        var executableIcon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if(executableIcon is not null)Icon=executableIcon;
        Text=Title(settings.Appearance);ClientSize=new Size(1180,725);MinimumSize=NormalMinimum;
        AutoScaleMode=AutoScaleMode.Dpi;BackColor=Theme.Window;ForeColor=Theme.Text;surface.BackColor=Theme.Window;
        Font=Fonts.Ui;KeyPreview=true;
        // 画面の構成：ツールバー、本体の絵の2段と、ウィンドウの下いっぱいのステータスバー。
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));main.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(main);
        // Added after the table, so it is docked first and the table fills what is left.
        Controls.Add(status);
        // The buttons wrap onto a second row rather than running off a narrow window.
        var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new Padding(0,0,0,4)};main.Controls.Add(bar,0,0);
        pause.Click+=(_,_)=>TogglePause();
        // One list for the toolbar and the right-click menu, so the two cannot differ.
        // ツールバーと右クリックのメニューは同じ一覧から作る（食い違わないように）。
        Command[] commands=[
            new(L.T("ROMを開く","Open ROM"),OpenRom,Glyph:Theme.Icons.OpenFolder),new(L.T("プログラム読込","Load program"),LoadFile,Glyph:Theme.Icons.Import),new(PauseCommand,TogglePause,pause,()=>paused,Theme.Icons.Pause),
            new(L.T("ステータス保存","Save state"),SaveState,Glyph:Theme.Icons.Save),new(L.T("ステータス読込","Load state"),LoadState,Glyph:Theme.Icons.Load),
            new(L.T("設定","Settings"),ConfigureHardware,Glyph:Theme.Icons.Settings),new(L.T("画面","Display"),ShowDisplay,Glyph:Theme.Icons.Display),new("CE-140F",ShowDisk,Glyph:Theme.Icons.Drive),new("CE-126P",ShowPrinter,Glyph:Theme.Icons.Print),
            new(L.T("文字入出力","Text I/O"),ShowTextTransfer,Glyph:Theme.Icons.Text),new(L.T("コントローラー","Controller"),ShowPad,Glyph:Theme.Icons.Game),new(L.T("オールリセット","All Reset"),AllReset,Glyph:Theme.Icons.Reset),new(L.T("デバッグ","Debug"),ShowDebugger,Glyph:Theme.Icons.Bug),new(L.T("使い方","Help"),Help,Glyph:Theme.Icons.Help)
        ];
        foreach(var command in commands) {
            if(command.Button is not null)bar.Controls.Add(command.Button);else AddAction(bar,command.Text,command.Action,command.Glyph);
        }
        bar.Controls.Add(updateNotice);
        updateNotice.Click+=(_,_)=>TryAction(OpenUpdate);updateItem.Click+=(_,_)=>TryAction(OpenUpdate);
        BuildMenu(commands);
        main.Controls.Add(surface,0,1);
        surface.BackgroundDoubleClicked+=()=>TryAction(CycleView);
        // A .BAS or .BIN dropped on the window loads as "Load program" does.
        // .BAS・.BIN をウィンドウに落とすと、「プログラム読込」と同じに読み込む。
        foreach(Control target in new Control[]{this,surface}) {
            target.AllowDrop=true;
            target.DragEnter+=(_,e)=>e.Effect=e.Data?.GetDataPresent(DataFormats.FileDrop)==true?DragDropEffects.Copy:DragDropEffects.None;
            target.DragDrop+=(_,e)=>{if(e.Data?.GetData(DataFormats.FileDrop) is string[] {Length:>0} files){Activate();string file=files[0];BeginInvoke(()=>TryAction(()=>Notify(LoadProgramFile(file,settings.LoadRun))));}};
            // The load runs after the drop has returned: a question it asks would otherwise hold Explorer until answered.
            // 読み込みはドロップが終わってから行う（途中で聞く確認が、答えるまでエクスプローラーを止めてしまうため）。
        }
        surface.BackgroundDragged+=DragWindow;
        // 画面のキーのクリック：RESET 穴はリセット、SHIFT・CTRL は押したままにする、ほかはキー入力の待ち行列へ。
        surface.KeyClicked+=name=>{
            if(core is null||paused)return;
            if(name=="RESET") {TryAction(ResetPin);return;}
            int code=Keyboard.Codes[name];
            // A clicked SHIFT or CTRL joins the queue, so it reaches the key after the
            // ones still waiting, not those too; it stays down for the next key.
            // 画面の SHIFT・CTRL は待ち行列に入れる（待っている前のキーには効かせず、次のキーまで押したまま）。
            if(name is "SHIFT" or "CTRL") {
                bool down=held.Add(code);if(!down)held.Remove(code);
                inputQueue.Enqueue((code,down,down?ShiftLeadCycles:ModifierCycles));NextKey();surface.SetPressed(name,down);
            }
            // The PC's Ctrl held while a key is clicked adds CTRL to it.
            else if((ModifierKeys&Keys.Control)!=0)QueueWith(Keyboard.Codes["CTRL"],code);
            else QueueKey(code);
        };
        // Focus leaving lets go of the PC's keys only; typing still queued (a paste) goes on.
        Deactivate+=(_,_)=>ReleaseKeys(true);
        KeyDown+=OnKeyDown;KeyUp+=OnKeyUp;KeyPress+=OnKeyPress;
        timer.Tick+=(_,_)=>Tick();
        // 閉じる前に RAM・カード・状態と設定を保存する。失敗したら、それでも閉じるかを聞く。
        FormClosing+=(_,e)=>{
            if(core is null)return;
            try {ReleaseKeys();SavePersistent();if(!testing)settings.Save(Path.Combine(root,"state","settings.json"));
                // Lines still waiting for the printer go out while the window is here
                // (a PDF printer asks where to save); closed first, they were lost.
                // 印刷を待っている行は、ウィンドウがあるうちに出す（閉じてからでは失われた）。
                printOut.Flush(15000);}
            catch(Exception ex) {
                if(MessageBox.Show(L.T("状態を保存できませんでした。終了しますか？\n","Could not save the state. Quit anyway?\n")+ex.Message,"PC-E500",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.No)e.Cancel=true;
            }
        };
        FormClosed+=(_,_)=>{foreach(var window in windows.Values.ToArray())window.Close();pacer?.Dispose();timer.Dispose();audio?.Dispose();printOut.Dispose();core?.Dispose();};
        // A USB printer found on another port keeps its new path in the settings.
        printOut.Moved+=moved=>BeginInvoke(()=>{settings=settings with{PrintTo=moved};SaveSettings();});
        // Every line the CE-126P prints goes on to the chosen printer as well.
        paper.LineAdded+=line=>printOut.Line(line,core?.Font??[]);
        // The saved printer as it is; one not found under its path is looked for by name when it is first written to.
        if(!testing && settings.PrintTo is not null)printOut.Select(settings.PrintTo);
        // ROM の指定がなければ、最後に開いた ROM か、rom フォルダから外観に合うものを選ぶ。
        romPath??=StartRom(root,settings);
        if(romPath is not null) {
            TryAction(()=>SwitchRom(romPath,!testing));
        }
        if(!testing){timer.Start();pacer=new FramePacer(this,Frame);}
        TopMost=settings.TopMost && !testing;
        // The window has its place on the screen only once shown; then the saved view.
        if(!testing)Shown+=(_,_)=>TryAction(()=>RestoreView(settings));
        // Once a day at most, in the background; nothing is said if it fails.
        if(!testing)Shown+=(_,_)=>{if(settings.CheckUpdates && DateTime.UtcNow-settings.UpdateCheckedAt>=TimeSpan.FromDays(1))_=CheckForUpdate(false);};
        // Written at once, so that a crash before closing does not lose the place.
        ResizeEnd+=(_,_)=>{if(view!=WindowView.Normal){RememberView();SaveSettings();}};
    }
    // The ROM last opened with "Open ROM" starts again, wherever it is: an installed
    // copy's rom folder starts empty, and a ROM opened from elsewhere had to be
    // opened by hand at every start. Gone (a removed drive), the folder is searched.
    /// <summary>起動時に開く ROM：「ROMを開く」で最後に開いたもの（あれば）、なければ rom フォルダから外観（E500 か E650）に合う大きさと名前のもの。</summary>
    /// <param name="root">データのフォルダ</param>
    /// <param name="s">設定</param>
    /// <returns>ROM のファイル（見つからなければ null）</returns>
    // Read before the first window, which the theme must be chosen before;
    // a file that cannot be read starts on Windows' own (the window then says why).
    /// <summary>設定ファイルのテーマ（読めなければ Windows に合わせる）。</summary>
    internal static string SavedTheme() {
        try {return HardwareSettings.Load(Path.Combine(AppFolders.Data(AppFolders.Program()),"state","settings.json")).Theme;}
        catch(Exception) {return "System";}
    }
    internal static string? StartRom(string root,HardwareSettings s) {
        if(s.RomFile.Length>0 && File.Exists(s.RomFile))return s.RomFile;
        string directory=Path.Combine(root,"rom");
        // A raw SAVEM dump of the main ROM is 16 bytes longer and opens as well.
        static long RomBytes(string x){long n=new FileInfo(x).Length;return n==256*1024+16?256*1024:n;}
        var candidates=Directory.Exists(directory)?Directory.GetFiles(directory,"*.rom")
            .Where(x=>RomBytes(x) is 256*1024 or 384*1024).ToArray():[];
        bool e500=s.Appearance=="PC-E500";
        string[] wanted=e500?["E500","E550","1480","1490"]:["E650","U6000"];
        long wantedSize=e500?256*1024:384*1024;
        // The first model in the list that has a ROM; of several, the last by name,
        // which for names such as PC-E500(7.2).ROM is the newest version.
        // 一覧の先の機種を優先し、同じ機種が複数あれば名前の順で最後（「機種名(版).ROM」なら新しい版）を選ぶ。
        return wanted.Select(w=>candidates.Where(x=>RomBytes(x)==wantedSize && Path.GetFileName(x).Contains(w,StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x=>Path.GetFileName(x),StringComparer.OrdinalIgnoreCase).FirstOrDefault()).FirstOrDefault(x=>x is not null)
            ??candidates.FirstOrDefault(x=>Path.GetFileName(x)=="s3-7.3(E500).rom")
            ??(candidates.Length==1?candidates[0]:null);
    }
    /// <summary>RAM カードのイメージを置くフォルダ。</summary>
    string CardFolder()=>Path.Combine(root,"ram");
    /// <summary>自動保存のファイル（ROM・内蔵 RAM・カードの容量ごと）。</summary>
    string AutoSavePath(Core? of=null){of??=core!;return Path.Combine(root,"state",$"{of.Hash}.i{of.InternalKB}-c{of.CardKB}.e500state");}
    /// <summary>内蔵 RAM を保存するフォルダ（ROM ごと）。</summary>
    string MemoryDirectory(Core? of=null)=>Path.Combine(root,"state","memory",(of??core!).RomHash);
    /// <summary>内蔵 RAM・カード・状態のファイルを書く役（自動保存は裏のスレッドで書く）。</summary>
    readonly PersistentWriter writer=new();
    /// <summary>保存するもの：内蔵 RAM、カード、保存できる状態なら自動保存の状態。</summary>
    List<PersistentWriter.Item> PersistentItems() {
        var items=new List<PersistentWriter.Item>{core!.InternalItem(MemoryDirectory())};
        if(core.CardItem() is PersistentWriter.Item card)items.Add(card);
        if(core.CanSnapshot)items.Add(new(AutoSavePath(),core.Snapshot(),core.Hash));
        return items;
    }
    /// <summary>内蔵 RAM とカードを保存し、保存できる状態なら状態も自動保存する（その場で書く。失敗は例外）。</summary>
    void SavePersistent() {
        if(core is null||testing)return;
        writer.WriteNow(PersistentItems());
    }
    // Nothing is taken while the last write is still going: the copy would only be thrown away.
    /// <summary>自動保存：中身を取り出し、裏のスレッドで書く。</summary>
    /// <returns>書き始めたか（前の書き込みが終わっていなければ false）</returns>
    bool AutoSave() {
        if(core is null||testing)return true;
        return !writer.Busy && writer.Start(PersistentItems());
    }
    /// <summary>一時停止と再開を切り替える。</summary>
    void TogglePause() {
        paused=!paused;pause.Text=paused?L.T("再開","Resume"):PauseCommand;pause.Glyph=paused?Theme.Icons.Play:Theme.Icons.Pause;ReleaseKeys();audio?.Flush();UpdateDisplay();
    }
    /// <summary>実行の速さの倍率。</summary>
    static readonly string[] Speeds=["0.5x","1x","2x","4x"];
    /// <summary>実行の速さを選ぶ。</summary>
    /// <param name="index">Speeds の位置</param>
    void SetSpeed(int index) {speedIndex=index;audio?.Flush();statusKey=default;UpdateStatus();}
    /// <summary>次の速度へ（0.5x → 1x → 2x → 4x → 0.5x。ステータスバーの速度の欄から）。</summary>
    void NextSpeed()=>SetSpeed((speedIndex+1)%Speeds.Length);
    /// <summary>常に手前に表示するかを切り替えて、設定に残す（メニューから）。</summary>
    void ToggleTopMost() {settings=settings with{TopMost=!settings.TopMost};TopMost=settings.TopMost;SaveSettings();}
    // The toolbar's commands, then what the title bar and the frame give, which
    // the case-only and screen-only windows have not: the menu is the same in
    // every view, so it is always the way back.
    /// <summary>ツールバーとメニューの操作1つ。</summary>
    /// <param name="Text">ボタンとメニューの文字</param>
    /// <param name="Action">押したときの処理</param>
    /// <param name="Button">専用のボタン（文字が変わる一時停止など。null なら普通のボタンを作る）</param>
    /// <param name="Checked">メニューでチェックを付ける条件（null なら付けない）</param>
    /// <param name="Glyph">アイコンの文字（Theme.Icons）</param>
    readonly record struct Command(string Text,Action Action,ToolButton? Button=null,Func<bool>? Checked=null,char Glyph='\0');
    /// <summary>右クリックのメニューを作る：ツールバーと同じ操作、速度、ウィンドウの表示、最小化・終了。</summary>
    /// <param name="commands">ツールバーの操作</param>
    void BuildMenu(Command[] commands) {
        var checks=new List<(ToolStripMenuItem Item,Func<bool> Checked)>();
        foreach(var command in commands) {
            var action=command.Action;var item=new ToolStripMenuItem(command.Text,MenuIcon(command.Glyph),(_,_)=>TryAction(action));
            if(command.Button==pause)pauseItem=item;
            if(command.Checked is not null)checks.Add((item,command.Checked));
            menu.Items.Add(item);
        }
        menu.Items.Insert(0,updateItem);
        menu.Items.Add(L.T("新しい版を確認...","Check for a new version..."),MenuIcon(Theme.Icons.Download),(_,_)=>_=CheckForUpdate(true));
        menu.Items.Add(L.T("機械語エリア...","Machine-code area..."),MenuIcon(Theme.Icons.Code),(_,_)=>TryAction(ShowMachineCode));
        menu.Items.Add(L.T("データフォルダを開く","Open the data folder"),MenuIcon(Theme.Icons.Folder),(_,_)=>TryAction(()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppFolders.Prepare(root)){UseShellExecute=true})));
        var speedMenu=new ToolStripMenuItem(L.T("速度","Speed"),MenuIcon(Theme.Icons.Speed));
        for(int i=0;i<Speeds.Length;i++){int index=i;speedMenu.DropDownItems.Add(Speeds[i],null,(_,_)=>SetSpeed(index));}
        menu.Items.Add(speedMenu);
        menu.Items.Add(new ToolStripSeparator());
        var viewMenu=new ToolStripMenuItem(L.T("ウィンドウの表示","Window"),MenuIcon(Theme.Icons.Window));
        string[] views=[L.T("通常（タイトル・ボタンを表示）","Normal (title and buttons)"),L.T("本体だけ","Case only"),L.T("画面だけ","Screen only")];
        for(int i=0;i<views.Length;i++){var v=(WindowView)i;viewMenu.DropDownItems.Add(views[i],null,(_,_)=>TryAction(()=>SetView(v)));}
        menu.Items.Add(viewMenu);
        var topItem=new ToolStripMenuItem(L.T("常に手前に表示","Always on top"),null,(_,_)=>TryAction(ToggleTopMost));
        menu.Items.Add(topItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.T("最小化","Minimize"),MenuIcon(Theme.Icons.Minimize),(_,_)=>WindowState=FormWindowState.Minimized);
        menu.Items.Add(L.T("終了","Exit"),MenuIcon(Theme.Icons.Close),(_,_)=>Close());
        FluentMenuRenderer.Use(menu);
        // The checks follow the machine, which may have changed them from elsewhere.
        menu.Opening+=(_,_)=>{
            foreach(var (item,isChecked) in checks)item.Checked=isChecked();
            // The pause item shows what it does now, as the button does.
            if(pauseItem is not null){pauseItem.Text=pause.Text;pauseItem.Image?.Dispose();pauseItem.Image=MenuIcon(pause.Glyph);pauseItem.Checked=false;}
            for(int i=0;i<speedMenu.DropDownItems.Count;i++)((ToolStripMenuItem)speedMenu.DropDownItems[i]).Checked=i==speedIndex;
            for(int i=0;i<viewMenu.DropDownItems.Count;i++)((ToolStripMenuItem)viewMenu.DropDownItems[i]).Checked=i==(int)view;
            topItem.Checked=settings.TopMost;
        };
        // Set on the form too: a right-click on the toolbar or the status line asks the form.
        surface.ContextMenuStrip=menu;ContextMenuStrip=menu;
    }
    // Notification only: a newer release is announced on the toolbar and in
    // the menu, and the user downloads it. The automatic check is quiet - no
    // dialog, no error - and does not announce a version the user skipped; one
    // asked for from the menu always answers.
    /// <summary>新しい版を確認する。新しければツールバーとメニューに知らせる（手動の確認なら、お知らせも開く）。</summary>
    /// <param name="manual">メニューから確認したか（結果やエラーも表示する）</param>
    async Task CheckForUpdate(bool manual) {
        Release? release;
        try {release=await UpdateCheck.FetchAsync();}
        catch(Exception ex) {
            if(manual && !IsDisposed)MessageBox.Show(this,L.T("新しい版を確認できませんでした。\n","Could not check for a new version.\n")+ex.Message,"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        if(IsDisposed)return;
        settings=settings with{UpdateCheckedAt=DateTime.UtcNow};SaveSettings();
        if(release is null || !UpdateCheck.IsNewer(release)) {
            if(manual)MessageBox.Show(this,L.T($"最新の版です（{UpdateCheck.Current}）。",$"This is the latest version ({UpdateCheck.Current})."),"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return;
        }
        ShowUpdate(release,manual);
    }
    /// <summary>新しい版をツールバーとメニューに知らせる（自動の確認では、通知しないことにした版は知らせない）。</summary>
    /// <param name="release">新しい版</param>
    /// <param name="manual">メニューから確認したか（お知らせのウィンドウも開く）</param>
    void ShowUpdate(Release release,bool manual) {
        if(!manual && release.Tag==settings.SkippedUpdate)return;
        available=release;
        updateNotice.Text=L.T($"新しい版 {release.Tag}",$"New version {release.Tag}");updateNotice.Visible=true;
        updateItem.Text=L.T($"新しい版 {release.Tag} があります...",$"Version {release.Tag} is out...");updateItem.Available=true;
        if(manual)OpenUpdate();
    }
    /// <summary>新しい版のお知らせを開く。</summary>
    void OpenUpdate() {
        if(available is null)return;
        using var dialog=new UpdateDialog(available,AppFolders.IsInstalled(programRoot));dialog.ShowDialog(this);
        if(dialog.Skip)SkipUpdate();
    }
    /// <summary>今知らせている版を、次から通知しないことにする（お知らせも消す）。</summary>
    void SkipUpdate() {
        if(available is null)return;
        settings=settings with{SkippedUpdate=available.Tag};SaveSettings();
        available=null;updateNotice.Visible=false;updateItem.Available=false;
    }
    /// <summary>次のウィンドウの表示へ（通常 → 本体だけ → 画面だけ → 通常）。</summary>
    void CycleView()=>SetView(view switch{WindowView.Normal=>WindowView.Body,WindowView.Body=>WindowView.Screen,_=>WindowView.Normal});
    // The picture keeps its size, and the LCD its place on the screen: the
    // window closes in around the case, or around the glass, and goes back to
    // the bounds it had when it returns to normal. Without the frame the case's
    // corners are see-through (TransparencyKey is the surface's own background).
    /// <summary>ウィンドウの表示を変える。絵の大きさと、画面上の LCD の位置は変えず、ウィンドウがそれを囲む。</summary>
    /// <param name="next">新しい表示</param>
    void SetView(WindowView next)=>SetView(next,true);
    /// <summary>ウィンドウの表示を変える。</summary>
    /// <param name="next">新しい表示</param>
    /// <param name="keep">設定に残してファイルに書くか（起動時の復元は、拡大率と位置を戻してから書く）</param>
    void SetView(WindowView next,bool keep) {
        if(next==view)return;
        var anchor=surface.PointToScreen(surface.LcdArea().Location);float zoom=surface.Zoom;
        if(view==WindowView.Normal) {
            normalMaximized=WindowState==FormWindowState.Maximized;
            if(normalMaximized)WindowState=FormWindowState.Normal;
            normalBounds=Bounds;
        }
        view=next;
        SuspendLayout();
        try {
            surface.SetScreenOnly(next==WindowView.Screen);
            if(next==WindowView.Normal) {
                Controls.Remove(surface);main.Controls.Add(surface,0,1);main.Visible=true;status.Visible=true;
                TransparencyKey=Color.Empty;surface.BackColor=Theme.Window;FormBorderStyle=FormBorderStyle.Sizable;MinimumSize=NormalMinimum;
                Bounds=normalBounds;if(normalMaximized)WindowState=FormWindowState.Maximized;
            } else {
                if(surface.Parent==main){main.Controls.Remove(surface);Controls.Add(surface);surface.BringToFront();}
                main.Visible=false;status.Visible=false;
                FormBorderStyle=FormBorderStyle.None;MinimumSize=Size.Empty;
                // The see-through colour is one no part of the case is drawn in.
                surface.BackColor=next==WindowView.Body?PocketSurface.SeeThrough:Theme.Window;
                TransparencyKey=next==WindowView.Body?surface.BackColor:Color.Empty;
                ClientSize=ViewSize(zoom);
            }
            surface.Resizable=next!=WindowView.Normal;
        } finally {ResumeLayout(true);}
        if(next!=WindowView.Normal) {
            surface.Arrange();var now=surface.PointToScreen(surface.LcdArea().Location);
            Location=new Point(Location.X+anchor.X-now.X,Location.Y+anchor.Y-now.Y);
        }
        // Windows 11 rounds the frameless screen as it does any window; the case keeps its own shape.
        Theme.SetCorners(Handle,next==WindowView.Screen?2:next==WindowView.Body?1:0);
        if(keep){RememberView();SaveSettings();}
        // The keys go to the surface when this window is next active; the
        // display window, if it made the change, keeps the focus meanwhile.
        ActiveControl=surface;
        if(windows.TryGetValue("display",out var open) && open is DisplayDialog d)d.Sync(surface.RealLcd,surface.Contrast,view,settings.LcdAfterimage);
    }
    // How far the frameless views zoom: from about one screen pixel per LCD dot
    // up to eight times the case's own size.
    /// <summary>本体だけ・画面だけの表示で使える拡大率の範囲。</summary>
    const float MinZoom=0.75f,MaxZoom=8;
    /// <summary>枠のない表示で、拡大率 zoom の絵がちょうど収まるウィンドウの大きさ。</summary>
    Size ViewSize(float zoom) {
        zoom=Math.Clamp(zoom,MinZoom,MaxZoom);var size=surface.ShownSize;
        return new Size((int)Math.Ceiling(size.Width*zoom)+surface.Border,(int)Math.Ceiling(size.Height*zoom)+surface.Border);
    }
    // Windows' sizing edges, as WM_SIZING names them.
    const int WMSZ_LEFT=1,WMSZ_TOP=3,WMSZ_TOPLEFT=4,WMSZ_TOPRIGHT=5,WMSZ_BOTTOM=6,WMSZ_BOTTOMLEFT=7;
    // The edge dragged sets the new zoom - the height for the top and bottom
    // edges, the width for the rest - and the other side stays where it is, so
    // the window always fits the picture, with no margin round it.
    /// <summary>ドラッグ中のウィンドウの範囲を、絵の縦横の比に合わせる（つかんだ辺の反対側は動かさない）。</summary>
    /// <param name="r">Windows が提案する範囲</param>
    /// <param name="edge">つかんだ辺（WMSZ_ の値）</param>
    internal Rectangle FitSizing(Rectangle r,int edge) {
        var size=surface.ShownSize;int border=surface.Border;
        float zoom=edge is WMSZ_TOP or WMSZ_BOTTOM?(r.Height-border)/size.Height:(r.Width-border)/size.Width;
        var fit=ViewSize(zoom);
        int left=edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT?r.Right-fit.Width:r.Left;
        int top=edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT?r.Bottom-fit.Height:r.Top;
        return new Rectangle(left,top,fit.Width,fit.Height);
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct NativeRect {public int Left,Top,Right,Bottom;}
    protected override void WndProc(ref Message m) {
        const int WM_NCHITTEST=0x84,WM_SIZING=0x214,WM_NCLBUTTONDBLCLK=0xA3,WM_DEVICECHANGE=0x219;
        // A controller plugged in or out: looked for again on the next tick.
        if(m.Msg==WM_DEVICECHANGE){padRescanAt=clock.Elapsed.TotalSeconds+0.5;}
        if(view!=WindowView.Normal) {
            switch(m.Msg) {
            case WM_NCHITTEST: {
                // The surface handed this on: the edge of the picture under the mouse.
                var p=surface.PointToClient(new Point(unchecked((short)(long)m.LParam),unchecked((short)((long)m.LParam>>16))));
                int edge=surface.EdgeAt(p);
                if(edge!=0){m.Result=edge;return;}
                break;
            }
            case WM_SIZING: {
                var r=System.Runtime.InteropServices.Marshal.PtrToStructure<NativeRect>(m.LParam);
                var fit=FitSizing(Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom),(int)m.WParam);
                System.Runtime.InteropServices.Marshal.StructureToPtr(new NativeRect{Left=fit.Left,Top=fit.Top,Right=fit.Right,Bottom=fit.Bottom},m.LParam,false);
                m.Result=1;return;
            }
            // A double-click on an edge would stretch the window to the screen's height.
            case WM_NCLBUTTONDBLCLK:return;
            }
        }
        base.WndProc(ref m);
    }
    // Kept in the settings as it changes, written with them: the view, and for
    // the frameless ones their zoom and place, so the window comes back as it was.
    /// <summary>今のウィンドウの表示（枠のない表示なら拡大率と位置も）を設定に残す。</summary>
    void RememberView() {
        settings=view==WindowView.Normal?settings with{View=view}:settings with{View=view,ViewZoom=surface.Zoom,ViewLeft=Left,ViewTop=Top};
    }
    // At start-up: the view saved, at its zoom and place - unless that place is
    // off every screen now (a monitor unplugged), when the window stays where the
    // normal one opened.
    /// <summary>保存してあるウィンドウの表示に戻す（位置がどの画面にも入らなければ、位置だけは戻さない）。</summary>
    /// <param name="s">設定</param>
    internal void RestoreView(HardwareSettings s) {
        if(s.View==WindowView.Normal)return;
        SetView(s.View,false);
        if(s.ViewZoom>0){ClientSize=ViewSize(s.ViewZoom);surface.Arrange();}
        var at=new Rectangle(new Point(s.ViewLeft,s.ViewTop),Size);
        if(Screen.AllScreens.Any(screen=>{var shown=Rectangle.Intersect(screen.WorkingArea,at);return shown.Width>=40 && shown.Height>=40;}))Location=at.Location;
        RememberView();SaveSettings();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    /// <summary>枠のない表示で、本体の絵をつかんでウィンドウを動かす（タイトルバーをつかんだのと同じにする）。</summary>
    void DragWindow() {
        if(view==WindowView.Normal)return;
        const int WM_NCLBUTTONDOWN=0xA1,HTCAPTION=2;
        ReleaseCapture();SendMessage(Handle,WM_NCLBUTTONDOWN,HTCAPTION,IntPtr.Zero);
    }
    /// <summary>LCD のリアル表示とコントラストを変え、設定に残す（ファイルへは「画面」ウィンドウを閉じるときと終了時に書く）。</summary>
    void SetLcd(bool real,int contrast) {
        surface.SetLcd(real,contrast);settings=settings with{LcdDotLines=surface.RealLcd,LcdContrast=surface.Contrast};
        if(windows.TryGetValue("display",out var open) && open is DisplayDialog d)d.Sync(surface.RealLcd,surface.Contrast,view,settings.LcdAfterimage);
        if(!windows.ContainsKey("display"))SaveSettings();
    }
    /// <summary>LCD の残像（フレーム補完）を切り替え、設定に残す。</summary>
    void SetAfterimage(bool on) {
        settings=settings with{LcdAfterimage=on};surface.SetAfterimage(on && !testing);
        if(!windows.ContainsKey("display"))SaveSettings();
    }
    // The auto power off held off as the ESP32 build does: once a second of the
    // machine's time, the count the ROM runs down is put back (Core.HoldPower).
    /// <summary>自動電源 OFF を止める設定なら、本体の時間で1秒ごとに残り時間を初めの値に戻す。</summary>
    void KeepPowerOn() {
        if(core is null || settings.AutoPowerOff)return;
        ulong now=core.Cycles;
        if(now<heldAt)heldAt=now;
        if(now-heldAt<CpuHz)return;
        heldAt=now;core.HoldPower();
    }
    /// <summary>設定をファイルに書く（書けなければ知らせるだけ）。</summary>
    void SaveSettings() {if(!testing)TryAction(()=>settings.Save(Path.Combine(root,"state","settings.json")));}
    /// <summary>「画面」ウィンドウを開く。</summary>
    void ShowDisplay()=>ShowWindow("display",()=>{
        var dialog=new DisplayDialog(surface.RealLcd,surface.Contrast,view,settings.LcdAfterimage);
        dialog.LcdChanged=SetLcd;dialog.ViewChanged=v=>TryAction(()=>SetView(v));dialog.AfterimageChanged=SetAfterimage;
        dialog.FormClosed+=(_,_)=>SaveSettings();
        return dialog;
    });
    /// <summary>ツールバーにボタンを足す。押すと action を実行し、例外はメッセージで示す。</summary>
    /// <param name="parent">ボタンを置く場所</param>
    /// <param name="text">ボタンの文字</param>
    /// <param name="action">押したときの処理</param>
    /// <param name="glyph">アイコンの文字</param>
    void AddAction(Control parent,string text,Action action,char glyph) {
        var b=new ToolButton{Text=text,AutoSize=true,Glyph=glyph,Margin=new Padding(0,0,2,0)};
        b.Click+=(_,_)=>TryAction(action);parent.Controls.Add(b);
    }
    // The ROM scans the key matrix from its 64 Hz sub-timer, and only counts a key
    // whose press and release land in different scans with one scan between them.
    // 18000 down plus 24000 up (one and a half scans, then two) is the shortest pair that never drops a key; the
    // 120000/100000 at 1.024 MHz that this replaces spent 215 ms per key, so anything faster than
    // five keys a second piled up into a backlog that felt like input lag.
    // A shifted character only comes out shifted when SHIFT is already down for a
    // whole scan before the key joins it, and stays down for a whole scan after the
    // key leaves. The 8000 that both used to be is half a scan, so whichever
    // keystroke the scan phase fell on lost its SHIFT: pasting LOAD M "X:TETRIS.BIN"
    // dropped the closing quote to a bare W, and typing quotes gave one W in four.
    const int CpuHz=768000,KeyDownCycles=18000,KeyUpCycles=24000,ModeSettleCycles=200000,ModifierCycles=7500,
        ShiftLeadCycles=18000,ShiftTailCycles=18000;
    /// <summary>キーを1回押して離す操作を待ち行列に入れ、すぐに始める。</summary>
    /// <param name="code">キーコード</param>
    void QueueKey(int code) {
        if(core is null || paused)return;
        inputQueue.Enqueue((code,true,KeyDownCycles));inputQueue.Enqueue((code,false,KeyUpCycles));
        if(code==Keyboard.Codes["BASIC"] || code==Keyboard.Codes["MENU"] || code==Keyboard.Codes["F1"])
            inputQueue.Enqueue((code,false,ModeSettleCycles));
        // Keep a clicked modifier held until the character's matrix scan completes.
        foreach(var modifier in held.ToArray()) {inputQueue.Enqueue((modifier,false,ModifierCycles));held.Remove(modifier);}
        // Press now instead of on the next frame: the 16 ms timer would otherwise
        // add its own wait in front of the ROM scan on every keystroke.
        NextKey();
    }
    /// <summary>SHIFT を押したまま code のキーを押す操作を待ち行列に入れる。</summary>
    void QueueShifted(int code)=>QueueWith(Keyboard.Codes["SHIFT"],code);
    // The PC's Ctrl is no longer held down in the machine while it is held on the
    // PC: with a key it goes into the queue around that key, the way SHIFT does.
    // Held down, it reached the first key of a Ctrl+V paste, and stayed down
    // after a Ctrl+letter that sent nothing.
    /// <summary>modifier（SHIFT・CTRL）を押したまま code のキーを押す操作を待ち行列に入れる。</summary>
    void QueueWith(int modifier,int code) {
        inputQueue.Enqueue((modifier,true,ShiftLeadCycles));
        QueueKey(code);
        inputQueue.Enqueue((modifier,false,ShiftTailCycles));
    }
    /// <summary>前のキーの時間が過ぎていれば、待ち行列の次の操作を本体へ送る。</summary>
    /// <returns>送ったか</returns>
    bool NextKey() {
        if(core is null || inputWait!=0 || !inputQueue.TryDequeue(out var key))return false;
        core.Key(key.Code,key.Down);inputWait=key.Cycles;
        surface.SetPressed(Keyboard.Matrix[key.Code/8][key.Code%8],key.Down);
        return true;
    }
    // Windows repeats a held key thirty-one times a second. Those repeats used to
    // be sent as taps (and thinned so they never piled up); now the key is simply
    // held, and the repeats are ignored, so nothing can pile up behind a held key.
    /// <summary>本体のキーを押し、PC のキー pcKey が離されるまで押したままにする（modifier は一緒に押す SHIFT・CTRL、なければ -1）。</summary>
    void Hold(Keys pcKey,int code,int modifier)=>Hold(new HoldBy(pcKey),code,modifier);
    void Hold(HoldBy pcKey,int code,int modifier) {
        if(core is null || paused || holding.ContainsKey(pcKey))return;
        if(modifier>=0)inputQueue.Enqueue((modifier,true,ShiftLeadCycles));
        // The press stays at least one key time, so a quick tap still reaches a scan.
        inputQueue.Enqueue((code,true,KeyDownCycles));
        holding[pcKey]=(code,modifier);NextKey();
    }
    /// <summary>PC のキー pcKey で押したままにしていた本体のキーを離す。</summary>
    void Unhold(Keys pcKey)=>Unhold(new HoldBy(pcKey));
    void Unhold(HoldBy pcKey) {
        if(!holding.Remove(pcKey,out var h))return;
        inputQueue.Enqueue((h.Code,false,KeyUpCycles));
        if(h.Code==Keyboard.Codes["BASIC"] || h.Code==Keyboard.Codes["MENU"] || h.Code==Keyboard.Codes["F1"])inputQueue.Enqueue((h.Code,false,ModeSettleCycles));
        if(h.Modifier>=0)inputQueue.Enqueue((h.Modifier,false,ShiftTailCycles));
        // A SHIFT or CTRL clicked on the case goes with this key, as with a tap.
        foreach(var modifier in held.ToArray()){inputQueue.Enqueue((modifier,false,ModifierCycles));held.Remove(modifier);surface.SetPressed(Keyboard.Matrix[modifier/8][modifier%8],false);}
        NextKey();
    }
    /// <summary>押したままの本体のキーをすべて離す（ウィンドウからフォーカスが離れたとき）。</summary>
    void UnholdAll(){foreach(var pcKey in holding.Keys.ToArray())Unhold(pcKey);}
    // The keys with no character (Enter, the arrows, F1...) arrive in
    // ProcessCmdKey; here only the repeat of a character key is told apart, and
    // Ctrl is taken without pressing anything (it goes with the next key).
    /// <summary>PC のキーが押された：文字キーの自動リピートかを記録する（文字にならないキーは ProcessCmdKey、文字は OnKeyPress）。</summary>
    void OnKeyDown(object? sender,KeyEventArgs e) {
        if(e.Alt)return;
        charRepeats=!downKeys.Add(e.KeyCode);
        if(!charRepeats)lastDown=e.KeyCode;
        if(e.KeyCode==Keys.ControlKey){e.Handled=true;e.SuppressKeyPress=true;}
    }
    /// <summary>ショートカット（Ctrl+C・Ctrl+D・Ctrl+V）と、WinForms が先に取ってしまう矢印・Enter・Tab を処理する。</summary>
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
        if(keyData==(Keys.Control|Keys.C)){TryAction(ShowTextTransfer);return true;}
        if(keyData==(Keys.Control|Keys.D)){TryAction(ShowDebugger);return true;}
        if(keyData==(Keys.Control|Keys.V)){TryAction(()=>{
            if(!Clipboard.ContainsText(TextDataFormat.UnicodeText))throw new InvalidOperationException(L.T("クリップボードに文字列がありません。","The clipboard has no text."));
            Notify(PasteText(Clipboard.GetText(TextDataFormat.UnicodeText)));
        });return true;}
        // The arrows, Enter and Tab are dialog keys, so WinForms takes them here before
        // KeyDown ever runs. Only the press comes through: KeyUp still raises normally.
        if((keyData&Keys.Alt)==0 && Keyboard.Special.TryGetValue(keyData&Keys.KeyCode,out string? name) && name!="CTRL") {
            bool repeat=!downKeys.Add(keyData&Keys.KeyCode);
            // A character key carries its own shift through WM_CHAR; the arrows,
            // Enter and the function keys have no character, so the PC SHIFT is the
            // only way to reach their shifted meanings.
            if(!repeat) {
                int code=Keyboard.Codes[name];
                Hold(keyData&Keys.KeyCode,code,(keyData&Keys.Control)!=0?Keyboard.Codes["CTRL"]:(keyData&Keys.Shift)!=0?Keyboard.Codes["SHIFT"]:-1);
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    /// <summary>PC のキーが離された：自動リピートの判定から外す。</summary>
    void OnKeyUp(object? sender,KeyEventArgs e){downKeys.Remove(e.KeyCode);Unhold(e.KeyCode);}
    /// <summary>PC で文字が入力された：対応する本体のキー（必要なら SHIFT 付き）を待ち行列に入れる。</summary>
    void OnKeyPress(object? sender,KeyPressEventArgs e) {
        // Ctrl with a letter comes as a control character (Ctrl+A is 01h): the letter, with CTRL.
        // Ctrl+英字は制御文字（Ctrl+A は 01h）で来るので、その英字を CTRL 付きで送る。
        // The character belongs to the key last pressed, if it is still down: that
        // key holds it. One with no key down (an IME, a character sent by another
        // program) is a tap.
        // 文字は、まだ押されている最後のキーの長押しとして送る（押されたキーのない文字は1回押して離す）。
        bool hold=downKeys.Contains(lastDown);
        if(e.KeyChar is >= '\x01' and <= '\x1a' && (ModifierKeys&Keys.Control)!=0) {
            if(!charRepeats && Keyboard.Codes.TryGetValue(((char)('A'+e.KeyChar-1)).ToString(),out int letter)) {
                if(hold)Hold(lastDown,letter,Keyboard.Codes["CTRL"]);else QueueWith(Keyboard.Codes["CTRL"],letter);
            }
            charRepeats=false;e.Handled=true;return;
        }
        if(char.IsControl(e.KeyChar))return;
        if(!charRepeats) {
            if(!CharacterKey(e.KeyChar,out int code,out int modifier))
                Notify(L.T($"未対応のPC文字: {e.KeyChar}（画面のSHIFTとポケコンのキーで入力してください）",$"Unsupported PC character: {e.KeyChar} (use the on-screen SHIFT and pocket computer keys)"));
            else if(hold)Hold(lastDown,code,modifier);
            else if(modifier>=0)QueueWith(modifier,code);else QueueKey(code);
        }
        // Consumed: a WM_CHAR that arrives without its WM_KEYDOWN is a fresh keystroke.
        charRepeats=false;e.Handled=true;
    }
    /// <summary>1文字を本体のキー操作に変えて待ち行列に入れる。</summary>
    /// <param name="character">文字</param>
    /// <returns>対応するキーがあったか</returns>
    bool QueueCharacter(char character) {
        if(!CharacterKey(character,out int code,out int modifier))return false;
        if(modifier>=0)QueueWith(modifier,code);else QueueKey(code);
        return true;
    }
    /// <summary>1文字に当たる本体のキーと、一緒に押す SHIFT（なければ -1）。</summary>
    /// <returns>対応するキーがあったか</returns>
    static bool CharacterKey(char character,out int code,out int modifier) {
        modifier=-1;
        string text=character==' '?"SPACE":char.ToUpperInvariant(character).ToString();
        if(Keyboard.Codes.TryGetValue(text,out code))return true;
        if(Keyboard.Shifted.TryGetValue(character,out string? baseKey)){code=Keyboard.Codes[baseKey];modifier=Keyboard.Codes["SHIFT"];return true;}
        return false;
    }
    /// <summary>文字列をキー入力として送る（改行は ENTER、タブは空白4つ、対応しない文字は省く）。</summary>
    /// <param name="value">貼り付ける文字列（4096文字まで）</param>
    /// <returns>結果のメッセージ</returns>
    string PasteText(string value) {
        if(core is null)return L.T("ROMが読み込まれていません。","No ROM is loaded.");
        if(paused)return L.T("一時停止を解除してから貼り付けてください。","Resume the machine before pasting.");
        if(value.Length>4096)return L.T("貼り付けは一度に4096文字までです。","Paste up to 4096 characters at a time.");
        value=value.Replace("\r\n","\n").Replace('\r','\n');int queued=0,skipped=0;
        foreach(char character in value) {
            if(character=='\n'){QueueKey(Keyboard.Codes["ENTER"]);queued++;continue;}
            if(character=='\t'){for(int i=0;i<4;i++)QueueCharacter(' ');queued+=4;continue;}
            if(QueueCharacter(character))queued++;else skipped++;
        }
        string message=L.T($"{queued}文字をキー入力へ送信しました。",$"Sent {queued} characters as key input.");
        if(skipped>0)message+=L.T($" 未対応の{skipped}文字は省略しました。",$" Skipped {skipped} unsupported characters.");
        return message;
    }
    // A paste can take minutes to type. Moving to another window or using the
    // toolbar lets go of the PC's keys only; what stops the machine (a ROM
    // switch, a pause, a reset) drops the rest too, and says so.
    /// <summary>キーを離す。keepQueue なら PC のキーの記録だけを消し、待ち行列（貼り付けの残り）は続ける。
    /// そうでなければ本体のキーもすべて離して待ち行列を空にし、残っていれば知らせる。</summary>
    void ReleaseKeys(bool keepQueue=false) {
        // Keys held come up in the queue, behind what is still to be typed.
        if(keepQueue){UnholdAll();downKeys.Clear();charRepeats=false;return;}
        downKeys.Clear();charRepeats=false;holding.Clear();
        if(inputQueue.Count>0)Notify(L.T("入力の残り（貼り付けの途中など）を取り消しました。","The keys still to be typed (a paste) were dropped."));
        core?.Release();held.Clear();inputQueue.Clear();inputWait=0;
        // A BIN waiting for its reservation is dropped with the typing: the machine it
        // was meant for may be gone (a state loaded, a ROM switched, a reset).
        // 確保を待っている BIN も一緒にやめる（状態の読み込み・ROM の切り替え・リセットで、別の本体になっていることがある）。
        if(pendingBin is {} job){pendingBin=null;Notify(L.T($"{job.Name} の読み込みをやめました（機械語エリアの確保が終わる前に中断しました）。",$"{job.Name} was not loaded (stopped before the machine-code area was reserved)."));}
        surface.ReleaseKeys();
    }
    // A message shown in place of the status line for a few seconds; the line is
    // rebuilt every frame, which wiped one set straight on it in about 16 ms.
    /// <summary>ステータスバーに5秒間出すメッセージ。</summary>
    string? notice;
    double noticeUntil;
    /// <summary>メッセージをステータスバーに5秒間出す。</summary>
    void Notify(string message){notice=message;noticeUntil=clock.Elapsed.TotalSeconds+5;statusKey=default;UpdateStatus();}
    /// <summary>速度の倍率（Speeds の文字から求める）。</summary>
    static readonly double[] Rates=Speeds.Select(s=>double.Parse(s.TrimEnd('x'),System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    /// <summary>
    /// タイマーごとの処理：経過時間と速度の倍率に応じて CPU を進め、音声を出し、
    /// 5秒ごと（と電源 OFF になったとき）に自動保存し、1回おきに画面を更新する（変わった所だけ描き直す）。
    /// </summary>
    void Tick()=>Advance(pacer is null);
    // At each refresh of the screen: the machine run up to this moment, then the
    // LCD drawn there and then, so every frame shows the same span of the
    // machine's time and reaches the screen at the next refresh.
    /// <summary>画面の書き換えごと：本体を今まで進め、LCD をその場で描く。</summary>
    void Frame() {
        if(core is null)return;
        Advance(false);
        // As in Advance: a fault while drawing stops the machine with one message, not one a frame.
        // Advance と同じく、描画中の例外は本体を止めて1度だけ知らせる（コマごとに出さない）。
        try {if(!paused || lcdSettling){UpdateDisplay();surface.Update();}}
        catch(Exception ex) {if(paused)return;paused=true;pause.Text=L.T("再開","Resume");ReleaseKeys();MessageBox.Show(ex.Message,L.T("実行停止","Execution stopped"));}
    }
    /// <summary>経過時間の分だけ本体を進め、音声・自動保存・電源の保持を行う。</summary>
    /// <param name="draw">ここで画面も描くか（画面の書き換えごとに描く役がないとき、1回おきに）</param>
    void Advance(bool draw) {
        double now=clock.Elapsed.TotalSeconds,elapsed=Math.Clamp(now-previous,0,0.1);previous=now;
        if(core is null)return;
        // Paused, the afterimage still finishes fading to the picture that stays.
        if(paused){if(draw && lcdSettling)UpdateDisplay();return;}
        try {
            PollPad(now);
            double rate=Rates[speedIndex];
            int cycles=(int)(elapsed*CpuHz*rate);
            RunCycles(cycles);
            int count=Native.e500_audio(core.Handle,soundBuffer,soundBuffer.Length);audio?.Submit(soundBuffer,count);
            bool off=(Native.e500_status(core.Handle)&1)!=0;
            // A round that finds the last write still going is owed, and tried again on
            // the next tick, so the save at power-off is not lost to a slow one.
            // 前の書き込みが終わっておらず始められなかった回は、次のタイマーですぐ試す（電源 OFF の保存を失わないため）。
            if(!testing && (saveOwed || now-lastSaved>=5 || (off&&!lastOff))) {
                saveOwed=!AutoSave();
                if(!saveOwed)lastSaved=now;
            }
            bool powerChanged=off!=lastOff;lastOff=off;
            KeepPowerOn();
            if(draw?(++tick&1)==0 || powerChanged:powerChanged)UpdateDisplay();
        } catch(Exception ex) {paused=true;pause.Text=L.T("再開","Resume");ReleaseKeys();MessageBox.Show(ex.Message,L.T("実行停止","Execution stopped"));}
    }
    /// <summary>キー入力の待ち行列を進めながら、CPU を cycles サイクル実行する。</summary>
    /// <param name="cycles">実行するサイクル数</param>
    void RunCycles(int cycles) {
        if(core is null)return;
        while(cycles>0) {
            NextKey();
            int batch=inputWait>0?Math.Min(cycles,inputWait):cycles;
            core.Run(batch);cycles-=batch;if(inputWait>0)inputWait-=batch;
        }
        if(pendingBin is not null)FinishPendingBin();
    }
    /// <summary>LCD を読み直して絵を更新し、ステータスバーに状態を表示する。</summary>
    void UpdateDisplay() {
        if(core is null)return;
        core.Refresh();lcdSettling=surface.UpdateLcd();
        UpdateStatus();
    }
    // What the status line shows, compared each frame; the text, ten strings
    // and an interpolation, is built only when something in it has changed.
    /// <summary>ステータスバーに出す内容（変わったときだけ文字を作り直すため）。</summary>
    readonly record struct StatusKey(Core Core,int Status,int Speed,int MachineCode,bool Paused,int Disk,bool Printer,string? Audio,bool Muted,Exception? Saving,string? Notice,int InternalKB,string? Card,string? Pad);
    StatusKey statusKey;
    /// <summary>ステータスバーを、表示する内容が変わったときだけ書き直す（メッセージがあれば5秒間はそれを出す）。</summary>
    void UpdateStatus() {
        if(core is null)return;
        if(notice is not null && clock.Elapsed.TotalSeconds>=noticeUntil)notice=null;
        var ml=MachineCodeArea.Read(core);
        var key=new StatusKey(core,Native.e500_status(core.Handle),speedIndex,ml?.Start??-1,paused,Native.e500_disk_status(core.Handle),core.PrinterConnected,audio?.Error,settings.Muted,writer.Failure,notice,core.InternalKB,core.CardPath,PadStatus());
        if(key==statusKey)return;
        statusKey=key;
        int s=key.Status;bool off=(s&1)!=0;
        // The state: a coloured dot as well as the word; clicked, it pauses or resumes.
        var state=off?new StatusBar.Part('\0',L.T("電源OFF（EscでON）","Power OFF (Esc turns ON)"),Theme.Dark?Color.FromArgb(150,150,150):Color.FromArgb(140,140,140))
            :paused?new StatusBar.Part('\0',L.T("一時停止","Paused"),Color.FromArgb(232,160,0),TogglePause,L.T("押すと再開します","Click to resume"))
            :new StatusBar.Part('\0',L.T("実行中","Running"),Color.FromArgb(16,160,60),TogglePause,L.T("押すと一時停止します","Click to pause"));
        // A message for five seconds; otherwise a failed autosave, which stays until a later one goes through.
        var message=notice is not null?new StatusBar.Part(Theme.Icons.Info,notice)
            :writer.Failure is Exception failed?new StatusBar.Part(Theme.Icons.Warning,L.T("自動保存できません: ","Autosave failed: ")+failed.Message,Warn:true):null;
        string card=core.HasCard?$"+{core.CardKB}KB":"";
        string disk=DiskStatus();int d=key.Disk;
        // The controller's field is there only while one is in use, before the speed;
        // it goes short (to "on"/"off") after the CE-126P's.
        var pad=key.Pad is null?null:new StatusBar.Part(Theme.Icons.Game,key.Pad,Click:()=>TryAction(ShowPad),
            Tip:(padDevice is not null?padDevice.Name+L.T($"\nプリセット: {padConfig.Current.Name}",$"\nPreset: {padConfig.Current.Name}"):L.T("コントローラーが見つかりません","No controller found"))+L.T("\n押すと「コントローラー」を開きます","\nClick to open the Controller window"),
            Short:padDevice is not null?L.T("接続中","on"):L.T("未接続","off"));
        status.ShortOrder=pad is null?[3,4,6,5,2,1,0]:[3,4,6,7,5,2,1,0];
        status.SetParts(state,message,[
            new(Theme.Icons.Chip,$"ROM Ver.{core.RomVersion} ({(core.IsE500S?"E500S":core.IsE650Family?"E650":"E500")})",Tip:$"{core.RomKB}KB ({(core.IsE500S?"E500S":core.IsE650Family?"E650/U6000":"E500")})\n{core.RomPath}",Short:$"Ver.{core.RomVersion}"),
            new(Theme.Icons.Memory,$"RAM {core.InternalKB}KB{card}",Click:()=>TryAction(ConfigureHardware),
                Tip:L.T($"内蔵RAM {core.InternalKB}KB / ",$"Internal RAM {core.InternalKB}KB / ")+(core.HasCard?L.T($"RAMカード {core.CardKB}KB ({core.CardName})",$"RAM card {core.CardKB}KB ({core.CardName})"):L.T("RAMカードなし","no RAM card"))+L.T("\n押すと「設定」を開きます","\nClick to open Settings"),Short:$"{core.InternalKB}KB{card}"),
            new(Theme.Icons.Code,ml is not {Size:>0} m?L.T("機械語 なし","ML none"):L.T($"機械語 {Bytes(m.Size)}",$"ML {Bytes(m.Size)}"),Click:()=>TryAction(ShowMachineCode),
                Tip:(ml is {Size:>0} a?L.T($"機械語エリア {a.Start:X5}〜BFBFF（{a.Size:X}h バイト）\n",$"Machine-code area {a.Start:X5}-BFBFF ({a.Size:X}h bytes)\n"):"")+L.T("押すと大きさを変えられます","Click to change its size"),Short:ml is not {Size:>0} s0?L.T("なし","none"):Bytes(s0.Size)),
            new(Theme.Icons.Drive,$"CE-140F {disk}",Click:()=>TryAction(ShowDisk),Tip:L.T("押すと「CE-140F」を開きます","Click to open the CE-140F window"),Warn:(d&1)!=0 && (d&8)!=0,Short:disk),
            new(Theme.Icons.Print,$"CE-126P {(core.PrinterConnected?L.T("接続","on"):L.T("未接続","off"))}",Click:()=>TryAction(ShowPrinter),Tip:L.T("押すと「CE-126P」を開きます","Click to open the CE-126P window"),Short:core.PrinterConnected?L.T("接続","on"):L.T("未接続","off")),
            new(settings.Muted||audio?.Error is not null?Theme.Icons.Mute:Theme.Icons.Volume,audio?.Error ?? (settings.Muted?L.T("消音","Muted"):L.T("音声ON","Sound on")),
                Click:audio is null?null:ToggleMute,Tip:settings.Muted?L.T("押すと音を出します","Click to unmute"):L.T("押すと消音します","Click to mute"),Warn:audio?.Error is not null,Short:audio?.Error is null && !settings.Muted?"ON":null),
            ..(pad is null?(StatusBar.Part[])[]:[pad]),
            // At the far right, the last field a narrow window gives up.
            new(Theme.Icons.Speed,L.T("速度 ","Speed ")+Speeds[speedIndex],Click:NextSpeed,Tip:L.T("押すと速度を切り替えます（0.5x → 1x → 2x → 4x）","Click for the next speed (0.5x → 1x → 2x → 4x)"),Short:Speeds[speedIndex]),
        ]);
    }
    /// <summary>バイト数を短く（1KB 単位なら KB、それ以外はバイト）。</summary>
    static string Bytes(int n)=>n%1024==0?$"{n/1024}KB":$"{n}B";
    /// <summary>機械語エリアのウィンドウを開く。</summary>
    void ShowMachineCode() {
        if(core is null)throw new InvalidOperationException(L.T("ROMが読み込まれていません。","No ROM is loaded."));
        var area=MachineCodeArea.Read(core);int free=BasicImage.FreeBytes(core);
        if(area is not {} a || free<0)throw new InvalidOperationException(L.T("今の RAM からは機械語エリアを読めません（初期化の前など）。","The machine-code area cannot be read from the RAM now (before it is set up, say)."));
        ShowWindow("machine-code",()=>new MachineCodeDialog(RamLayout.Internal(core),a.Size,LargestMachineCode(a.Size,free)){Apply=size=>{
            try{Notify(ReserveMachineCode(size));return true;}
            catch(Exception ex){MessageBox.Show(ex.Message,L.T("機械語エリア","Machine-code area"),MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}
        }});
    }
    // Room is left for the BASIC program and variables to work in: the ROM stops a
    // request it cannot meet, but one that leaves nothing would stop BASIC instead.
    /// <summary>機械語エリアにできるいちばん大きい値（今の大きさと空きから、BASIC の分を 200h 残して 100h 単位に）。</summary>
    static int LargestMachineCode(int current,int free)=>MachineCodeArea.Largest(current,free);
    /// <summary>機械語エリアを size バイトにする（ROM の確保の処理を打ち込み、メインメニューから BASIC に戻す）。</summary>
    /// <returns>結果のメッセージ</returns>
    /// <summary>確保が終わるのを待って書き込む BIN（名前、ファイルの中身、確保する大きさ）。</summary>
    (string Name,byte[] Data,int Size)? pendingBin;
    /// <summary>確保と読み込みのどちらにするかを聞く処理（テストで差し替える。null なら MessageBox）。</summary>
    internal Func<string,DialogResult>? AskMachineCode;
    // Asked only when the bytes go into the internal RAM below the system bytes and
    // not wholly into the area as it is; the area is taken from the BIN's start
    // up. Yes reserves (at BASIC's prompt) and loads once that is done; No loads
    // as before; Cancel loads nothing.
    /// <summary>機械語エリアの外に書く BIN なら、確保してから読み込むかを聞く。確保するなら始めたことのメッセージ、
    /// このまま読み込むなら null を返す（やめたら例外）。</summary>
    string? MachineCodeOffer(string name,int start,int length) {
        if(core is null || length<=0)return null;
        int bottom=0xc0000-core.InternalKB*1024,end=start+length;
        // Only the RAM really there: an address beyond it lands on a mirror of
        // another part (the program, the stacks), so such a file is refused.
        // 実際にある RAM だけ。範囲外の番地は別の場所（プログラムやスタック）の写しに当たるので断る。
        bool inInternal=start>=bottom && end<=0xc0000,inCard=core.CardKB>0 && start>=0x40000 && end<=0x40000+core.CardKB*1024;
        if(!inInternal && !inCard)throw new InvalidDataException(L.T($"{name} は {start:X5}〜{end-1:X5} に入るファイルですが、この本体の RAM にはその範囲がありません。デバッグ（Ctrl+D）の「BIN書込み」で書く場所を指定してください。",
            $"{name} goes to {start:X5}-{end-1:X5}, which is not in this machine's RAM. Use BIN load in the debugger (Ctrl+D) and give the address."));
        // What is in use there (the program, the variables, the stacks, the work
        // areas, the system bytes) is not written over as it is: only a reservation,
        // which moves it out of the way first, can make the room.
        // 使用中の所（プログラム、変数、スタック、作業域、システム）にはそのまま書かない。確保（先にそれらをずらす）だけができる。
        bool clobbers=(inInternal?RamLayout.Internal(core):RamLayout.Card(core)).Any(r=>r.Kind is not (RegionKind.Free or RegionKind.MachineCode) && r.Start<end && start<r.End);
        string inUse=L.T($"{name} は {start:X5}〜{end-1:X5} に入りますが、ここには使用中の所（BASIC のプログラム・変数・作業域など）があり、そのまま書くと壊れます。",
            $"{name} goes to {start:X5}-{end-1:X5}, where parts in use (the BASIC program, variables, work areas) are; writing it there as it is would break them.");
        if(!inInternal || end>0xbfc00 || MachineCodeArea.Read(core) is not {} area || start>=area.Start) {
            if(clobbers)throw new InvalidDataException(inUse+L.T(" デバッグ（Ctrl+D）の「BIN書込み」なら書けます。"," BIN load in the debugger (Ctrl+D) can still write it."));
            return null;
        }
        int size=0xbfc00-start,free=BasicImage.FreeBytes(core);
        int mode=Native.e500_symbols(core.Handle,0);
        bool canReserve=free>=0 && size<=LargestMachineCode(area.Size,free) && (mode&1)==0 && (mode&6)!=0 && !paused;
        if(clobbers) {
            if(!canReserve)throw new InvalidDataException(inUse+L.T(size>LargestMachineCode(area.Size,free)?" 空きが足りないため、機械語エリアとしても確保できません。":" BASIC の RUN か PRO の表示で読み込むと、先に機械語エリアを確保できます。",
                size>LargestMachineCode(area.Size,free)?" There is not enough free RAM to reserve it as the machine-code area either.":" Load it at BASIC's RUN or PRO prompt to reserve the machine-code area first."));
            string reserve=inUse+L.T($"\n\n機械語エリアを {size:X}h バイト（{start:X5}〜BFBFF）確保してから読み込みますか？",$"\n\nReserve {size:X}h bytes ({start:X5}-BFBFF) for machine code first, then load?");
            var yes=AskMachineCode?.Invoke(reserve)??(testing?DialogResult.Yes:MessageBox.Show(this,reserve,L.T("機械語エリア","Machine-code area"),MessageBoxButtons.YesNo,MessageBoxIcon.Question));
            if(yes!=DialogResult.Yes)throw new OperationCanceledException(L.T("読み込みをやめました。","Loading was cancelled."));
            ReserveMachineCode(size);
            return L.T($"機械語エリアを {size:X}h バイト確保してから、{name} を読み込みます。",$"Reserving {size:X}h bytes for machine code, then loading {name}.");
        }
        string where=L.T($"{name} は {start:X5}〜{end-1:X5} に入りますが、ここは機械語エリアの外（BASIC の空き）です。BASIC のプログラムや変数に上書きされることがあります。",
            $"{name} goes to {start:X5}-{end-1:X5}, outside the machine-code area (in BASIC's free space), where the BASIC program or variables can overwrite it.");
        string question=canReserve
            ?where+L.T($"\n\n機械語エリアを {size:X}h バイト（{start:X5}〜BFBFF）確保してから読み込みますか？\n「いいえ」はこのまま読み込みます。",$"\n\nReserve {size:X}h bytes ({start:X5}-BFBFF) for machine code first, then load?\nNo loads it as it is.")
            :where+L.T(size>LargestMachineCode(area.Size,free)?"\n\n空きが足りないため、ここまでは確保できません。このまま読み込みますか？":"\n\n確保は BASIC の RUN か PRO の表示でできます。このまま読み込みますか？",
                size>LargestMachineCode(area.Size,free)?"\n\nThere is not enough free RAM to reserve that much. Load it as it is?":"\n\nReserving needs BASIC's RUN or PRO prompt. Load it as it is?");
        var answer=AskMachineCode?.Invoke(question)??(testing?(canReserve?DialogResult.No:DialogResult.Yes):
            MessageBox.Show(this,question,L.T("機械語エリア","Machine-code area"),canReserve?MessageBoxButtons.YesNoCancel:MessageBoxButtons.YesNo,MessageBoxIcon.Question));
        if(answer==DialogResult.Cancel || (!canReserve && answer==DialogResult.No))throw new OperationCanceledException(L.T("読み込みをやめました。","Loading was cancelled."));
        if(!canReserve || answer!=DialogResult.Yes)return null;
        ReserveMachineCode(size);
        return L.T($"機械語エリアを {size:X}h バイト確保してから、{name} を読み込みます。",$"Reserving {size:X}h bytes for machine code, then loading {name}.");
    }
    // Once the typed reservation has gone through (nothing left to type), the BIN
    // goes in if the area came out as asked; otherwise nothing is written.
    /// <summary>確保を待っている BIN があれば、確保が終わったところで書き込む。</summary>
    void FinishPendingBin() {
        if(pendingBin is not {} job || core is null || inputQueue.Count>0 || inputWait>0)return;
        pendingBin=null;
        if(MachineCodeArea.Read(core) is not {Size:var got} || got<job.Size){Notify(L.T($"機械語エリアを確保できなかったため、{job.Name} は読み込みませんでした。",$"The machine-code area was not reserved, so {job.Name} was not loaded."));return;}
        try {
            var (address,length)=BinaryTransfer.Load(core,job.Data,0,true);
            UpdateDisplay();
            Notify(L.T($"機械語エリアを確保し、{job.Name} を {address:X5}-{address+length-1:X5} に書き込みました。",$"Reserved the machine-code area and wrote {job.Name} to {address:X5}-{address+length-1:X5}."));
        } catch(Exception ex) {Notify(ex.Message);}
    }
    internal string ReserveMachineCode(int size) {
        if(core is null)throw new InvalidOperationException(L.T("ROMが読み込まれていません。","No ROM is loaded."));
        if(paused)throw new InvalidOperationException(L.T("一時停止を解除してください。","Resume the machine first."));
        var area=MachineCodeArea.Read(core);int free=BasicImage.FreeBytes(core);
        if(area is not {} a || free<0)throw new InvalidOperationException(L.T("今の RAM からは機械語エリアを読めません。","The machine-code area cannot be read from the RAM now."));
        if(size<0 || size>LargestMachineCode(a.Size,free))throw new InvalidOperationException(L.T($"大きすぎます。いちばん大きくて {LargestMachineCode(a.Size,free):X}h バイトです。",$"Too large: the most is {LargestMachineCode(a.Size,free):X}h bytes."));
        if(size==a.Size)return L.T("機械語エリアの大きさは変わりません。","The machine-code area stays the size it is.");
        int mode=Native.e500_symbols(core.Handle,0);
        if((mode&1)!=0)throw new InvalidOperationException(L.T("プログラムの実行中です。BRK（Esc）で止めてから行ってください。","A program is running. Stop it with BRK (Esc) first."));
        if((mode&6)==0)throw new InvalidOperationException(L.T("BASIC の RUN モードか PRO モードにしてから行ってください（BASIC キー、PC の F7）。","Switch to BASIC's RUN or PRO mode first (the BASIC key, F7 on the PC)."));
        QueueKey(Keyboard.Codes["C-CE"]);
        PasteText(MachineCodeArea.Command(size)+"\n");
        // The ROM ends at the main menu; a second later, BASIC brings the prompt back.
        inputQueue.Enqueue((Keyboard.Codes["BASIC"],false,CpuHz));
        QueueKey(Keyboard.Codes["BASIC"]);
        return size==0?L.T("機械語エリアを返しています。","Giving the machine-code area back.")
            :L.T($"機械語エリアを {size:X}h バイト（{size} バイト）にしています。",$"Making the machine-code area {size:X}h bytes ({size} bytes).");
    }
    /// <summary>消音を切り替えて、設定に残す（ステータスバーの音の欄から）。</summary>
    void ToggleMute() {
        settings=settings with{Muted=!settings.Muted};if(audio is not null)audio.Muted=settings.Muted;
        SaveSettings();UpdateStatus();
    }
    /// <summary>ステータスバーに出す CE-140F の状態（未接続・エラー・通信中・読取専用・接続）。</summary>
    string DiskStatus() {
        if(core is null)return L.T("未接続","Not connected");int d=Native.e500_disk_status(core.Handle);
        return (d&1)==0?L.T("未接続","off"):(d&8)!=0?L.T("エラー","error"):(d&4)!=0?L.T("通信中","busy"):(d&2)!=0?L.T("読取専用","read-only"):L.T("接続","on");
    }
    /// <summary>ROM を開き直す（今のコアは RAM などを保存してから破棄する）。</summary>
    /// <param name="path">ROM ファイル</param>
    /// <param name="restore">保存してある RAM と状態を復元するか</param>
    void SwitchRom(string path,bool restore) {
        var next=new Core(path,settings.InternalKB,settings.CardPath);
        // The saved RAM goes into the new machine before it takes over. A file that
        // cannot be read (held by a sync client, the wrong size) then leaves the
        // machine that was running - or none - instead of a blank one, which the
        // next autosave would have written over the user's RAM file.
        // 保存した RAM は、差し替える前に新しいコアへ読む。読めなければ、それまでのコア（なければなし）のまま
        // （空のコアにすると、次の自動保存が利用者の RAM ファイルを上書きしてしまう）。
        try {
            if(core is not null) {ReleaseKeys();SavePersistent();}
            if(restore && !testing)next.MemoryFiles(MemoryDirectory(next),true);
        }
        catch {next.Dispose();throw;}
        core?.Dispose();core=next;surface.Core=core;audio?.Flush();
        core.PrinterConnected=settings.PrinterConnected;core.Paper=paper;
        if(settings.InternalKB!=core.InternalKB) {
            settings=settings with{InternalKB=core.InternalKB};
            SaveSettings();
        }
        // An open settings window offers the RAM sizes of the machine now running.
        if(windows.TryGetValue("hardware",out var open) && open is HardwareDialog hardware && !hardware.IsDisposed)hardware.SetRom(core.StandardRamKB,settings.InternalKB);
        paused=false;pause.Text=PauseCommand;
        // The drive goes in before the snapshot, which may hold its place in a
        // device code; mounted afterwards it would start that code over. A drive
        // that cannot be mounted must not cost the restore, or the RAM files
        // would be saved over with a blank machine: its error is shown after,
        // and not thrown, since the ROM switch itself has gone through (settings
        // already applied must not be reported as refused).
        // ドライブは状態の復元より前に接続する（状態がコードの途中を持つことがあるため）。
        // 接続に失敗しても復元は続け、エラーは最後に表示する（空の RAM で保存ファイルを上書きしないため）。
        // ROM の切り替えは済んでいるので、例外にはしない（適用済みの設定を失敗と見せないため）。
        Exception? mountError=testing?null:TryMountConfiguredDisk(settings);
        try {
            if(restore && !testing) {
                // 自動保存の状態は、RAM のファイル以上に新しいときだけ復元する。
                string snapshot=AutoSavePath();
                if(!File.Exists(snapshot)&&settings.InternalKB==32&&core.CardKB==0)snapshot=Path.Combine(root,"state",core.Hash+".e500state");
                // The card image is always loaded as it is on disk; the snapshot only
                // resumes the CPU and internal RAM when it is at least as new as they are.
                string?[] banks=[core.InternalFile(MemoryDirectory()),core.CardPath];
                DateTime newest=banks.Where(File.Exists).Select(f=>File.GetLastWriteTimeUtc(f!)).DefaultIfEmpty(DateTime.MinValue).Max();
                if(File.Exists(snapshot)&&File.GetLastWriteTimeUtc(snapshot)>=newest) {
                    try {core.Load(snapshot);}
                    catch(Exception ex) {MessageBox.Show(L.T("状態を復元できなかったため、保存RAMから起動します。\n","Could not restore the snapshot, starting from the saved RAM.\n")+ex.Message,"PC-E500");}
                }
            }
            core.Run(1024000);
            Native.e500_audio(core.Handle,soundBuffer,soundBuffer.Length);UpdateDisplay();previous=clock.Elapsed.TotalSeconds;
        }
        // Shown even when a step above has thrown, so the drive's error is not lost to it.
        // 上の処理が例外を投げても、CE-140F のエラーは失わずに表示する。
        finally {ReportMountError(mountError);}
    }
    /// <summary>設定どおりに CE-140F を接続する（設定で切っていれば取り外す）。</summary>
    /// <param name="target">接続するコア</param>
    /// <param name="s">使う設定</param>
    /// <exception cref="IOException">フォルダを開けない</exception>
    static void MountConfiguredDisk(Core target,HardwareSettings s)=>target.MountDisk(s.DiskEnabled?s.DiskFolder:null,s.DiskReadOnly);
    // For a step a drive that cannot be mounted must not stop (the restore of a
    // ROM switch): the error comes back, to be shown once the step is done. The
    // drive is left as it was - on a new core, unmounted.
    /// <summary>設定どおりに CE-140F を接続し、失敗したらその例外を返す（成功なら null）。
    /// 失敗したら、ドライブはそれまでのまま（新しいコアなら取り外した状態）。</summary>
    /// <param name="s">使う設定</param>
    Exception? TryMountConfiguredDisk(HardwareSettings s) {
        try {MountConfiguredDisk(core!,s);return null;}
        catch(Exception ex) {return ex;}
    }
    /// <summary>接続できなかった CE-140F のエラーを表示する（操作そのものは済んでいる）。</summary>
    /// <param name="error">TryMountConfiguredDisk の結果（null なら何もしない）</param>
    void ReportMountError(Exception? error) {
        if(error is null)return;
        MessageBox.Show(L.T("CE-140Fを接続できないため、外した状態で続けます。\n","The CE-140F could not be connected, and stays disconnected.\n")+error.Message,"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    }
    /// <summary>サブウィンドウを開く。同じ種類がもう開いていれば、それを前に出す。</summary>
    /// <param name="key">ウィンドウの種類</param>
    /// <param name="create">新しく作る処理</param>
    /// <returns>開いたウィンドウ</returns>
    Form ShowWindow(string key,Func<Form> create) {
        if(windows.TryGetValue(key,out var open)&&!open.IsDisposed) {
            if(open.WindowState==FormWindowState.Minimized)open.WindowState=FormWindowState.Normal;
            open.Activate();return open;
        }
        var window=create();windows[key]=window;
        window.FormClosed+=(_,_)=>windows.Remove(key);
        window.Show(this);return window;
    }
    /// <summary>「設定」ウィンドウを開く。</summary>
    void ConfigureHardware() {
        ShowWindow("hardware",()=>{
            Directory.CreateDirectory(CardFolder());
            var dialog=new HardwareDialog(settings,core?.StandardRamKB??32,CardFolder()){InUse=()=>core?.CardPath};
            dialog.Apply=edit=>{
                try {ReleaseKeys();ApplyHardware(edit(settings));return true;}
                catch(Exception ex) {MessageBox.Show(ex.Message,"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Error);return false;}
            };
            return dialog;
        });
    }
    /// <summary>設定を適用する。RAM の構成が変われば ROM を開き直し、言語が変われば再起動する。</summary>
    /// <param name="next">新しい設定</param>
    /// <exception cref="InvalidOperationException">今は状態を保存できない</exception>
    void ApplyHardware(HardwareSettings next) {
        if(next.DiskEnabled&&string.IsNullOrWhiteSpace(next.DiskFolder))throw new InvalidDataException(L.T("CE-140Fの保存先を指定してください。","Specify the CE-140F folder."));
        bool memory=next.InternalKB!=settings.InternalKB||
            !string.Equals(next.CardPath,settings.CardPath,StringComparison.OrdinalIgnoreCase);
        // A CPU stopped on a fault has no state worth saving, and must not be kept
        // from the settings that may be what gets it going again.
        // CPU のエラー中は状態を保存できないが、設定の変更は断らない（保存は省く）。
        if(core is not null&&!core.CanSnapshot&&!core.Faulted)throw new InvalidOperationException(L.T("設定を変える前に状態を保存します。","Settings save the state first. ")+Core.DiskBusy);
        // A folder that cannot be opened refuses the settings and leaves the drive
        // as it was, with a new memory layout too: that is tried on the core that
        // checks the layout, as the new core will mount it (SwitchRom); otherwise
        // the running core changes folders. A new layout saves the machine once,
        // in SwitchRom; otherwise it is saved before the drive changes.
        // 開けないフォルダなら設定を断り、ドライブはそれまでのまま。RAM の構成が変わるときも同じで、
        // 構成を確かめるコアで接続を試す（新しいコアは SwitchRom が接続する）。そうでなければ今のコアを
        // 接続し直す。RAM の構成が変わるときの保存は SwitchRom の1回だけにする。
        if(memory&&core is not null)using(var validation=new Core(core.RomPath,next.InternalKB,next.CardPath))MountConfiguredDisk(validation,next);
        if(!memory)SavePersistent();
        if(core is not null&&!memory)MountConfiguredDisk(core,next);
        bool language=next.Language!=settings.Language || next.Theme!=settings.Theme;
        var before=settings;settings=next;
        // Every window builds its strings when it opens, so a new language takes
        // effect by starting over; the close path saves the machine first, and the
        // new start opens with the new memory layout too.
        // Tests never write the user's settings file, whichever way the apply goes.
        if(language&&!testing){SaveSettings();Application.Restart();return;}
        // The settings are kept only once a new layout is running: a switch that
        // fails leaves the old machine, and the old settings with it.
        // 設定は、新しい RAM の構成で動き出してから保存する（切り替えに失敗したら、設定も元のまま）。
        if(memory&&core is not null) {
            try {SwitchRom(core.RomPath,true);}
            catch {settings=before;throw;}
        }
        SaveSettings();
        SetAppearance(settings.Appearance);
        surface.SetLcd(settings.LcdDotLines,settings.LcdContrast);
        TopMost=settings.TopMost;
        if(audio is not null){audio.Muted=settings.Muted;audio.Volume=settings.Volume/100f;}
        previous=clock.Elapsed.TotalSeconds;UpdateDisplay();
    }
    /// <summary>ウィンドウのタイトル：外観の機種名と、このアプリの版。</summary>
    /// <param name="model">外観の機種名</param>
    internal static string Title(string model)=>$"{model} Emulator v{UpdateCheck.Current} | PokeCom";
    /// <summary>本体の外観を変える。枠のない表示なら、ウィンドウを新しいケースの大きさに合わせ直す（拡大率は保つ）。</summary>
    /// <param name="model">HardwareSettings.Appearances のどれか</param>
    void SetAppearance(string model) {
        float zoom=surface.Zoom;
        surface.SetModel(model);Text=Title(model);
        // A frameless window fits the picture: another case, another size.
        if(view!=WindowView.Normal){ClientSize=ViewSize(zoom);surface.Arrange();RememberView();}
    }
    /// <summary>CE-126P のウィンドウを開く。「接続」の切り替えは設定にも保存する。</summary>
    void ShowPrinter()=>ShowWindow("printer",()=>new PrinterDialog(paper,()=>core?.Font??[],()=>settings.PrinterConnected,printOut,to=>{
        // The printer chosen is kept with the settings, for the next start.
        printOut.Select(to);settings=settings with{PrintTo=to};SaveSettings();
    },on=>{
        // Kept with the settings, so the printer is plugged in or not as it was
        // left. The machine follows the checkbox first; a settings file that cannot
        // be written (locked by a sync, say) is reported, not thrown out of the click.
        settings=settings with{PrinterConnected=on};
        if(core is not null)core.PrinterConnected=on;UpdateDisplay();
        SaveSettings();
    }));
    /// <summary>CE-140F のトレースのウィンドウを開く。</summary>
    void ShowDisk()=>ShowWindow("disk",()=>new DiskTraceDialog(()=>core,()=>settings.DiskFolder,TryAction));
    // These delegates read the field instead of capturing the current core, so a
    // ROM switch that disposes it underneath an open window cannot be observed.
    /// <summary>LCD 文字のコピー・貼り付けのウィンドウを開く。</summary>
    void ShowTextTransfer()=>ShowWindow("text",()=>new ScreenTextDialog(()=>core?.ScreenText()??"",PasteText));
    /// <summary>デバッグウィンドウを開く。</summary>
    void ShowDebugger()=>ShowWindow("debug",()=>new DebugDialog(()=>core));
    /// <summary>コントローラーの設定ファイル。</summary>
    string PadConfigPath=>Path.Combine(root,"state","controller.json");
    /// <summary>「コントローラー」ウィンドウを開く。</summary>
    void ShowPad()=>ShowWindow("controller",()=>new PadDialog(padConfig){Apply=next=>{
        try {
            // A file that could not be read at start is not written over with what was shown instead.
            // 起動時に読めなかったファイルは、代わりに出した既定の設定で上書きしない。
            if(padConfig.Unread && File.Exists(PadConfigPath)){MessageBox.Show(L.T("起動時に controller.json を読めなかったため、上書きせずに残しています。読めるようになってから（同期の完了など）エミュレータを起動し直してください。","controller.json could not be read at start, so it is kept as it is. Start the emulator again once it can be read (after syncing, for example)."),L.T("コントローラー","Controller"),MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}
            if(!testing)next.Save(PadConfigPath);
            ReleasePad();padConfig=next;padDevice=null;padScanAt=0;statusKey=default;UpdateStatus();
            return true;
        } catch(Exception ex) {MessageBox.Show(ex.Message,L.T("コントローラー","Controller"),MessageBoxButtons.OK,MessageBoxIcon.Error);return false;}
    }});
    /// <summary>ステータスバーのコントローラーの欄の文字（使わない設定なら null）。</summary>
    string? PadStatus()=>!padConfig.Enabled?null:padDevice is not null?L.T("コントローラー 接続中","Pad connected"):L.T("コントローラー 未接続","Pad not connected");
    // Looking for pads takes a while (winmm reloads its list: the window stood still
    // for it), so it runs on another thread. It starts every three seconds while
    // none is found, and half a second after Windows last says a device came or
    // went (it says so several times for one plug).
    /// <summary>裏で動いているコントローラーの検索（なければ null）と、機器の抜き差しのあとに探し直す時刻（0 ならなし）。</summary>
    Task<List<PadDevice>>? padScan;
    double padRescanAt;
    // Read on every tick while the window has the focus, as the keyboard is.
    /// <summary>コントローラーを読み、押されているボタンの割り当てのキーを押したままにする（離されたら離す）。</summary>
    void PollPad(double now) {
        if(testing)return;
        if(!padConfig.Enabled){ReleasePad();return;}
        if(padScan is {IsCompleted:true}) {
            var found=GamePad.Pick(padScan.IsCompletedSuccessfully?padScan.Result:[],padConfig.Device);padScan=null;padScanAt=now+3;
            if(found?.Id!=padDevice?.Id || found?.Index!=padDevice?.Index){ReleasePad();padDevice=found;statusKey=default;}
        }
        if(padScan is null && (padRescanAt>0 && now>=padRescanAt || padDevice is null && now>=padScanAt)){padRescanAt=0;padScan=Task.Run(GamePad.Scan);}
        if(padDevice is null)return;
        if(ActiveForm!=this){ReleasePad();return;}
        if(!GamePad.Read(padDevice,padPressed)){padDevice=null;padScanAt=now+1;ReleasePad();statusKey=default;return;}
        PressPad(padPressed);
    }
    /// <summary>押されている操作の割り当てを押したままにし、もう押されていない割り当てを離す。</summary>
    void PressPad(IEnumerable<string> controls) {
        padWanted.Clear();
        var map=padConfig.Current.Map;
        foreach(var c in controls)if(map.TryGetValue(c,out string? b))padWanted.Add(b);
        foreach(var by in holding.Keys.Where(h=>h.Binding is not null && !padWanted.Contains(h.Binding)).ToArray())Unhold(by);
        foreach(var b in padWanted)if(PadConfig.Parse(b,out int code,out int modifier))Hold(new HoldBy(Keys.None,b),code,modifier);
    }
    /// <summary>コントローラーで押したままのキーをすべて離す。</summary>
    void ReleasePad(){foreach(var by in holding.Keys.Where(h=>h.Binding is not null).ToArray())Unhold(by);}
    /// <summary>ROM ファイルを選んで開く。</summary>
    void OpenRom() {
        using var d=new OpenFileDialog{Filter=L.T("PC-E500シリーズの ROM (*.rom;*.bin)|*.rom;*.bin|すべてのファイル|*.*","PC-E500 series ROM (*.rom;*.bin)|*.rom;*.bin|All files|*.*"),InitialDirectory=Path.Combine(root,"rom")};
        if(d.ShowDialog(this)!=DialogResult.OK)return;
        SwitchRom(d.FileName,true);
        // Opened once, it is the ROM of the next start too (kept only once it runs).
        // 開いた ROM は、次の起動でも開く（動き出してから設定に残す）。
        settings=settings with{RomFile=core!.RomPath};SaveSettings();
    }
    /// <summary>「プログラム読込」：BAS か BIN のファイルを選んで、本体に読み込む。</summary>
    // The check box is remembered, and a file dropped on the window goes the same way.
    // チェックは設定に残し、ウィンドウに落としたファイルも同じにする。
    void LoadFile() {
        if(core is null)throw new InvalidOperationException(L.T("ROMが読み込まれていません。","No ROM is loaded."));
        bool run=settings.LoadRun;
        string? path=FileDialogs.OpenWithCheck(this,[(L.T("BASIC・機械語 (*.bas;*.bin;*.txt)","BASIC and machine code (*.bas;*.bin;*.txt)"),"*.bas;*.bin;*.txt"),(L.T("すべてのファイル","All files"),"*.*")],
            settings.DiskFolder,L.T("読み込んだら実行する（BAS は RUN）","Run after loading (RUN for a BAS)"),ref run);
        if(run!=settings.LoadRun){settings=settings with{LoadRun=run};SaveSettings();}
        if(path is not null)Notify(LoadProgramFile(path,run));
    }
    // A machine-code file with the SAVE M header goes straight into memory at the
    // address the header gives, in any mode. Anything else is a BASIC program,
    // text or the machine's own tokenised form: the ROM reads either from the
    // CE-140F, so the file is put in the drive's folder (under a name the drive
    // takes) and LOAD "X:..." is typed, as the user would.
    // SAVE M のヘッダーがある機械語は、ヘッダーのアドレスへそのまま書く（どのモードでも）。ほかは BASIC のプログラム
    //（テキストでも中間コードでも ROM が読む）として、CE-140F のフォルダに置き、LOAD "X:..." を打つ。
    /// <summary>BAS・BIN のファイルを本体に読み込む。</summary>
    /// <param name="path">読み込むファイル</param>
    /// <param name="run">BAS なら読み込んだあと実行する（LOAD "X:...",R）</param>
    /// <returns>結果のメッセージ</returns>
    internal string LoadProgramFile(string path,bool run) {
        if(core is null)throw new InvalidOperationException(L.T("ROMが読み込まれていません。","No ROM is loaded."));
        if(paused)throw new InvalidOperationException(L.T("一時停止を解除してから読み込んでください。","Resume the machine before loading."));
        // One load at a time: the last one may still be typing (a reservation, a LOAD).
        // 読み込みは1つずつ（前の読み込みが、確保や LOAD をまだ打っていることがある）。
        if(pendingBin is not null || inputQueue.Count>0)throw new InvalidOperationException(L.T("前の読み込み（キー入力）が終わってから読み込んでください。","Wait for the last load (keys still being typed) to finish."));
        var info=new FileInfo(path);
        if(!info.Exists)throw new FileNotFoundException(L.T("ファイルが見つかりません。","The file was not found."),path);
        if(info.Length>0x100000+BinaryTransfer.HeaderLength)throw new InvalidDataException(L.T("1MBを超えるファイルは読み込めません。","Files larger than 1MB cannot be loaded."));
        byte[] data=File.ReadAllBytes(path);
        bool bin=string.Equals(info.Extension,".bin",StringComparison.OrdinalIgnoreCase);
        if(BinaryTransfer.TryReadHeader(data,out int binStart,out int binLength)) {
            // Machine code outside the machine-code area sits in BASIC's free space,
            // where the program, the variables or a later reservation can overwrite
            // it: offer to take the area down to its start first, then load.
            // 機械語エリアの外の機械語は BASIC の空きにあり、上書きされうるので、先に確保してから読み込むかを聞く。
            if(MachineCodeOffer(info.Name,binStart,binLength) is string reserving){pendingBin=(info.Name,data,0xbfc00-binStart);return reserving;}
            var (address,length)=BinaryTransfer.Load(core,data,0,true);
            UpdateDisplay();
            return L.T($"{info.Name} を {address:X5}-{address+length-1:X5} に書き込みました（{length}バイト）。",$"Wrote {info.Name} to {address:X5}-{address+length-1:X5} ({length} bytes).");
        }
        // A program the machine saved goes straight into RAM: no LOAD, no CE-140F,
        // any mode. A layout this does not know, or a text file, goes on to LOAD.
        // 本体が保存した形のプログラムは RAM に直接入れる（LOAD も CE-140F も使わず、どのモードでも）。
        if(!bin && BasicImage.IsSaved(data)) {
            if((Native.e500_symbols(core.Handle,0)&1)!=0)throw new InvalidOperationException(L.T("プログラムの実行中です。BRK（Esc）で止めてから読み込んでください。","A program is running. Stop it with BRK (Esc) first."));
            if(BasicImage.Load(core,data)) {
                UpdateDisplay();
                string loaded=L.T($"{info.Name} を RAM に直接読み込みました（{data.Length}バイト）。",$"Loaded {info.Name} straight into RAM ({data.Length} bytes).");
                if(!run)return loaded;
                // RUN is typed at BASIC's prompt only; elsewhere it would be a stray key.
                if((Native.e500_symbols(core.Handle,0)&6)==0)return loaded+L.T(" 実行は BASIC の RUN か PRO モードで RUN と打ってください。"," To run it, type RUN in BASIC's RUN or PRO mode.");
                QueueKey(Keyboard.Codes["C-CE"]);PasteText("RUN\n");
                return L.T($"{info.Name} を RAM に直接読み込んで実行します。",$"Loaded {info.Name} straight into RAM and running it.");
            }
        }
        if(bin)throw new InvalidDataException(L.T("SAVE M のヘッダーがない BIN は、書き込むアドレスが分かりません。デバッグ（Ctrl+D）の「BIN書込み」でアドレスを指定してください。","A BIN without the SAVE M header does not say where it goes. Use BIN load in the debugger (Ctrl+D) and give the address."));
        // A BASIC program goes through the CE-140F, with the machine at its BASIC prompt.
        if((Native.e500_disk_status(core.Handle)&1)==0)throw new InvalidOperationException(L.T("BAS の読み込みには CE-140F を使います。「設定」で CE-140F を接続してください。","A BAS file loads through the CE-140F. Connect the CE-140F in Settings."));
        int mode=Native.e500_symbols(core.Handle,0);
        if((mode&1)!=0)throw new InvalidOperationException(L.T("プログラムの実行中です。BRK（Esc）で止めてから読み込んでください。","A program is running. Stop it with BRK (Esc) first."));
        if((mode&6)==0)throw new InvalidOperationException(L.T("BASIC の RUN モードか PRO モードにしてから読み込んでください（BASIC キー、PC の F7）。","Switch to BASIC's RUN or PRO mode first (the BASIC key, F7 on the PC)."));
        string name=DriveName(info.Name),target=Path.Combine(settings.DiskFolder,name);
        if(!string.Equals(Path.GetFullPath(target),info.FullName,StringComparison.OrdinalIgnoreCase)) {
            if(File.Exists(target) && !File.ReadAllBytes(target).AsSpan().SequenceEqual(data) && !testing &&
                MessageBox.Show(this,L.T($"CE-140F のフォルダに、中身の違う {name} があります。上書きしますか？",$"The CE-140F folder has another {name}. Overwrite it?"),L.T("プログラム読込","Load program"),MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)
                return L.T("読み込みをやめました。","Loading was cancelled.");
            File.WriteAllBytes(target,data);
        }
        // C-CE first: whatever is on the line is not run together with the command.
        QueueKey(Keyboard.Codes["C-CE"]);
        // ,R runs the program once it is in (the ROM's own LOAD option).
        string command=$"LOAD \"X:{name}\""+(run?",R":"");
        PasteText(command+"\n");
        return run?L.T($"{name} を CE-140F から読み込んで実行します（{command}）。",$"Loading and running {name} from the CE-140F ({command}).")
            :L.T($"{name} を CE-140F から読み込んでいます（{command}）。",$"Loading {name} from the CE-140F ({command}).");
    }
    // The CE-140F takes an 8.3 name of A-Z, 0-9, _ and -. A PC name is cut down to
    // that: "my prog.bas" is MYPROG.BAS; one with nothing left is LOAD.BAS.
    /// <summary>PC のファイル名を、CE-140F が受け付ける 8.3 の名前（英大文字・数字・_・-）にする。</summary>
    /// <param name="fileName">PC のファイル名</param>
    internal static string DriveName(string fileName) {
        static string Keep(string s,int max)=>new string(s.ToUpperInvariant().Where(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-').Take(max).ToArray());
        string stem=Keep(Path.GetFileNameWithoutExtension(fileName),8),extension=Keep(Path.GetExtension(fileName).TrimStart('.'),3);
        if(stem.Length==0)stem="LOAD";
        // The device names are refused by the drive.
        if(stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length==4 && (stem.StartsWith("COM",StringComparison.Ordinal) || stem.StartsWith("LPT",StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9'))stem="_"+stem;
        return extension.Length==0?stem+".BAS":stem+"."+extension;
    }
    /// <summary>状態をファイルに保存する（保存先を選ぶ）。</summary>
    void SaveState() {
        if(core is null)return;ReleaseKeys();
        using var d=new SaveFileDialog{Filter=L.T("PC-E500シリーズ state|*.e500state","PC-E500 series state|*.e500state"),FileName=settings.Appearance+".e500state"};
        if(d.ShowDialog(this)==DialogResult.OK)core.Save(d.FileName);
    }
    /// <summary>状態をファイルから読み込む。</summary>
    void LoadState() {
        if(core is null)return;ReleaseKeys();
        if(!core.CanLoad)throw new InvalidOperationException(Core.CannotLoad);
        using var d=new OpenFileDialog{Filter=L.T("PC-E500シリーズ state|*.e500state","PC-E500 series state|*.e500state")};
        if(d.ShowDialog(this)==DialogResult.OK) {
            // What the machine holds now goes to its RAM file first: the state
            // loaded may bring another RAM size, and these changes belong to this one.
            // 読み込む前に今の RAM を保存する（読み込む状態は別の容量のことがあり、今の変更は今の容量のもの）。
            SavePersistent();
            core.Load(d.FileName);
            // The snapshot brings its own internal RAM size; kept, so the next start opens with it.
            if(settings.InternalKB!=core.InternalKB){settings=settings with{InternalKB=core.InternalKB};SaveSettings();}
            audio?.Flush();UpdateDisplay();
        }
    }
    /// <summary>RESET 穴：RAM を保存してから CPU を再起動する（RAM は消さない）。</summary>
    void ResetPin() {
        if(core is null)return;
        ReleaseKeys();SavePersistent();Native.e500_reset(core.Handle,0);audio?.Flush();
        core.Run(1024000);
        UpdateDisplay();
    }
    // The ON+RESET gesture of the real machine: the reset pin is released with ON
    // still down, and the ROM answers with its own "スベテノ データヲ ショウキョ
    // シマスカ? (Y/N)" prompt. The emulator only has to hold the key across the
    // reset; everything the prompt does afterwards is the ROM's own code.
    /// <summary>オールリセット（実機の ON＋RESET）：状態を残してからリセットし、ROM の確認画面を出す。</summary>
    void AllReset() {
        if(core is null)return;
        // The backup below must be possible before anything is cleared - unless the
        // CPU has stopped on a fault, when there is no state to keep and a reset is
        // the way out; the RAM files are still written.
        // バックアップの保存ができなければ断る。ただし CPU のエラー中は、立ち直るための
        // リセットなので断らず、状態のバックアップだけを省く（RAM のファイルは保存する）。
        if(!core.CanSnapshot&&!core.Faulted)throw new InvalidOperationException(L.T("リセットの前に状態を保存します。","All Reset saves the state first. ")+Core.DiskBusy);
        ReleaseKeys();SavePersistent();
        if(!testing&&core.CanSnapshot)core.Save(Path.Combine(root,"state","before-reset-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".e500state"));
        // The reset closes the CE-140F's files and transfer too, so a program
        // stopped on a fault leaves nothing to hold off the saves after.
        // リセットは CE-140F の開いたファイルと転送も閉じる（CPU のエラーで止まったプログラムの分も残らない）。
        int on=Keyboard.Codes["ON/BRK"];
        Native.e500_reset(core.Handle,0);
        core.Key(on,true);core.Run(2048000);core.Key(on,false);
        audio?.Flush();core.Run(1024000);UpdateDisplay();
        Notify(L.T("オールリセット: 画面のY／Nで、全データを消去するか選んでください。","All reset: answer Y/N on the screen to choose whether to erase all data."));
    }
    // The HTML manual beside the executable, in the UI language; the summary
    // box stands in when it is missing.
    /// <summary>使い方（HTML のマニュアル）を開く。なければ要約を表示する。</summary>
    void Help() {
        string manual=Path.Combine(programRoot,"manual",L.En?"index_en.html":"index.html");
        if(File.Exists(manual)){Process.Start(new ProcessStartInfo(manual){UseShellExecute=true});return;}
        HelpSummary();
    }
    /// <summary>使い方の要約をメッセージで表示する。</summary>
    void HelpSummary() => MessageBox.Show(L.En?HelpEnglish:"初回：PF1 → BASIC（PCのF7）\nRUN表示：1+2 → Enter、BEEP 1 → Enter\nプログラム：BASICでPROに切替 → 10 PRINT 123 → Enter\nBASICでRUNに戻す → RUN → Enter\n\n文字入出力：LCD文字の範囲コピー／クリップボードからキー入力\nCtrl+C：文字入出力を開く / Ctrl+V：直接貼り付け\nCtrl+D：デバッグ（CPUレジスタ、内部RAM、メモリの表示・書換え、BINの読み書き）\nCE-140F：SAVE \"X:TEST.BAS\" / LOAD \"X:TEST.BAS\"\n保存先・書込み禁止は「設定」で変更できます。\n内蔵RAMは容量ごとに内容を保存し、OFFやアプリ終了でも保持します。\nRAMカードは「設定」でイメージファイルを作成・装着・取り外しします。内容はそのファイルへ書き込みます。\nE650/U6000の384KB ROMでは内蔵RAMは実機標準の64KB、増設改造相当の128KB・256KBから選べます。\nRESET穴はCPUを再起動し、内蔵RAMとカードを保持します。\n「オールリセット」は実機のON＋RESETと同じで、ROMが「スベテノ データヲ ショウキョ シマスカ?」とY／Nで聞いてきます。\n\nSHIFT / CTRLをクリックすると次のキーまで保持します。\nF8は2ndF、EscはON/BRKです。\n詳細はEmulatorフォルダのMANUAL.mdを参照してください。", L.T($"使い方 / PC-E500シリーズ v{UpdateCheck.Current}",$"Help / PC-E500 series v{UpdateCheck.Current}"));
    const string HelpEnglish="First run: PF1 → BASIC (F7 on the PC)\nRUN mode: 1+2 → Enter, BEEP 1 → Enter\nProgram: BASIC switches to PRO → 10 PRINT 123 → Enter\nBASIC back to RUN → RUN → Enter\n\nText I/O: copy LCD text / type from the clipboard\nCtrl+C: open Text I/O / Ctrl+V: paste directly\nCtrl+D: Debug (registers, internal RAM, memory view and editing, BIN load/save)\nCE-140F: SAVE \"X:TEST.BAS\" / LOAD \"X:TEST.BAS\"\nFolder and write protection are in Settings.\nInternal RAM is kept per capacity and survives OFF and exit.\nRAM cards are image files created, attached and detached in Settings; contents are written to that file.\nE650/U6000 384KB ROMs offer 64KB (stock) or 128KB/256KB (expansion mod) internal RAM.\nThe RESET hole restarts the CPU and keeps internal RAM and the card.\nAll Reset is the real ON+RESET: the ROM asks whether to erase all data (Y/N).\n\nClicking SHIFT / CTRL holds it for the next key.\nF8 is 2ndF, Esc is ON/BRK.\nSee MANUAL_en.md in the Emulator folder for details.";
    /// <summary>キーを離してから action を実行し、例外はメッセージで示す。</summary>
    // A test run shows no dialog: hidden and waited on by build.ps1 and
    // test.ps1, one would hold the build up for ever. The error is thrown instead.
    // テストではダイアログを出さず、例外にする（隠したウィンドウでダイアログを待ち続けないように）。
    void TryAction(Action action) {
        ReleaseKeys(true);
        try {action();}
        // Cancelled by the user (a question answered Cancel): said, not shown as an error.
        catch(OperationCanceledException ex) {if(testing)throw;Notify(ex.Message);}
        catch(Exception ex) {if(testing)throw;MessageBox.Show(ex.Message,"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    // 以下は自動テスト（SmokeTest・ManualShots）用の入口。
    internal Core TestCore=>core??throw new InvalidOperationException("ROM not loaded");
    internal string TestRoot=>Path.TrimEndingDirectorySeparator(root);
    internal void TestRefresh()=>UpdateDisplay();
    internal void TestDotLines(bool show)=>SetLcd(show,surface.Contrast);
    internal WindowView TestView=>view;
    internal void TestSetView(WindowView next)=>SetView(next);
    internal void TestDoubleClick()=>CycleView();
    internal void TestRestoreView(HardwareSettings s)=>RestoreView(s);
    internal void TestShowUpdate(Release release)=>ShowUpdate(release,false);
    internal void TestSkipUpdate()=>SkipUpdate();
    internal (bool Visible,string Text,bool InMenu) TestUpdateNotice=>(updateNotice.Visible,updateNotice.Text,updateItem.Available);
    internal ContextMenuStrip TestMenu=>menu;
    internal IEnumerable<string> TestToolbar=>main.Controls[0].Controls.Cast<Control>().Where(c=>c.Visible).Select(c=>c.Text);
    /// <summary>ツールバーの部品（説明書の画像で、全部の段を写すため）。</summary>
    internal Control TestToolbarPanel=>main.Controls[0];
    internal DisplayDialog TestDisplay()=>(DisplayDialog)TestWindow("display");
    internal HardwareSettings TestSettings=>settings;
    internal void TestAppearance(string model)=>SetAppearance(model);
    internal void TestApplyHardware(HardwareSettings next)=>ApplyHardware(next);
    internal string TestStatus=>status.Text;
    internal void TestClearNotice(){notice=null;statusKey=default;UpdateStatus();}
    internal StatusBar TestStatusBar=>status;
    internal bool TestPaused=>paused;
    internal bool TestHasWindow(string key)=>windows.TryGetValue(key,out var w) && !w.IsDisposed;
    internal void TestPhysicalKey(string id)=>surface.TriggerForTest(id);
    internal void TestAllReset()=>AllReset();
    internal Form TestWindow(string key) {
        (key switch{"debug"=>(Action)ShowDebugger,"display"=>ShowDisplay,"hardware"=>ConfigureHardware,"disk"=>ShowDisk,"printer"=>ShowPrinter,"text"=>ShowTextTransfer,_=>throw new ArgumentException(key)})();
        return windows[key];
    }
    internal DebugDialog TestDebugger()=>(DebugDialog)TestWindow("debug");
    internal int TestFocusableButtons() {
        int count=0;
        void Walk(Control parent){foreach(Control child in parent.Controls){if(child is Button b && b.CanSelect)count++;Walk(child);}}
        Walk(this);return count;
    }
    internal int TestKeyCycles(string key) {
        ReleaseKeys();QueueKey(Keyboard.Codes[key]);
        int total=inputWait+inputQueue.Sum(x=>x.Cycles);
        ReleaseKeys();return total;
    }
    internal string TestReserveMachineCode(int size){string said=ReserveMachineCode(size);TestSettle();return said;}
    internal string TestLoadFile(string path,bool run=false) {
        string result=LoadProgramFile(path,run);TestSettle();return result;
    }
    internal void TestKeepPowerOn()=>KeepPowerOn();
    internal void TestAfterimage(bool on)=>SetAfterimage(on);
    internal string TestPaste(string text) {
        string result=PasteText(text);TestSettle();return result;
    }
    internal void TestRunCycles(int cycles)=>RunCycles(cycles);
    internal PocketSurface TestSurface=>surface;
    internal PrinterPaper TestPaper=>paper;
    internal PrintOutput TestPrintOut=>printOut;
    internal bool TestPrinterSetting=>settings.PrinterConnected;
    internal void TestRepeat(Keys key,char ch){OnKeyDown(this,new KeyEventArgs(key));OnKeyPress(this,new KeyPressEventArgs(ch));}
    internal void TestRepeatKey(Keys key){Message m=new();ProcessCmdKey(ref m,key);}
    internal void TestRelease(Keys key)=>OnKeyUp(this,new KeyEventArgs(key));    internal void TestPad(params string[] controls)=>PressPad(controls);
    internal void TestSetPadConfig(PadConfig c)=>padConfig=c;
    internal int TestPadHeld=>holding.Keys.Count(h=>h.Binding is not null);
    internal PadDialog TestShowPad()=>(PadDialog)ShowWindow("controller",()=>new PadDialog(padConfig));
    internal int TestBacklogCycles=>inputWait+inputQueue.Sum(x=>x.Cycles);
    // One keystroke the way Windows delivers it: KEYDOWN, CHAR, then KEYUP. The
    // stand-in has to be a real virtual key, because KeyEventArgs.KeyCode answers
    // Keys.None for anything that is not a defined Keys member; every tap is released
    // before the next, so one letter key serves for the whole alphabet.
    internal const Keys TestKey=Keys.A;
    void TestTap(char ch) {
        OnKeyDown(this,new KeyEventArgs(TestKey));
        OnKeyPress(this,new KeyPressEventArgs(ch));
        OnKeyUp(this,new KeyEventArgs(TestKey));
    }
    void TestTapKey(Keys key) {
        Message message=new();ProcessCmdKey(ref message,key);
        OnKeyUp(this,new KeyEventArgs(key));
    }
    void TestSettle() {
        while(inputQueue.Count>0 || inputWait>0)RunCycles(1024000);
        core!.Run(1024000);UpdateDisplay();
    }
    internal void TestTypeOnly(string text) {
        foreach(char ch in text){TestTap(ch);TestSettle();}
    }
    internal void TestText(string text) {
        TestTypeOnly(text);
        TestTapKey(Keys.Enter);TestSettle();
    }
}
