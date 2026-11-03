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
            if(args.Length>0 && args[0]=="--smoke-test") {SmokeTest.Run(args[1],args[2]);return;}
            if(args.Length>0 && args[0]=="--manual-shots") {ManualShots.Run(args[1],args[2],args.Length>3?args[3]:"ja");return;}
            if(args.Length>0 && args[0]=="--open-folder") {AppFolders.Open(args.Length>1?args[1]:"");return;}
            Application.Run(new MainForm(args.FirstOrDefault()));
        } catch(Exception ex) {MessageBox.Show(ex.ToString(),"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Error);Environment.ExitCode=1;}
    }
}

// A focused Button swallows Space and Enter to activate itself, and both are
// pocket-computer keys. The toolbar refuses focus so every keystroke reaches
// the machine no matter which button was clicked last.
/// <summary>フォーカスを取らないツールバーのボタン（Space・Enter をポケコンのキーとして使うため）。</summary>
internal sealed class ToolButton : Button
{
    internal ToolButton(){SetStyle(ControlStyles.Selectable,false);TabStop=false;}
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
    readonly Label status=new();
    // 8 ms so a keystroke and the click it makes are not held back by the frame
    // they land in. The LCD and the status line still repaint on every other tick.
    /// <summary>エミュレーションを進めるタイマー（8ms ごと）。</summary>
    readonly System.Windows.Forms.Timer timer=new(){Interval=8};
    int tick;
    readonly Stopwatch clock=Stopwatch.StartNew();
    /// <summary>キー入力の待ち行列：キーコード、押すか離すか、その状態を保つサイクル数。</summary>
    readonly Queue<(int Code,bool Down,int Cycles)> inputQueue=new();
    
