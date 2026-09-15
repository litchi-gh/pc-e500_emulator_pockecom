// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;

namespace PokeCom;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try {
            if(args.Length>0 && args[0]=="--smoke-test") {SmokeTest.Run(args[1],args[2]);return;}
            if(args.Length>0 && args[0]=="--manual-shots") {ManualShots.Run(args[1],args[2],args.Length>3?args[3]:"ja");return;}
            Application.Run(new MainForm(args.FirstOrDefault()));
        } catch(Exception ex) {MessageBox.Show(ex.ToString(),"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Error);Environment.ExitCode=1;}
    }
}

// A focused Button swallows Space and Enter to activate itself, and both are
// pocket-computer keys. The toolbar refuses focus so every keystroke reaches
// the machine no matter which button was clicked last.
internal sealed class ToolButton : Button
{
    internal ToolButton(){SetStyle(ControlStyles.Selectable,false);TabStop=false;}
}

internal sealed class MainForm : Form
{
    Core? core;
    readonly PocketSurface surface=new();
    readonly AudioOutput? audio;
    readonly short[] soundBuffer=new short[48000];
    HardwareSettings settings=new();
    readonly bool testing;
    double lastSaved;
    bool lastOff;
    readonly Label status=new();
    // 8 ms so a keystroke and the click it makes are not held back by the frame
    // they land in. The LCD and the status line still repaint on every other tick.
    readonly System.Windows.Forms.Timer timer=new(){Interval=8};
    int tick;
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly Queue<(int Code,bool Down,int Cycles)> inputQueue=new();
    