    /// <summary>画面でクリックして押したままの SHIFT・CTRL。</summary>
    readonly HashSet<int> held=new();
    // PC keys physically down. A KEYDOWN for one already in here is Windows
    // repeating the key, not a second keystroke.
    readonly HashSet<Keys> downKeys=new();
    bool charRepeats;
    // Every sub-window is modeless so the machine stays usable while one is open.
    // One instance each: reopening brings the existing window forward.
    /// <summary>開いているサブウィンドウ（種類ごとに1つ）。</summary>
    readonly Dictionary<string,Form> windows=new();
    // The CE-126P's paper outlives ROM switches and its window.
    readonly PrinterPaper paper=new();
    readonly ToolButton pause=new(){Text=PauseCommand,AutoSize=true};
    readonly ToolButton speed=new(){Text=L.T("速度 1x","Speed 1x"),AutoSize=true,ForeColor=Color.Black,BackColor=Color.FromArgb(227,229,225)};
    int speedIndex=1;
    /// <summary>データを置くフォルダ（rom・ram・drive-x・state がある）と、プログラムのフォルダ（manual がある）。
    /// zip 版では同じ、インストール版では別（AppFolders を参照）。</summary>
    readonly string root,programRoot;
    /// <summary>今のキーの状態を保つ残りのサイクル数（0 なら次のキーへ）。</summary>
    int inputWait;
    double previous;
    bool paused;
    /// <summary>ツールバー・本体の絵・案内・ステータスバーを並べる表（通常の表示のときだけ見せる）。</summary>
    readonly TableLayoutPanel main=new(){Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(12)};
    /// <summary>通常の表示でのウィンドウの最小の大きさ。</summary>
    static readonly Size NormalMinimum=new(1040,625);
    /// <summary>右クリックのメニュー（ツールバーと同じ操作と、ウィンドウの表示）。</summary>
    readonly ContextMenuStrip menu=new();
    /// <summary>新しい版があるときだけ出す、ツールバーのボタンとメニューの項目。</summary>
    readonly ToolButton updateNotice=new(){AutoSize=true,Visible=false,ForeColor=Color.Black,BackColor=Color.FromArgb(238,204,121),FlatStyle=FlatStyle.Flat,Margin=new Padding(0,0,7,0)};
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
            settings=HardwareSettings.Load(Path.Combine(root,"state","settings.json"));
            L.Set(settings.Language);
            audio=new AudioOutput();audio.Muted=settings.Muted;audio.Volume=settings.Volume/100f;
        }
        if(string.IsNullOrWhiteSpace(settings.DiskFolder))settings=settings with{DiskFolder=Path.Combine(root,"drive-x")};
        surface.SetModel(settings.Appearance);
        surface.SetLcd(settings.LcdDotLines,settings.LcdContrast);
        var executableIcon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if(executableIcon is not null)Icon=executableIcon;
        Text=Title(settings.Appearance);ClientSize=new Size(1180,725);MinimumSize=NormalMinimum;
        AutoScaleMode=AutoScaleMode.Dpi;BackColor=Color.FromArgb(38,44,54);ForeColor=Color.FromArgb(230,231,229);
        Font=new Font("Yu Gothic UI",10);KeyPreview=true;
        // 画面の構成：ツールバー、本体の絵、操作の案内、ステータスバーの4段。
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));main.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute,28));main.RowStyles.Add(new RowStyle(SizeType.Absolute,28));Controls.Add(main);
        // The buttons wrap onto a second row rather than running off a narrow window.
        var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new Padding(0,0,0,6)};main.Controls.Add(bar,0,0);
        pause.Click+=(_,_)=>TogglePause();speed.Click+=(_,_)=>SetSpeed((speedIndex+1)%4);
        // One list for the toolbar and the right-click menu, so the two cannot differ.
        // ツールバーと右クリックのメニューは同じ一覧から作る（食い違わないように）。
        Command[] commands=[
            new(L.T("ROMを開く","Open ROM"),OpenRom),new(PauseCommand,TogglePause,pause,()=>paused),new(L.T("保存","Save"),SaveState),new(L.T("読込","Load"),LoadState),
            new(L.T("設定","Settings"),ConfigureHardware),new(L.T("画面","Display"),ShowDisplay),new("CE-140F",ShowDisk),new("CE-126P",ShowPrinter),
            new(L.T("文字入出力","Text I/O"),ShowTextTransfer),new(L.T("オールリセット","All Reset"),AllReset),new(L.T("デバッグ","Debug"),ShowDebugger),new(L.T("使い方","Help"),Help)
        ];
        foreach(var command in commands) {
            if(command.Button is not null)bar.Controls.Add(command.Button);else AddAction(bar,command.Text,command.Action);
        }
        bar.Controls.Add(speed);bar.Controls.Add(updateNotice);
        updateNotice.Click+=(_,_)=>TryAction(OpenUpdate);updateItem.Click+=(_,_)=>TryAction(OpenUpdate);
        BuildMenu(commands);
        main.Controls.Add(surface,0,1);
        surface.BackgroundDoubleClicked+=()=>TryAction(CycleView);
        surface.BackgroundDragged+=DragWindow;
        main.Controls.Add(new Label{Text=L.T("初回 PF1 → BASIC  /  F6: MENU  F7: BASIC  F8: 2ndF  Esc: ON/BRK  /  SHIFT・CTRLは次の1キーまで保持","First run: PF1 → BASIC  /  F6: MENU  F7: BASIC  F8: 2ndF  Esc: ON/BRK  /  SHIFT and CTRL stay held for the next key"),Dock=DockStyle.Fill,Font=new Font("Yu Gothic UI",9)},0,2);
        status.Dock=DockStyle.Fill;status.Font=new Font("Yu Gothic UI",9);main.Controls.Add(status,0,3);
        // 画面のキーのクリック：RESET 穴はリセット、SHIFT・CTRL は押したままにする、ほかはキー入力の待ち行列へ。
        surface.KeyClicked+=name=>{
            if(core is null||paused)return;
            if(name=="RESET") {TryAction(ResetPin);return;}
            int code=Keyboard.Codes[name];
            if(name is "SHIFT" or "CTRL") {if(!held.Add(code))held.Remove(code);core.Key(code,held.Contains(code));surface.SetPressed(name,held.Contains(code));}
            else QueueKey(code);
        };
        Deactivate+=(_,_)=>ReleaseKeys();
        KeyDown+=OnKeyDown;KeyUp+=OnKeyUp;KeyPress+=OnKeyPress;
        timer.Tick+=(_,_)=>Tick();
        // 閉じる前に RAM・カード・状態と設定を保存する。失敗したら、それでも閉じるかを聞く。
        FormClosing+=(_,e)=>{
            if(core is null)return;
            try {ReleaseKeys();SavePersistent();if(!testing)settings.Save(Path.Combine(root,"state","settings.json"));}
            catch(Exception ex) {
                if(MessageBox.Show(L.T("状態を保存できませんでした。終了しますか？\n","Could not save the state. Quit anyway?\n")+ex.Message,"PC-E500",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.No)e.Cancel=true;
            }
        };
        FormClosed+=(_,_)=>{foreach(var window in windows.Values.ToArray())window.Close();timer.Dispose();audio?.Dispose();core?.Dispose();};
        // ROM の指定がなければ、rom フォルダから外観（E500 か E650）に合う大きさと名前のものを選ぶ。
        if(romPath is null) {
            string directory=Path.Combine(root,"rom");
            // A raw SAVEM dump of the main ROM is 16 bytes longer and opens as well.
            static long RomBytes(string x){long n=new FileInfo(x).Length;return n==256*1024+16?256*1024:n;}
            var candidates=Directory.Exists(directory)?Directory.GetFiles(directory,"*.rom")
                .Where(x=>RomBytes(x) is 256*1024 or 384*1024).ToArray():[];
            bool e500=settings.Appearance=="PC-E500";
            string[] wanted=e500?["E500","E550","1480","1490"]:["E650","U6000"];
            long wantedSize=e500?256*1024:384*1024;
            // The first model in the list that has a ROM; of several, the last by name,
            // which for names such as PC-E500(7.2).ROM is the newest version.
            // 一覧の先の機種を優先し、同じ機種が複数あれば名前の順で最後（「機種名(版).ROM」なら新しい版）を選ぶ。
            romPath=wanted.Select(w=>candidates.Where(x=>RomBytes(x)==wantedSize && Path.GetFileName(x).Contains(w,StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x=>Path.GetFileName(x),StringComparer.OrdinalIgnoreCase).FirstOrDefault()).FirstOrDefault(x=>x is not null)
                ??candidates.FirstOrDefault(x=>Path.GetFileName(x)=="s3-7.3(E500).rom")
                ??(candidates.Length==1?candidates[0]:null);
        }
        if(romPath is not null) {
            TryAction(()=>SwitchRom(romPath,!testing));
        }
        if(!testing)timer.Start();
        // The window has its place on the screen only once shown; then the saved view.
        if(!testing)Shown+=(_,_)=>TryAction(()=>RestoreView(settings));
        // Once a day at most, in the background; nothing is said if it fails.
        if(!testing)Shown+=(_,_)=>{if(settings.CheckUpdates && DateTime.UtcNow-settings.UpdateCheckedAt>=TimeSpan.FromDays(1))_=CheckForUpdate(false);};
        // Written at once, so that a crash before closing does not lose the place.
        ResizeEnd+=(_,_)=>{if(view!=WindowView.Normal){RememberView();SaveSettings();}};
    }
    /// <summary>RAM カードのイメージを置くフォルダ。</summary>
    string CardFolder()=>Path.Combine(root,"ram");
    /// <summary>自動保存のファイル（ROM・内蔵 RAM・カードの容量ごと）。</summary>
    string AutoSavePath()=>Path.Combine(root,"state",$"{core!.Hash}.i{core.InternalKB}-c{core.CardKB}.e500state");
    /// <summary>内蔵 RAM を保存するフォルダ（ROM ごと）。</summary>
    string MemoryDirectory()=>Path.Combine(root,"state","memory",core!.RomHash);
    /// <summary>内蔵 RAM とカードを保存し、保存できる状態なら状態も自動保存する。</summary>
    void SavePersistent() {
        if(core is null||testing)return;
        core.MemoryFiles(MemoryDirectory(),false);core.SaveCard();
        if(core.CanSnapshot)core.Save(AutoSavePath());
    }
    /// <summary>一時停止と再開を切り替える。</summary>
    void TogglePause() {
        paused=!paused;pause.Text=paused?L.T("再開","Resume"):PauseCommand;ReleaseKeys();audio?.Flush();UpdateDisplay();
    }
    /// <summary>実行の速さの倍率。</summary>
    static readonly string[] Speeds=["0.5x","1x","2x","4x"];
    /// <summary>実行の速さを選ぶ。</summary>
    /// <param name="index">Speeds の位置</param>
    void SetSpeed(int index) {speedIndex=index;speed.Text=L.T("速度 ","Speed ")+Speeds[speedIndex];audio?.Flush();}
    // The toolbar's commands, then what the title bar and the frame give, which
    // the case-only and screen-only windows have not: the menu is the same in
    // every view, so it is always the way back.
    /// <summary>ツールバーとメニューの操作1つ。</summary>
    /// <param name="Text">ボタンとメニューの文字</param>
    /// <param name="Action">押したときの処理</param>
    /// <param name="Button">専用のボタン（文字が変わる一時停止など。null なら普通のボタンを作る）</param>
    /// <param name="Checked">メニューでチェックを付ける条件（null なら付けない）</param>
    readonly record struct Command(string Text,Action Action,ToolButton? Button=null,Func<bool>? Checked=null);
    /// <summary>右クリックのメニューを作る：ツールバーと同じ操作、速度、ウィンドウの表示、最小化・終了。</summary>
    /// <param name="commands">ツールバーの操作</param>
    void BuildMenu(Command[] commands) {
        var checks=new List<(ToolStripMenuItem Item,Func<bool> Checked)>();
        foreach(var command in commands) {
            var action=command.Action;var item=new ToolStripMenuItem(command.Text,null,(_,_)=>TryAction(action));
            if(command.Checked is not null)checks.Add((item,command.Checked));
            menu.Items.Add(item);
        }
        menu.Items.Insert(0,updateItem);
        menu.Items.Add(L.T("新しい版を確認...","Check for a new version..."),null,(_,_)=>_=CheckForUpdate(true));
        menu.Items.Add(L.T("データフォルダを開く","Open the data folder"),null,(_,_)=>TryAction(()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppFolders.Prepare(root)){UseShellExecute=true})));
        var speedMenu=new ToolStripMenuItem(L.T("速度","Speed"));
        for(int i=0;i<Speeds.Length;i++){int index=i;speedMenu.DropDownItems.Add(Speeds[i],null,(_,_)=>SetSpeed(index));}
        menu.Items.Add(speedMenu);
        menu.Items.Add(new ToolStripSeparator());
        var viewMenu=new ToolStripMenuItem(L.T("ウィンドウの表示","Window"));
        string[] views=[L.T("通常（タイトル・ボタンを表示）","Normal (title and buttons)"),L.T("本体だけ","Case only"),L.T("画面だけ","Screen only")];
        for(int i=0;i<views.Length;i++){var v=(WindowView)i;viewMenu.DropDownItems.Add(views[i],null,(_,_)=>TryAction(()=>SetView(v)));}
        menu.Items.Add(viewMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.T("最小化","Minimize"),null,(_,_)=>WindowState=FormWindowState.Minimized);
        menu.Items.Add(L.T("終了","Exit"),null,(_,_)=>Close());
        // The checks follow the machine, which may have changed them from elsewhere.
        menu.Opening+=(_,_)=>{
            foreach(var (item,isChecked) in checks)item.Checked=isChecked();
            for(int i=0;i<speedMenu.DropDownItems.Count;i++)((ToolStripMenuItem)speedMenu.DropDownItems[i]).Checked=i==speedIndex;
            for(int i=0;i<viewMenu.DropDownItems.Count;i++)((ToolStripMenuItem)viewMenu.DropDownItems[i]).Checked=i==(int)view;
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
                Controls.Remove(surface);main.Controls.Add(surface,0,1);main.Visible=true;
                TransparencyKey=Color.Empty;FormBorderStyle=FormBorderStyle.Sizable;MinimumSize=NormalMinimum;
                Bounds=normalBounds;if(normalMaximized)WindowState=FormWindowState.Maximized;
            } else {
                if(surface.Parent==main){main.Controls.Remove(surface);Controls.Add(surface);surface.BringToFront();}
                main.Visible=false;
                FormBorderStyle=FormBorderStyle.None;MinimumSize=Size.Empty;
                TransparencyKey=next==WindowView.Body?surface.BackColor:Color.Empty;
                ClientSize=ViewSize(zoom);
            }
            surface.Resizable=next!=WindowView.Normal;
        } finally {ResumeLayout(true);}
        if(next!=WindowView.Normal) {
            surface.Arrange();var now=surface.PointToScreen(surface.LcdArea().Location);
            Location=new Point(Location.X+anchor.X-now.X,Location.Y+anchor.Y-now.Y);
        }
        if(keep){RememberView();SaveSettings();}
        // The keys go to the surface when this window is next active; the
        // display window, if it made the change, keeps the focus meanwhile.
        ActiveControl=surface;
        if(windows.TryGetValue("display",out var open) && open is DisplayDialog d)d.Sync(surface.RealLcd,surface.Contrast,view);
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
        const int WM_NCHITTEST=0x84,WM_SIZING=0x214,WM_NCLBUTTONDBLCLK=0xA3;
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
        if(windows.TryGetValue("display",out var open) && open is DisplayDialog d)d.Sync(surface.RealLcd,surface.Contrast,view);
        if(!windows.ContainsKey("display"))SaveSettings();
    }
    /// <summary>設定をファイルに書く（書けなければ知らせるだけ）。</summary>
    void SaveSettings() {if(!testing)TryAction(()=>settings.Save(Path.Combine(root,"state","settings.json")));}
    /// <summary>「画面」ウィンドウを開く。</summary>
    void ShowDisplay()=>ShowWindow("display",()=>{
        var dialog=new DisplayDialog(surface.RealLcd,surface.Contrast,view);
        dialog.LcdChanged=SetLcd;dialog.ViewChanged=v=>TryAction(()=>SetView(v));
        dialog.FormClosed+=(_,_)=>SaveSettings();
        return dialog;
    });
    /// <summary>ツールバーにボタンを足す。押すと action を実行し、例外はメッセージで示す。</summary>
    /// <param name="parent">ボタンを置く場所</param>
    /// <param name="text">ボタンの文字</param>
    /// <param name="action">押したときの処理</param>
    void AddAction(Control parent,string text,Action action) {
        var b=new ToolButton{Text=text,AutoSize=true,ForeColor=Color.Black,BackColor=Color.FromArgb(227,229,225),FlatStyle=FlatStyle.Flat,Margin=new Padding(0,0,7,0)};
        b.Click+=(_,_)=>TryAction(action);parent.Controls.Add(b);
    }
    // The ROM scans the key matrix from its 64 Hz sub-timer, and only counts a key
    // whose press and release land in different scans with one scan between them.
    // 24000 down plus 32000 up is the shortest pair that never drops a key; the
    // 120000/100000 this replaces spent 215 ms per key, so anything faster than
    // five keys a second piled up into a backlog that felt like input lag.
    // A shifted character only comes out shifted when SHIFT is already down for a
    // whole scan before the key joins it, and stays down for a whole scan after the
    // key leaves. The 8000 that both used to be is half a scan, so whichever
    // keystroke the scan phase fell on lost its SHIFT: pasting LOAD M "X:TETRIS.BIN"
    // dropped the closing quote to a bare W, and typing quotes gave one W in four.
    const int KeyDownCycles=24000,KeyUpCycles=32000,ModeSettleCycles=200000,ModifierCycles=10000,
        ShiftLeadCycles=24000,ShiftTailCycles=24000;
    /// <summary>キーを1回押して離す操作を待ち行列に入れ、すぐに始める。</summary>
    /// <param name="code">キーコード</param>
    /// <param name="fast">貼り付けからの入力か（今は同じ速さ）</param>
    void QueueKey(int code,bool fast=false) {
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
    void QueueShifted(int code,bool fast=false) {
        int shift=Keyboard.Codes["SHIFT"];
        inputQueue.Enqueue((shift,true,ShiftLeadCycles));
        QueueKey(code,fast);
        inputQueue.Enqueue((shift,false,ShiftTailCycles));
    }
    /// <summary>前のキーの時間が過ぎていれば、待ち行列の次の操作を本体へ送る。</summary>
    /// <returns>送ったか</returns>
    bool NextKey() {
        if(core is null || inputWait!=0 || !inputQueue.TryDequeue(out var key))return false;
        core.Key(key.Code,key.Down);inputWait=key.Cycles;
        surface.SetPressed(Keyboard.Matrix[key.Code/8][key.Code%8],key.Down);
        return true;
    }
    // A keystroke costs the machine a 56000-cycle press/release pair, so it can take
    // about eighteen a second. Windows repeats a held key thirty-one times a second,
    // and queueing every one of those built a backlog that grew for as long as the
    // key was down: two seconds of a held key left 1.4 s of typing and clicking still
    // to come out after the key was released. A repeat that arrives while the machine
    // still has a keystroke in hand is dropped instead of queued. The key still
    // repeats, now at the fastest rate the machine can actually accept, and the queue
    // can never hold more than the one keystroke. Keystrokes the typist actually made
    // are never dropped - only Windows' own repeats are.
    /// <summary>キーを受け付けるか（Windows の自動リピートは、前のキーが終わるまで捨てる）。</summary>
    /// <param name="repeat">自動リピートか</param>
    bool AcceptKey(bool repeat)=>!repeat || inputQueue.Count==0;
    /// <summary>PC のキーが押された：Enter・矢印・F キーなど文字にならないキーを本体のキーにする（文字は OnKeyPress）。</summary>
    void OnKeyDown(object? sender,KeyEventArgs e) {
        if(e.Alt)return;
        charRepeats=!downKeys.Add(e.KeyCode);
        if(Keyboard.Special.TryGetValue(e.KeyCode,out var name)) {
            if(name=="CTRL") {int code=Keyboard.Codes[name];if(held.Add(code))core?.Key(code,true);}
            else if(AcceptKey(charRepeats)) {
                if(e.Shift)QueueShifted(Keyboard.Codes[name]);else QueueKey(Keyboard.Codes[name]);
            }
            e.Handled=true;e.SuppressKeyPress=true;
        }
    }
    /// <summary>ショートカット（Ctrl+C・Ctrl+D・Ctrl+V）と、WinForms が先に取ってしまう矢印・Enter・Tab を処理する。</summary>
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
        if(keyData==(Keys.Control|Keys.C)){TryAction(ShowTextTransfer);return true;}
        if(keyData==(Keys.Control|Keys.D)){TryAction(ShowDebugger);return true;}
        if(keyData==(Keys.Control|Keys.V)){TryAction(()=>{
            if(!Clipboard.ContainsText(TextDataFormat.UnicodeText))throw new InvalidOperationException(L.T("クリップボードに文字列がありません。","The clipboard has no text."));
            PasteText(Clipboard.GetText(TextDataFormat.UnicodeText));
        });return true;}
        // The arrows, Enter and Tab are dialog keys, so WinForms takes them here before
        // KeyDown ever runs. Only the press comes through: KeyUp still raises normally.
        if((keyData&Keys.Alt)==0 && Keyboard.Special.TryGetValue(keyData&Keys.KeyCode,out string? name) && name!="CTRL") {
            bool repeat=!downKeys.Add(keyData&Keys.KeyCode);
            // A character key carries its own shift through WM_CHAR; the arrows,
            // Enter and the function keys have no character, so the PC SHIFT is the
            // only way to reach their shifted meanings.
            if(AcceptKey(repeat)) {
                if((keyData&Keys.Shift)!=0)QueueShifted(Keyboard.Codes[name]);else QueueKey(Keyboard.Codes[name]);
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    /// <summary>PC のキーが離された：押しっぱなしの CTRL を離す。</summary>
    void OnKeyUp(object? sender,KeyEventArgs e) {
        downKeys.Remove(e.KeyCode);
        if(e.KeyCode==Keys.ControlKey) {int code=Keyboard.Codes["CTRL"];held.Remove(code);core?.Key(code,false);}
    }
    /// <summary>PC で文字が入力された：対応する本体のキー（必要なら SHIFT 付き）を待ち行列に入れる。</summary>
    void OnKeyPress(object? sender,KeyPressEventArgs e) {
        if(char.IsControl(e.KeyChar))return;
        if(AcceptKey(charRepeats) && !QueueCharacter(e.KeyChar,false))
            status.Text=L.T($"未対応のPC文字: {e.KeyChar}（画面のSHIFTとポケコンのキーで入力してください）",$"Unsupported PC character: {e.KeyChar} (use the on-screen SHIFT and pocket computer keys)");
        // Consumed: a WM_CHAR that arrives without its WM_KEYDOWN is a fresh keystroke.
        charRepeats=false;e.Handled=true;
    }
    /// <summary>1文字を本体のキー操作に変えて待ち行列に入れる。</summary>
    /// <param name="character">文字</param>
    /// <param name="fast">貼り付けからの入力か</param>
    /// <returns>対応するキーがあったか</returns>
    bool QueueCharacter(char character,bool fast) {
        string text=character==' '?"SPACE":char.ToUpperInvariant(character).ToString();
        if(Keyboard.Codes.TryGetValue(text,out int code)){QueueKey(code,fast);return true;}
        if(Keyboard.Shifted.TryGetValue(character,out string? baseKey)) {QueueShifted(Keyboard.Codes[baseKey],fast);return true;}
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
            if(character=='\n'){QueueKey(Keyboard.Codes["ENTER"],true);queued++;continue;}
            if(character=='\t'){for(int i=0;i<4;i++)QueueCharacter(' ',true);queued+=4;continue;}
            if(QueueCharacter(character,true))queued++;else skipped++;
        }
        string message=L.T($"{queued}文字をキー入力へ送信しました。",$"Sent {queued} characters as key input.");
        if(skipped>0)message+=L.T($" 未対応の{skipped}文字は省略しました。",$" Skipped {skipped} unsupported characters.");
        status.Text=message;return message;
    }
    /// <summary>すべてのキーを離し、待ち行列を空にする。</summary>
    void ReleaseKeys() {
        core?.Release();held.Clear();inputQueue.Clear();inputWait=0;
        downKeys.Clear();charRepeats=false;
        surface.ReleaseKeys();
    }
    /// <summary>
    /// タイマーごとの処理：経過時間と速度の倍率に応じて CPU を進め、音声を出し、
    /// 5秒ごと（と電源 OFF になったとき）に自動保存し、1回おきに画面を更新する。
    /// </summary>
    void Tick() {
        double now=clock.Elapsed.TotalSeconds,elapsed=Math.Clamp(now-previous,0,0.1);previous=now;
        if(core is null || paused)return;
        try {
            double rate=new[]{0.5,1,2,4}[speedIndex];
            int cycles=(int)(elapsed*1024000*rate);
            RunCycles(cycles);
            int count=Native.e500_audio(core.Handle,soundBuffer,soundBuffer.Length);audio?.Submit(soundBuffer,count);
            bool off=(Native.e500_status(core.Handle)&1)!=0;
            if(!testing && (now-lastSaved>=5 || (off&&!lastOff))) {SavePersistent();lastSaved=now;}
            bool powerChanged=off!=lastOff;lastOff=off;
            if((++tick&1)==0 || powerChanged)UpdateDisplay();
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
    }
    /// <summary>LCD を読み直して絵を更新し、ステータスバーに状態を表示する。</summary>
    void UpdateDisplay() {
        if(core is null)return;
        core.Refresh();surface.Invalidate();
        int s=Native.e500_status(core.Handle);
        string card=core.HasCard?L.T($"RAMカード {core.CardKB}KB ({core.CardName})",$"RAM card {core.CardKB}KB ({core.CardName})"):L.T("RAMカード なし","RAM card: none");
        status.Text=$"{((s&1)!=0?L.T("電源OFF / EscでON","Power OFF / Esc turns ON"):paused?L.T("一時停止","Pause"):L.T("実行中","Running"))}   ROM Ver.{core.RomVersion} / {core.RomKB}KB ({(core.IsE650Family?"E650/U6000":"E500")})   {L.T("内蔵","Internal")} {core.InternalKB}KB / {card}   CE-140F {DiskStatus()}   CE-126P {(core.PrinterConnected?L.T("接続","Connected"):L.T("未接続","Not connected"))}   {audio?.Error ?? (settings.Muted?L.T("消音","Muted"):L.T("音声ON","Sound ON"))}";
    }
    /// <summary>ステータスバーに出す CE-140F の状態（未接続・エラー・通信中・読取専用・接続）。</summary>
    string DiskStatus() {
        if(core is null)return L.T("未接続","Not connected");int d=Native.e500_disk_status(core.Handle);
        return (d&1)==0?L.T("未接続","Not connected"):(d&8)!=0?L.T("エラー","Error"):(d&4)!=0?L.T("通信中","Busy"):(d&2)!=0?L.T("読取専用","Read-only"):L.T("接続","Connected");
    }
    /// <summary>ROM を開き直す（今のコアは RAM などを保存してから破棄する）。</summary>
    /// <param name="path">ROM ファイル</param>
    /// <param name="restore">保存してある RAM と状態を復元するか</param>
    void SwitchRom(string path,bool restore) {
        var next=new Core(path,settings.InternalKB,settings.CardPath);
        try {if(core is not null) {ReleaseKeys();SavePersistent();}}
        catch {next.Dispose();throw;}
        core?.Dispose();core=next;surface.Core=core;audio?.Flush();
        core.PrinterConnected=settings.PrinterConnected;core.Paper=paper;
        if(settings.InternalKB!=core.InternalKB) {
            settings=settings with{InternalKB=core.InternalKB};
            if(!testing)settings.Save(Path.Combine(root,"state","settings.json"));
        }
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
                core.MemoryFiles(MemoryDirectory(),true);
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
            var dialog=new HardwareDialog(settings,core?.RomKB??256,CardFolder());
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
        bool language=next.Language!=settings.Language&&!string.IsNullOrEmpty(settings.Language);
        // Tests never write the user's settings file, whichever way the apply goes.
        settings=next;if(!testing)settings.Save(Path.Combine(root,"state","settings.json"));
        // Every window builds its strings when it opens, so a new language takes
        // effect by starting over; the close path saves the machine first.
        if(language&&!testing){Application.Restart();return;}
        SetAppearance(settings.Appearance);
        surface.SetLcd(settings.LcdDotLines,settings.LcdContrast);
        if(audio is not null){audio.Muted=settings.Muted;audio.Volume=settings.Volume/100f;}
        if(memory&&core is not null)SwitchRom(core.RomPath,true);
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
    void ShowPrinter()=>ShowWindow("printer",()=>new PrinterDialog(paper,()=>core?.Font??[],()=>settings.PrinterConnected,on=>{
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
    /// <summary>ROM ファイルを選んで開く。</summary>
    void OpenRom() {
        using var d=new OpenFileDialog{Filter=L.T("PC-E500シリーズの ROM (*.rom;*.bin)|*.rom;*.bin|すべてのファイル|*.*","PC-E500 series ROM (*.rom;*.bin)|*.rom;*.bin|All files|*.*"),InitialDirectory=Path.Combine(root,"rom")};
        if(d.ShowDialog(this)==DialogResult.OK)SwitchRom(d.FileName,true);
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
        if(d.ShowDialog(this)==DialogResult.OK) {core.Load(d.FileName);settings=settings with{InternalKB=core.InternalKB};audio?.Flush();UpdateDisplay();}
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
        status.Text=L.T("オールリセット: 画面のY／Nで、全データを消去するか選んでください。","All reset: answer Y/N on the screen to choose whether to erase all data.");
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
    void TryAction(Action action) {ReleaseKeys();try {action();}catch(Exception ex) {MessageBox.Show(ex.Message,"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
    // 以下は自動テスト（SmokeTest・ManualShots）用の入口。
    internal Core TestCore=>core??throw new InvalidOperationException("ROM not loaded");
    internal string TestRoot=>root;
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
    internal DisplayDialog TestDisplay()=>(DisplayDialog)TestWindow("display");
    internal HardwareSettings TestSettings=>settings;
    internal void TestAppearance(string model)=>SetAppearance(model);
    internal void TestApplyHardware(HardwareSettings next)=>ApplyHardware(next);
    internal string TestStatus=>status.Text;
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
    internal string TestPaste(string text) {
        string result=PasteText(text);TestSettle();return result;
    }
    internal void TestRunCycles(int cycles)=>RunCycles(cycles);
    internal PocketSurface TestSurface=>surface;
    internal PrinterPaper TestPaper=>paper;
    internal bool TestPrinterSetting=>settings.PrinterConnected;
    internal void TestRepeat(Keys key,char ch){OnKeyDown(this,new KeyEventArgs(key));OnKeyPress(this,new KeyPressEventArgs(ch));}
    internal void TestRepeatKey(Keys key){Message m=new();ProcessCmdKey(ref m,key);}
    internal void TestRelease(Keys key)=>OnKeyUp(this,new KeyEventArgs(key));
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