    readonly HashSet<int> held=new();
    // PC keys physically down. A KEYDOWN for one already in here is Windows
    // repeating the key, not a second keystroke.
    readonly HashSet<Keys> downKeys=new();
    bool charRepeats;
    // Every sub-window is modeless so the machine stays usable while one is open.
    // One instance each: reopening brings the existing window forward.
    readonly Dictionary<string,Form> windows=new();
    readonly ToolButton pause=new(){Text=L.T("一時停止","Pause"),AutoSize=true};
    readonly ToolButton speed=new(){Text=L.T("速度 1x","Speed 1x"),AutoSize=true,ForeColor=Color.Black,BackColor=Color.FromArgb(227,229,225)};
    int speedIndex=1;
    readonly string root;
    int inputWait;
    double previous;
    bool paused;
    internal MainForm(string? romPath=null, bool testing=false)
    {
        root=FindRoot();
        this.testing=testing;
        if(!testing) {
            settings=HardwareSettings.Load(Path.Combine(root,"state","settings.json"));
            L.Set(settings.Language);
            audio=new AudioOutput();audio.Muted=settings.Muted;audio.Volume=settings.Volume/100f;
        }
        if(string.IsNullOrWhiteSpace(settings.DiskFolder))settings=settings with{DiskFolder=Path.Combine(root,"drive-x")};
        surface.SetModel(settings.Appearance);
        surface.SetDotLines(settings.LcdDotLines);
        var executableIcon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if(executableIcon is not null)Icon=executableIcon;
        Text=settings.Appearance+" Emulator | PokeCom";ClientSize=new Size(1180,725);MinimumSize=new Size(1040,625);
        AutoScaleMode=AutoScaleMode.Dpi;BackColor=Color.FromArgb(38,44,54);ForeColor=Color.FromArgb(230,231,229);
        Font=new Font("Yu Gothic UI",10);KeyPreview=true;
        var main=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(12)};
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));main.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute,28));main.RowStyles.Add(new RowStyle(SizeType.Absolute,28));Controls.Add(main);
        // The buttons wrap onto a second row rather than running off a narrow window.
        var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new Padding(0,0,0,6)};main.Controls.Add(bar,0,0);
        AddAction(bar,L.T("ROMを開く","Open ROM"),OpenRom);bar.Controls.Add(pause);pause.Click+=(_,_)=>{paused=!paused;pause.Text=paused?L.T("再開","Resume"):L.T("一時停止","Pause");ReleaseKeys();audio?.Flush();UpdateDisplay();};
        AddAction(bar,L.T("保存","Save"),SaveState);AddAction(bar,L.T("読込","Load"),LoadState);AddAction(bar,L.T("設定","Settings"),ConfigureHardware);
        AddAction(bar,"CE-140F",ShowDisk);AddAction(bar,L.T("文字入出力","Text I/O"),ShowTextTransfer);AddAction(bar,L.T("オールリセット","All Reset"),AllReset);AddAction(bar,L.T("デバッグ","Debug"),ShowDebugger);AddAction(bar,L.T("使い方","Help"),Help);
        speed.Click+=(_,_)=>{speedIndex=(speedIndex+1)%4;speed.Text=L.T("速度 ","Speed ")+new[]{"0.5x","1x","2x","4x"}[speedIndex];audio?.Flush();};bar.Controls.Add(speed);
        main.Controls.Add(surface,0,1);
        main.Controls.Add(new Label{Text=L.T("初回 PF1 → BASIC  /  F6: MENU  F7: BASIC  F8: 2ndF  Esc: ON/BRK  /  SHIFT・CTRLは次の1キーまで保持","First run: PF1 → BASIC  /  F6: MENU  F7: BASIC  F8: 2ndF  Esc: ON/BRK  /  SHIFT and CTRL stay held for the next key"),Dock=DockStyle.Fill,Font=new Font("Yu Gothic UI",9)},0,2);
        status.Dock=DockStyle.Fill;status.Font=new Font("Yu Gothic UI",9);main.Controls.Add(status,0,3);
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
        FormClosing+=(_,e)=>{
            if(core is null)return;
            try {ReleaseKeys();SavePersistent();settings.Save(Path.Combine(root,"state","settings.json"));}
            catch(Exception ex) {
                if(MessageBox.Show(L.T("状態を保存できませんでした。終了しますか？\n","Could not save the state. Quit anyway?\n")+ex.Message,"PC-E500",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.No)e.Cancel=true;
            }
        };
        FormClosed+=(_,_)=>{foreach(var window in windows.Values.ToArray())window.Close();timer.Dispose();audio?.Dispose();core?.Dispose();};
        if(romPath is null) {
            string directory=Path.Combine(root,"rom");
            // A raw SAVEM dump of the main ROM is 16 bytes longer and opens as well.
            static long RomBytes(string x){long n=new FileInfo(x).Length;return n==256*1024+16?256*1024:n;}
            var candidates=Directory.Exists(directory)?Directory.GetFiles(directory,"*.rom")
                .Where(x=>RomBytes(x) is 256*1024 or 384*1024).ToArray():[];
            bool e500=settings.Appearance=="PC-E500";
            string[] wanted=e500?["E500"]:["E650","U6000"];
            long wantedSize=e500?256*1024:384*1024;
            romPath=candidates.FirstOrDefault(x=>RomBytes(x)==wantedSize && wanted.Any(w=>Path.GetFileName(x).Contains(w,StringComparison.OrdinalIgnoreCase)))
                ??candidates.FirstOrDefault(x=>Path.GetFileName(x)=="s3-7.3(E500).rom")
                ??(candidates.Length==1?candidates[0]:null);
        }
        if(romPath is not null) {
            TryAction(()=>SwitchRom(romPath,!testing));
        }
        if(!testing)timer.Start();
    }
    // Everything the application reads and writes lives beside the executable:
    // app/rom, app/ram (card images), app/drive-x (CE-140F) and app/state. A
    // development build runs from src/desktop/bin, so walk up to the folder that
    // holds rom/ - or the app/ folder that does.
    static string FindRoot() {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent) {
            if(Directory.Exists(Path.Combine(d.FullName,"rom")))return d.FullName;
            if(Directory.Exists(Path.Combine(d.FullName,"app","rom")))return Path.Combine(d.FullName,"app");
        }
        return AppContext.BaseDirectory;
    }
    string CardFolder()=>Path.Combine(root,"ram");
    string AutoSavePath()=>Path.Combine(root,"state",$"{core!.Hash}.i{core.InternalKB}-c{core.CardKB}.e500state");
    string MemoryDirectory()=>Path.Combine(root,"state","memory",core!.RomHash);
    void SavePersistent() {
        if(core is null||testing)return;
        core.MemoryFiles(MemoryDirectory(),false);core.SaveCard();
        if((Native.e500_disk_status(core.Handle)&16)==0)core.Save(AutoSavePath());
    }
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
    void QueueShifted(int code,bool fast=false) {
        int shift=Keyboard.Codes["SHIFT"];
        inputQueue.Enqueue((shift,true,ShiftLeadCycles));
        QueueKey(code,fast);
        inputQueue.Enqueue((shift,false,ShiftTailCycles));
    }
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
    bool AcceptKey(bool repeat)=>!repeat || inputQueue.Count==0;
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
    void OnKeyUp(object? sender,KeyEventArgs e) {
        downKeys.Remove(e.KeyCode);
        if(e.KeyCode==Keys.ControlKey) {int code=Keyboard.Codes["CTRL"];held.Remove(code);core?.Key(code,false);}
    }
    void OnKeyPress(object? sender,KeyPressEventArgs e) {
        if(char.IsControl(e.KeyChar))return;
        if(AcceptKey(charRepeats) && !QueueCharacter(e.KeyChar,false))
            status.Text=L.T($"未対応のPC文字: {e.KeyChar}（画面のSHIFTとポケコンのキーで入力してください）",$"Unsupported PC character: {e.KeyChar} (use the on-screen SHIFT and pocket computer keys)");
        // Consumed: a WM_CHAR that arrives without its WM_KEYDOWN is a fresh keystroke.
        charRepeats=false;e.Handled=true;
    }
    bool QueueCharacter(char character,bool fast) {
        string text=character==' '?"SPACE":char.ToUpperInvariant(character).ToString();
        if(Keyboard.Codes.TryGetValue(text,out int code)){QueueKey(code,fast);return true;}
        if(Keyboard.Shifted.TryGetValue(character,out string? baseKey)) {QueueShifted(Keyboard.Codes[baseKey],fast);return true;}
        return false;
    }
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
    void ReleaseKeys() {
        core?.Release();held.Clear();inputQueue.Clear();inputWait=0;
        downKeys.Clear();charRepeats=false;
        surface.ReleaseKeys();
    }
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
    void RunCycles(int cycles) {
        if(core is null)return;
        while(cycles>0) {
            NextKey();
            int batch=inputWait>0?Math.Min(cycles,inputWait):cycles;
            core.Run(batch);cycles-=batch;if(inputWait>0)inputWait-=batch;
        }
    }
    void UpdateDisplay() {
        if(core is null)return;
        core.Refresh();surface.Invalidate();
        int s=Native.e500_status(core.Handle);
        string card=core.HasCard?L.T($"RAMカード {core.CardKB}KB ({core.CardName})",$"RAM card {core.CardKB}KB ({core.CardName})"):L.T("RAMカード なし","RAM card: none");
        status.Text=$"{((s&1)!=0?L.T("電源OFF / EscでON","Power OFF / Esc turns ON"):paused?L.T("一時停止","Pause"):L.T("実行中","Running"))}   ROM Ver.{core.RomVersion} / {core.RomKB}KB ({(core.IsE650Family?"E650/U6000":"E500")})   {L.T("内蔵","Internal")} {core.InternalKB}KB / {card}   CE-140F {DiskStatus()}   {audio?.Error ?? (settings.Muted?L.T("消音","Muted"):L.T("音声ON","Sound ON"))}";
    }
    string DiskStatus() {
        if(core is null)return L.T("未接続","Not connected");int d=Native.e500_disk_status(core.Handle);
        return (d&1)==0?L.T("未接続","Not connected"):(d&8)!=0?L.T("エラー","Error"):(d&4)!=0?L.T("通信中","Busy"):(d&2)!=0?L.T("読取専用","Read-only"):L.T("接続","Connected");
    }
    void SwitchRom(string path,bool restore) {
        var next=new Core(path,settings.InternalKB,settings.CardPath);
        try {if(core is not null) {ReleaseKeys();SavePersistent();}}
        catch {next.Dispose();throw;}
        core?.Dispose();core=next;surface.Core=core;audio?.Flush();
        if(settings.InternalKB!=core.InternalKB) {
            settings=settings with{InternalKB=core.InternalKB};
            if(!testing)settings.Save(Path.Combine(root,"state","settings.json"));
        }
        paused=false;pause.Text=L.T("一時停止","Pause");
        if(restore && !testing) {
            core.MemoryFiles(MemoryDirectory(),true);
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
        if(!testing)core.MountDisk(settings.DiskEnabled?settings.DiskFolder:null,settings.DiskReadOnly);
        core.Run(1024000);
        Native.e500_audio(core.Handle,soundBuffer,soundBuffer.Length);UpdateDisplay();previous=clock.Elapsed.TotalSeconds;
    }
    Form ShowWindow(string key,Func<Form> create) {
        if(windows.TryGetValue(key,out var open)&&!open.IsDisposed) {
            if(open.WindowState==FormWindowState.Minimized)open.WindowState=FormWindowState.Normal;
            open.Activate();return open;
        }
        var window=create();windows[key]=window;
        window.FormClosed+=(_,_)=>windows.Remove(key);
        window.Show(this);return window;
    }
    void ConfigureHardware() {
        ShowWindow("hardware",()=>{
            Directory.CreateDirectory(CardFolder());
            var dialog=new HardwareDialog(settings,core?.RomKB??256,CardFolder());
            dialog.Apply=next=>{
                try {ReleaseKeys();ApplyHardware(next);return true;}
                catch(Exception ex) {MessageBox.Show(ex.Message,"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Error);return false;}
            };
            return dialog;
        });
    }
    void ApplyHardware(HardwareSettings next) {
        if(next.DiskEnabled&&string.IsNullOrWhiteSpace(next.DiskFolder))throw new InvalidDataException(L.T("CE-140Fの保存先を指定してください。","Specify the CE-140F folder."));
        bool memory=next.InternalKB!=settings.InternalKB||
            !string.Equals(next.CardPath,settings.CardPath,StringComparison.OrdinalIgnoreCase);
        if(core is not null&&(Native.e500_disk_status(core.Handle)&16)!=0)throw new InvalidOperationException(L.T("CE-140Fの通信・OPENをCLOSEしてから設定してください。","CLOSE the CE-140F transfer or open file before changing settings."));
        if(memory&&core is not null)using(var validation=new Core(core.RomPath,next.InternalKB,next.CardPath)){}
        SavePersistent();
        if(core is not null)core.MountDisk(next.DiskEnabled?next.DiskFolder:null,next.DiskReadOnly);
        bool language=next.Language!=settings.Language&&!string.IsNullOrEmpty(settings.Language);
        settings=next;settings.Save(Path.Combine(root,"state","settings.json"));
        // Every window builds its strings when it opens, so a new language takes
        // effect by starting over; the close path saves the machine first.
        if(language&&!testing){Application.Restart();return;}
        surface.SetModel(settings.Appearance);Text=settings.Appearance+" Emulator | PokeCom";
        surface.SetDotLines(settings.LcdDotLines);
        if(audio is not null){audio.Muted=settings.Muted;audio.Volume=settings.Volume/100f;}
        if(memory&&core is not null)SwitchRom(core.RomPath,true);
        previous=clock.Elapsed.TotalSeconds;UpdateDisplay();
    }
    void ShowDisk()=>ShowWindow("disk",()=>new DiskTraceDialog(()=>core,()=>settings.DiskFolder,TryAction));
    // These delegates read the field instead of capturing the current core, so a
    // ROM switch that disposes it underneath an open window cannot be observed.
    void ShowTextTransfer()=>ShowWindow("text",()=>new ScreenTextDialog(()=>core?.ScreenText()??"",PasteText));
    void ShowDebugger()=>ShowWindow("debug",()=>new DebugDialog(()=>core));
    void OpenRom() {
        using var d=new OpenFileDialog{Filter=L.T("PC-E500/E650/U6000 ROM (*.rom;*.bin)|*.rom;*.bin|すべてのファイル|*.*","PC-E500/E650/U6000 ROM (*.rom;*.bin)|*.rom;*.bin|All files|*.*"),InitialDirectory=Path.Combine(root,"rom")};
        if(d.ShowDialog(this)==DialogResult.OK)SwitchRom(d.FileName,true);
    }
    void SaveState() {
        if(core is null)return;ReleaseKeys();
        using var d=new SaveFileDialog{Filter=L.T("PC-E500シリーズ state|*.e500state","PC-E500 series state|*.e500state"),FileName=settings.Appearance+".e500state"};
        if(d.ShowDialog(this)==DialogResult.OK)core.Save(d.FileName);
    }
    void LoadState() {
        if(core is null)return;ReleaseKeys();
        if((Native.e500_disk_status(core.Handle)&16)!=0)throw new InvalidOperationException(L.T("CE-140Fの通信・OPENをCLOSEしてから状態を読み込んでください。","CLOSE the CE-140F transfer or open file before loading a state."));
        using var d=new OpenFileDialog{Filter=L.T("PC-E500シリーズ state|*.e500state","PC-E500 series state|*.e500state")};
        if(d.ShowDialog(this)==DialogResult.OK) {core.Load(d.FileName);settings=settings with{InternalKB=core.InternalKB};audio?.Flush();UpdateDisplay();}
    }
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
    void AllReset() {
        if(core is null)return;
        if((Native.e500_disk_status(core.Handle)&16)!=0)throw new InvalidOperationException(L.T("CE-140Fの通信・OPENをCLOSEしてからリセットしてください。","CLOSE the CE-140F transfer or open file before resetting."));
        ReleaseKeys();SavePersistent();
        if(!testing)core.Save(Path.Combine(root,"state","before-reset-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".e500state"));
        int on=Keyboard.Codes["ON/BRK"];
        Native.e500_reset(core.Handle,0);
        core.Key(on,true);core.Run(2048000);core.Key(on,false);
        audio?.Flush();core.Run(1024000);UpdateDisplay();
        status.Text=L.T("オールリセット: 画面のY／Nで、全データを消去するか選んでください。","All reset: answer Y/N on the screen to choose whether to erase all data.");
    }
    // The HTML manual beside the executable, in the UI language; the summary
    // box stands in when it is missing.
    void Help() {
        string manual=Path.Combine(root,"manual",L.En?"index_en.html":"index.html");
        if(File.Exists(manual)){Process.Start(new ProcessStartInfo(manual){UseShellExecute=true});return;}
        HelpSummary();
    }
    void HelpSummary() => MessageBox.Show(L.En?HelpEnglish:"初回：PF1 → BASIC（PCのF7）\nRUN表示：1+2 → Enter、BEEP 1 → Enter\nプログラム：BASICでPROに切替 → 10 PRINT 123 → Enter\nBASICでRUNに戻す → RUN → Enter\n\n文字入出力：LCD文字の範囲コピー／クリップボードからキー入力\nCtrl+C：文字入出力を開く / Ctrl+V：直接貼り付け\nCtrl+D：デバッグ（CPUレジスタ、内部RAM、メモリの表示・書換え、BINの読み書き）\nCE-140F：SAVE \"X:TEST.BAS\" / LOAD \"X:TEST.BAS\"\n保存先・書込み禁止は「設定」で変更できます。\n内蔵RAMは容量ごとに内容を保存し、OFFやアプリ終了でも保持します。\nRAMカードは「設定」でイメージファイルを作成・装着・取り外しします。内容はそのファイルへ書き込みます。\nE650/U6000の384KB ROMでは内蔵RAMは実機標準の64KB、増設改造相当の128KB・256KBから選べます。\nRESET穴はCPUを再起動し、内蔵RAMとカードを保持します。\n「オールリセット」は実機のON＋RESETと同じで、ROMが「スベテノ データヲ ショウキョ シマスカ?」とY／Nで聞いてきます。\n\nSHIFT / CTRLをクリックすると次のキーまで保持します。\nF8は2ndF、EscはON/BRKです。\n詳細はEmulatorフォルダのREADME.mdを参照してください。", L.T("使い方 / PC-E500シリーズ","Help / PC-E500 series"));
    const string HelpEnglish="First run: PF1 → BASIC (F7 on the PC)\nRUN mode: 1+2 → Enter, BEEP 1 → Enter\nProgram: BASIC switches to PRO → 10 PRINT 123 → Enter\nBASIC back to RUN → RUN → Enter\n\nText I/O: copy LCD text / type from the clipboard\nCtrl+C: open Text I/O / Ctrl+V: paste directly\nCtrl+D: Debug (registers, internal RAM, memory view and editing, BIN load/save)\nCE-140F: SAVE \"X:TEST.BAS\" / LOAD \"X:TEST.BAS\"\nFolder and write protection are in Settings.\nInternal RAM is kept per capacity and survives OFF and exit.\nRAM cards are image files created, attached and detached in Settings; contents are written to that file.\nE650/U6000 384KB ROMs offer 64KB (stock) or 128KB/256KB (expansion mod) internal RAM.\nThe RESET hole restarts the CPU and keeps internal RAM and the card.\nAll Reset is the real ON+RESET: the ROM asks whether to erase all data (Y/N).\n\nClicking SHIFT / CTRL holds it for the next key.\nF8 is 2ndF, Esc is ON/BRK.\nSee README.md in the Emulator folder for details.";
    void TryAction(Action action) {ReleaseKeys();try {action();}catch(Exception ex) {MessageBox.Show(ex.Message,"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
    internal Core TestCore=>core??throw new InvalidOperationException("ROM not loaded");
    internal string TestRoot=>root;
    internal void TestRefresh()=>UpdateDisplay();
    internal void TestDotLines(bool show)=>surface.SetDotLines(show);
    internal void TestAppearance(string model)=>surface.SetModel(model);
    internal string TestStatus=>status.Text;
    internal void TestPhysicalKey(string id)=>surface.TriggerForTest(id);
    internal void TestAllReset()=>AllReset();
    internal Form TestWindow(string key) {
        (key switch{"debug"=>(Action)ShowDebugger,"hardware"=>ConfigureHardware,"disk"=>ShowDisk,"text"=>ShowTextTransfer,_=>throw new ArgumentException(key)})();
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
