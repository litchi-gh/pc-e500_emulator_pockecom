// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>
/// 自動テスト（PC-E500.exe --smoke-test ROM 出力先）。実際のウィンドウと ROM を動かして、
/// 起動・BASIC・キー入力・状態保存・RAM・周辺機器・各ウィンドウ・描画を確かめ、
/// 結果を出力先の smoke-result.txt に書く。失敗したら例外のメッセージを書いて終了コード 1。
/// </summary>
internal static class SmokeTest
{
    /// <summary>テストを実行する。</summary>
    /// <param name="rom">ROM ファイル</param>
    /// <param name="output">結果と画像を書くフォルダ</param>
    internal static void Run(string rom,string output)
    {
        Directory.CreateDirectory(output);
        try {
            using var form=new MainForm(rom,testing:true);
            form.CreateControl();form.Show();Application.DoEvents();
            var core=form.TestCore;
            byte[] loadedRom=File.ReadAllBytes(rom);
            string expectedRomVersion=$"{loadedRom[^16]}.{loadedRom[^15]}";
            form.TestRefresh();
            if(core.RomVersion!=expectedRomVersion || !form.TestStatus.Contains("ROM Ver."+expectedRomVersion,StringComparison.Ordinal))
                throw new Exception("ROM trailer version was not shown in the bottom status line");
            void Key(string key) {
                int code=Keyboard.Codes[key];core.Key(code,true);core.Run(100000);core.Key(code,false);core.Run(100000);
            }
            void Type(string text) => form.TestText(text);
            void Type2(string text) => form.TestTypeOnly(text);
            void ExpectThree(int x,int y) {
                core.Refresh();
                string lcd=core.ScreenText();
                if(!lcd.Contains('3'))throw new Exception($"Expected result digit 3 missing from LCD (ROM {core.RomKB}KB, RAM {core.InternalKB}KB, PC={Native.e500_reg(core.Handle,9):X5}, LCD={lcd.Replace(Environment.NewLine," / ")})");
            }
            Key("F1");core.Run(1024000);Key("BASIC");core.Run(1024000);
            Type("1+2");form.TestRefresh();Application.DoEvents();
            ExpectThree(234,8);
            using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"desktop.png"));}
            form.TestDotLines(true);Application.DoEvents();
            if(!form.TestSurface.RealLcd || !form.TestSettings.LcdDotLines)throw new Exception("The real LCD did not reach the settings");
            using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"desktop-dot-lines.png"));}
            form.TestDotLines(false);Application.DoEvents();
            // The title names the running version.
            if(!form.Text.Contains("v"+UpdateCheck.Current,StringComparison.Ordinal) || form.Text!=MainForm.Title(form.TestSettings.Appearance))throw new Exception("The window title lacks the version: "+form.Text);
            {
                // The right-click menu carries every toolbar command, the speeds and the views.
                var surface=form.TestSurface;
                var menuTexts=form.TestMenu.Items.OfType<ToolStripMenuItem>().Select(i=>i.Text).ToList();
                var missing=form.TestToolbar.Where(t=>!t.StartsWith(L.T("速度","Speed"),StringComparison.Ordinal) && !menuTexts.Contains(t)).ToList();
                if(missing.Count>0)throw new Exception("Context menu lacks toolbar commands: "+string.Join(", ",missing));
                var speeds=form.TestMenu.Items.OfType<ToolStripMenuItem>().First(i=>i.Text==L.T("速度","Speed"));
                if(speeds.DropDownItems.Count!=4 || form.TestMenu.Items.OfType<ToolStripMenuItem>().All(i=>i.Text!=L.T("ウィンドウの表示","Window")))throw new Exception("Context menu lacks the speeds or the views");
                if(surface.ContextMenuStrip!=form.TestMenu || form.ContextMenuStrip!=form.TestMenu)throw new Exception("Right-click does not open the menu");
                // The zip keeps its data beside the program; an installed copy (the
                // installer's marker beside it) keeps it per user, folders made up front.
                {
                    // Outside the output folder, which a sync client may hold on to.
                    string scratch=Path.Combine(Path.GetTempPath(),"pce500-folders-"+Guid.NewGuid().ToString("N"));
                    string program=Path.Combine(scratch,"program"),local=Path.Combine(scratch,"local");
                    Directory.CreateDirectory(program);
                    if(AppFolders.IsInstalled(program) || AppFolders.Data(program,local)!=program)throw new Exception("A zip copy does not keep its data beside itself");
                    File.WriteAllText(Path.Combine(program,AppFolders.Marker),"");
                    string data=AppFolders.Data(program,local);
                    if(!AppFolders.IsInstalled(program) || data!=Path.Combine(local,"PockeComE500"))throw new Exception("An installed copy's data is not per user: "+data);
                    AppFolders.Prepare(data);
                    if(!AppFolders.Folders.All(f=>Directory.Exists(Path.Combine(data,f))))throw new Exception("The data folders were not made");
                    // This build itself: run from an installed copy, its data is per user.
                    string dataRoot=AppFolders.Installed?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PockeComE500"):AppFolders.Program();
                    if(form.TestRoot!=Path.TrimEndingDirectorySeparator(dataRoot))throw new Exception($"Data in {form.TestRoot}, not {dataRoot} (installed: {AppFolders.Installed})");
                    try{Directory.Delete(scratch,true);}catch(IOException){}
                }
                // New versions are announced, never fetched: GitHub's answer read offline.
                {
                    string Answer(string tag,string url="https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases/tag/TAG",bool pre=false)=>
                        $$"""{"tag_name":"{{tag}}","name":"PC-E500 Emulator","html_url":"{{url.Replace("TAG",tag)}}","prerelease":{{(pre?"true":"false")}},"draft":false,"body":"## Changes\n- one"}""";
                    var newer=UpdateCheck.Parse(Answer("v99.1.2"));
                    // The notes come with Windows line breaks, for the text box.
                    if(newer is null || newer.Version!=new Version(99,1,2) || !UpdateCheck.IsNewer(newer) || !newer.Notes.Contains("## Changes\r\n- one",StringComparison.Ordinal) || !newer.Url.EndsWith("/v99.1.2",StringComparison.Ordinal))
                        throw new Exception("A newer release was not read: "+newer);
                    var same=UpdateCheck.Parse(Answer("v"+UpdateCheck.Current));
                    if(same is null || UpdateCheck.IsNewer(same) || UpdateCheck.Parse(Answer("v99.0.0",pre:true)) is not null || UpdateCheck.Parse(Answer("nightly")) is not null)
                        throw new Exception("The running version, a prerelease or a tag that is no version was taken for an update");
                    if(UpdateCheck.Parse(Answer("v99.0.0","https://example.com/TAG"))!.Url!=UpdateCheck.Releases)throw new Exception("A release page off this repository would be opened");
                    // Dot segments are no way out of the repository either.
                    if(UpdateCheck.Parse(Answer("v99.0.0",UpdateCheck.Releases+"/../../../other/repo/releases"))!.Url!=UpdateCheck.Releases)throw new Exception("A page of another repository, reached through ../, would be opened");
                    if(form.TestUpdateNotice.Visible)throw new Exception("An update is announced before any check");
                    form.TestShowUpdate(newer);Application.DoEvents();
                    var notice=form.TestUpdateNotice;
                    if(!notice.Visible || !notice.InMenu || !notice.Text.Contains("v99.1.2",StringComparison.Ordinal))throw new Exception($"Update not announced: {notice}");
                    using(var installed=new UpdateDialog(newer,true)){installed.StartPosition=FormStartPosition.CenterScreen;installed.Show(form);Application.DoEvents();
                        if(!installed.Controls.Cast<Control>().SelectMany(c=>c.Controls.Cast<Control>()).Any(c=>c.Text.Contains("setup.exe",StringComparison.Ordinal)))throw new Exception("The installed copy is told to unpack the zip");
                        installed.Close();}
                    using(var dialog=new UpdateDialog(newer)){dialog.StartPosition=FormStartPosition.CenterScreen;dialog.Show(form);Application.DoEvents();
                        using var bitmap=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(output,"update.png"));dialog.Close();}
                    // Skipped, it stays quiet on the next automatic check.
                    form.TestSkipUpdate();
                    if(form.TestUpdateNotice.Visible || form.TestUpdateNotice.InMenu || form.TestSettings.SkippedUpdate!="v99.1.2")throw new Exception("Skipped update still announced");
                    form.TestShowUpdate(newer);
                    if(form.TestUpdateNotice.Visible)throw new Exception("A skipped version was announced again");
                    if(!menuTexts.Contains(L.T("新しい版を確認...","Check for a new version...")))throw new Exception("No way to check for a new version from the menu");
                }
                // A double-click on a key is two keystrokes, not a change of window.
                var five=surface.Keys.First(k=>k.Id=="5").Bounds;
                surface.DoubleClickAt(surface.TestPoint(five.X+five.Width/2,five.Y+five.Height/2));
                if(form.TestView!=WindowView.Normal)throw new Exception("A double-click on a key changed the window");
                // Normal, case only, screen only, normal: the LCD stays where it is on the screen.
                var normal=form.Bounds;Point Lcd()=>surface.PointToScreen(surface.LcdArea().Location);var lcd=Lcd();
                bool Near(Point a,Point b)=>Math.Abs(a.X-b.X)<=2 && Math.Abs(a.Y-b.Y)<=2;
                surface.DoubleClickAt(surface.TestPoint(400,30));Application.DoEvents();
                if(form.TestView!=WindowView.Body || form.FormBorderStyle!=FormBorderStyle.None || form.TransparencyKey!=surface.BackColor || surface.ScreenOnly || !Near(Lcd(),lcd))
                    throw new Exception($"Case-only window: {form.TestView} {form.FormBorderStyle} LCD at {Lcd()} from {lcd}");
                if(Math.Abs(form.ClientSize.Width-(surface.ShownSize.Width*surface.Zoom+12))>3)throw new Exception($"Case-only window {form.ClientSize} does not fit the case");
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"window-body.png"));}
                // The case's own outline is the sizing edge; inside it, and off it, is not.
                var caseArea=surface.PictureArea();
                if(!surface.Resizable || surface.EdgeAt(new Point(caseArea.Right-2,caseArea.Top+caseArea.Height/2))!=11 ||
                   surface.EdgeAt(new Point(caseArea.Right-2,caseArea.Bottom-2))!=17 || surface.EdgeAt(new Point(caseArea.Left+caseArea.Width/2,caseArea.Top+caseArea.Height/2))!=0 ||
                   surface.EdgeAt(new Point(1,1))!=0)
                    throw new Exception($"Case-only window sizing edges at {caseArea}");
                if(form.TestSettings.View!=WindowView.Body || Math.Abs(form.TestSettings.ViewZoom-surface.Zoom)>0.01f || form.TestSettings.ViewLeft!=form.Left)
                    throw new Exception("Case-only view not kept in the settings");
                // Another case in the case-only view: the window fits it at the same zoom.
                {
                    float zoom=surface.Zoom;
                    bool Fits(float width)=>Math.Abs(form.ClientSize.Width-(width*zoom+12))<=2 && Math.Abs(form.ClientSize.Height-(400*zoom+12))<=2 && Math.Abs(surface.Zoom-zoom)<0.01f;
                    form.TestAppearance("PC-E650");Application.DoEvents();
                    if(!Fits(860))throw new Exception($"Case-only window {form.ClientSize} not refitted to the E650 case at zoom {zoom}");
                    form.TestAppearance("PC-E500");Application.DoEvents();
                    if(!Fits(744))throw new Exception($"Case-only window {form.ClientSize} not refitted to the E500 case at zoom {zoom}");
                    lcd=Lcd();
                }
                form.TestDoubleClick();Application.DoEvents();
                if(form.TestView!=WindowView.Screen || !surface.ScreenOnly || form.TransparencyKey!=Color.Empty || !Near(Lcd(),lcd) || Math.Abs(form.ClientSize.Width-surface.LcdArea().Width)>3)
                    throw new Exception($"Screen-only window: {form.TestView} {form.ClientSize} LCD at {Lcd()} from {lcd}");
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"window-screen.png"));}
                // Sizing keeps the glass's proportions, and the side opposite the edge dragged.
                {
                    var b=form.Bounds;var glass=surface.ShownSize;
                    var wider=form.FitSizing(new Rectangle(b.X,b.Y,b.Width+180,b.Height),2);
                    var taller=form.FitSizing(new Rectangle(b.X,b.Y-48,b.Width,b.Height+48),3);
                    var shrunk=form.FitSizing(new Rectangle(b.X+b.Width-20,b.Y,20,b.Height),1);
                    bool Fits(Rectangle r)=>Math.Abs(r.Height-glass.Height*r.Width/glass.Width)<=2;
                    if(!Fits(wider) || wider.Left!=b.Left || wider.Width!=b.Width+180 || !Fits(taller) || taller.Bottom!=b.Bottom || taller.Height<b.Height+40 ||
                       shrunk.Right!=b.Right || shrunk.Width<glass.Width*0.75f-1)
                        throw new Exception($"Screen-only sizing: {wider} {taller} {shrunk} from {b}");
                    if(surface.EdgeAt(new Point(surface.Width-2,surface.Height-2))!=17 || surface.EdgeAt(new Point(surface.Width/2,2))!=12)throw new Exception("Screen-only window sizing edges");
                }
                // The real display leaves a gap between two lit dots; packed, there is none.
                // Large, so that the gap is a whole pixel wide.
                form.ClientSize=new Size(1440,384);Application.DoEvents();core.Refresh();
                int pair=Enumerable.Range(0,240*32).First(i=>i%240<239 && core.Pixels[i]!=0 && core.Pixels[i+1]!=0);
                (int Center,int Gap) Sample() {
                    using var bitmap=new Bitmap(surface.Width,surface.Height);surface.DrawToBitmap(bitmap,surface.ClientRectangle);
                    var dots=surface.TestDotArea();float w=dots.Width/240,h=dots.Height/32,y=dots.Y+(pair/240+0.43f)*h;
                    int Light(float x){var c=bitmap.GetPixel((int)x,(int)y);return c.R+c.G+c.B;}
                    return (Light(dots.X+(pair%240+0.41f)*w),Light(dots.X+(pair%240+0.91f)*w));
                }
                var display=form.TestDisplay();Application.DoEvents();
                display.TestSet(false,PocketSurface.DefaultContrast);var packed=Sample();
                display.TestSet(true,PocketSurface.DefaultContrast);var real=Sample();
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"lcd-real.png"));}
                if(!surface.RealLcd || !form.TestSettings.LcdDotLines)throw new Exception("Display window did not turn the real LCD on");
                if(Math.Abs(packed.Gap-packed.Center)>30 || real.Gap<real.Center+100)throw new Exception($"LCD gap: packed {packed}, real {real}");
                // The legends that are off share the knob: faded out at the bottom, the
                // usual shade at 50, and at 100 the glass is black, off dots and all.
                static int Sum(Color c)=>c.R+c.G+c.B;
                var normalOff=surface.LcdColors().Off;
                display.TestSet(false,5);var fadedOff=surface.LcdColors().Off;
                display.TestSet(false,100);var black=surface.LcdColors();
                if(Sum(fadedOff)<Sum(normalOff)+80 || Sum(black.Lit)>60 || black.Unlit.A!=255 || black.Off!=black.Lit)
                    throw new Exception($"Contrast for the legends: off {fadedOff} at 5, {normalOff} at 50; at 100 {black}");
                {
                    int dark0=Enumerable.Range(0,240*32).First(i=>core.Pixels[i]==0);
                    using var bitmap=new Bitmap(surface.Width,surface.Height);surface.DrawToBitmap(bitmap,surface.ClientRectangle);
                    var dots=surface.TestDotArea();var c=bitmap.GetPixel((int)(dots.X+(dark0%240+0.5f)*dots.Width/240),(int)(dots.Y+(dark0/240+0.5f)*dots.Height/32));
                    if(Sum(c)>80)throw new Exception($"A dot that is off is {c} at contrast 100, not black");
                }
                display.TestSet(true,100);var dark=Sample();display.TestSet(true,0);var faint=Sample();
                if(form.TestSettings.LcdContrast!=0 || !(dark.Center<real.Center-20 && faint.Center>real.Center+100))throw new Exception($"LCD contrast: 0 {faint}, 50 {real}, 100 {dark}");
                display.TestSet(false,PocketSurface.DefaultContrast);
                if(surface.RealLcd || form.TestSettings.LcdContrast!=PocketSurface.DefaultContrast)throw new Exception("Display window did not go back to the default LCD");
                display.TestView(WindowView.Normal);Application.DoEvents();
                if(form.TestView!=WindowView.Normal || form.Bounds!=normal || form.FormBorderStyle!=FormBorderStyle.Sizable || surface.ScreenOnly)
                    throw new Exception($"Back to normal: {form.TestView} {form.Bounds} from {normal}");
                if(surface.Resizable || form.TestSettings.View!=WindowView.Normal)throw new Exception("Normal view still sized by the picture, or not kept in the settings");
                display.Close();
                // At start-up the saved view comes back at its zoom and place; a place
                // off every screen is not used, the view and zoom still are.
                var work=Screen.PrimaryScreen!.WorkingArea;
                form.TestRestoreView(form.TestSettings with{View=WindowView.Screen,ViewZoom=2,ViewLeft=work.Left+10,ViewTop=work.Top+10});Application.DoEvents();
                if(form.TestView!=WindowView.Screen || Math.Abs(surface.Zoom-2)>0.02f || form.Location!=new Point(work.Left+10,work.Top+10))
                    throw new Exception($"Saved view not restored: {form.TestView} zoom {surface.Zoom} at {form.Location}");
                form.TestRestoreView(form.TestSettings with{View=WindowView.Screen,ViewZoom=1.5f,ViewLeft=-100000,ViewTop=-100000});Application.DoEvents();
                if(form.Left<=-100000/2 || Math.Abs(surface.Zoom-1.5f)>0.02f)throw new Exception($"Saved view restored off the screens: {form.Location} zoom {surface.Zoom}");
                form.TestSetView(WindowView.Normal);Application.DoEvents();
                if(form.Bounds!=normal)throw new Exception("Normal window not back after a restored view");
            }
            core.Save(Path.Combine(output,"roundtrip.e500state"));
            byte[] expected=core.Pixels.ToArray();
            Key("BASIC");core.Run(1024000);
            if((Native.e500_symbols(core.Handle,0)&4)==0)throw new Exception("PRO mode not selected");
            Type("10 PRINT 123");Key("BASIC");core.Run(1024000);Type("RUN");core.Refresh();
            if(core.Pixels.SequenceEqual(expected))throw new Exception("Program output did not change LCD");
            core.Save(Path.Combine(output,"program.e500state"));
            ExpectThree(234,16);
            form.TestRefresh();Application.DoEvents();
            using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"basic-program.png"));}
            core.Load(Path.Combine(output,"roundtrip.e500state"));
            if(!core.Pixels.SequenceEqual(expected))throw new Exception("State roundtrip LCD mismatch");
            // A file from a newer build is refused as that, not as a broken one.
            {
                string json=File.ReadAllText(Path.Combine(output,"roundtrip.e500state"));
                if(!json.Contains("\"Version\":4",StringComparison.Ordinal))throw new Exception("State file version is not 4: "+json[..Math.Min(40,json.Length)]);
                string newer=Path.Combine(output,"newer.e500state");
                File.WriteAllText(newer,json.Replace("\"Version\":4","\"Version\":5",StringComparison.Ordinal));
                try{core.Load(newer);throw new Exception("A newer state file was loaded");}
                catch(InvalidDataException ex) when(ex.Message.Contains(L.T("新しい版","newer version"),StringComparison.Ordinal)){}
            }
            if(!form.TestPaste("2+3\r\n").Contains("4文字"))throw new Exception("Clipboard-style paste queue failed");
            if(!core.ScreenText().Contains('5'))throw new Exception("Pasted calculation did not reach LCD");
            Type("PRINT \"HELLO\"");form.TestRefresh();Application.DoEvents();
            if(!core.ScreenText().Contains("HELLO"))throw new Exception("LCD text decoder did not recognize ROM font");
            // Every shifted character has to survive whichever phase of the 64 Hz key
            // scan it lands on, typed one at a time and pasted in one run.
            string shifted="ABC\"DEF:GHI?JKL@MNO!PQR#STU$VWX%YZ&";
            void ExpectShifted(Action send,string how) {
                Key("ON/BRK");core.Run(1024000);send();
                // The line wraps at 40 columns, so join the rows before looking.
                string screen=core.ScreenText().Replace(Environment.NewLine,"");
                if(!screen.Contains(shifted,StringComparison.Ordinal))
                    throw new Exception($"Shifted keys lost their SHIFT ({how}): want [{shifted}] got [{screen.Replace(Environment.NewLine," / ")}]");
            }
            ExpectShifted(()=>Type2(shifted),"typed");
            ExpectShifted(()=>form.TestPaste(shifted),"pasted");
            // The keycaps' ¥ and ¦ type 5Ch and 7Ch, read back as \ and |.
            Key("ON/BRK");core.Run(1024000);form.TestPaste("¥¦\\|`");
            if(!core.ScreenText().Contains("\\|\\|`",StringComparison.Ordinal))throw new Exception("¥ and ¦ did not type 5Ch and 7Ch, or SHIFT+M not `: "+core.ScreenText());
            // "Load program": a BASIC text through the CE-140F, under a name the drive takes;
            // a BIN with the SAVE M header straight into memory; a bare BIN refused.
            {
                foreach(var (pc,drive) in new[]{("my prog.bas","MYPROG.BAS"),("長い名前.txt","LOAD.TXT"),("con.bas","_CON.BAS"),("abcdefghijk.basic","ABCDEFGH.BAS"),("game","GAME.BAS")})
                    if(MainForm.DriveName(pc)!=drive)throw new Exception($"{pc} became {MainForm.DriveName(pc)} on the CE-140F, not {drive}");
                string drivePath=Path.Combine(output,"load-drive");
                // Emptied, not removed: OneDrive can hold the folder itself for a while.
                Directory.CreateDirectory(drivePath);
                foreach(var file in Directory.GetFiles(drivePath))File.Delete(file);
                form.TestApplyHardware(form.TestSettings with{DiskEnabled=true,DiskReadOnly=false,DiskFolder=drivePath});
                string source=Path.Combine(output,"my prog.bas");File.WriteAllText(source,"10 PRINT 4321*2\r\n");
                Key("ON/BRK");core.Run(1024000);Type2("JUNK");
                string said=form.TestLoadFile(source);core.Run(768000*3);
                if(!File.Exists(Path.Combine(drivePath,"MYPROG.BAS")))throw new Exception("The BAS file was not put in the CE-140F folder: "+said);
                Type("RUN");core.Run(768000);
                if(!core.ScreenText().Contains("8642",StringComparison.Ordinal))throw new Exception("The loaded BASIC program did not run: "+said+" / "+core.ScreenText().Replace(Environment.NewLine," / "));
                // The file window's check box: Windows' dialog takes it and gives it back as set.
                if(!FileDialogs.TestPrepare(drivePath,true) || FileDialogs.TestPrepare(drivePath,false))throw new Exception("The file window's check box did not read back as it was set");
                // "Run after loading" types LOAD ...,R: the program runs with no RUN typed.
                string runSource=Path.Combine(output,"run me.bas");File.WriteAllText(runSource,"10 PRINT 1357*3\r\n");
                said=form.TestLoadFile(runSource,true);core.Run(768000*4);
                if(!said.Contains(",R",StringComparison.Ordinal) || !core.ScreenText().Contains("4071",StringComparison.Ordinal))
                    throw new Exception("Load and run did not run the program: "+said+" / "+core.ScreenText().Replace(Environment.NewLine," / "));
                // A program the machine saved goes straight into RAM: with the CE-140F
                // taken away and the machine at its menu, nothing typed, it is the
                // program RUN finds. The one in memory before (8642) is replaced.
                Type("SAVE \"X:SAVED.BAS\"");core.Run(768000*4);
                string saved=Path.Combine(drivePath,"SAVED.BAS");
                if(!File.Exists(saved) || !BasicImage.IsSaved(File.ReadAllBytes(saved)))throw new Exception("SAVE did not give a file in the machine's own form");
                form.TestLoadFile(source);core.Run(768000*3);
                // Byte for byte what LOAD typed at the prompt gives: the blocks and the pointer in internal RAM.
                {
                    string pre=Path.Combine(output,"before-direct.e500state");core.Save(pre);
                    Type("LOAD \"X:SAVED.BAS\"");core.Run(768000*4);
                    byte[] viaRom=BasicImage.TestImage(core);
                    core.Load(pre);
                    if(!BasicImage.Load(core,File.ReadAllBytes(saved)))throw new Exception("The saved program was not put straight into RAM");
                    if(!BasicImage.TestImage(core).AsSpan().SequenceEqual(viaRom))throw new Exception("Straight into RAM differs from LOAD in the blocks or the program pointer");
                    core.Load(pre);
                }
                form.TestApplyHardware(form.TestSettings with{DiskEnabled=false});
                Key("MENU");core.Run(1024000);
                said=form.TestLoadFile(saved);
                if(!said.Contains(L.T("直接","straight"),StringComparison.Ordinal))throw new Exception("A saved program did not go straight into RAM: "+said);
                Key("BASIC");core.Run(1024000);Type("RUN");core.Run(768000);
                if(!core.ScreenText().Contains("4071",StringComparison.Ordinal))throw new Exception("The program put straight into RAM did not run: "+core.ScreenText().Replace(Environment.NewLine," / "));
                // Run after loading types RUN itself; a text file without the CE-140F is refused as before.
                Type("CLS");said=form.TestLoadFile(saved,true);core.Run(768000*2);
                if(!core.ScreenText().Contains("4071",StringComparison.Ordinal))throw new Exception("Loading straight into RAM with run did not run it: "+said+" / "+core.ScreenText().Replace(Environment.NewLine," / "));
                try{form.TestLoadFile(source);throw new Exception("A text BAS loaded with no CE-140F");}catch(InvalidOperationException){}
                form.TestApplyHardware(form.TestSettings with{DiskEnabled=true});
                int ramTop=0xc0000-core.InternalKB*1024;
                string bin=Path.Combine(output,"poke.bin");
                void Bin(int to)=>File.WriteAllBytes(bin,[0xff,0x00,0x06,0x01,0x10,4,0,0,(byte)to,(byte)(to>>8),(byte)(to>>16),0xff,0xff,0xff,0x00,0x0f,0xde,0xad,0xbe,0xef]);
                // Over the slot header and the variables (in use): refused, nothing written.
                int used=ramTop+0x80;byte[] untouched=core.Dump(used,4);Bin(used);
                try{form.TestLoadFile(bin);throw new Exception("A BIN over the BASIC blocks in use was written");}catch(InvalidDataException){}
                if(!core.Dump(used,4).AsSpan().SequenceEqual(untouched))throw new Exception("A refused BIN changed the RAM");
                // Below the RAM there is (a mirror of it): refused.
                if(core.InternalKB<256){Bin(ramTop-0x100);try{form.TestLoadFile(bin);throw new Exception("A BIN below the RAM was written onto its mirror");}catch(InvalidDataException){}}
                // Into the free space: written (the question answered "load as it is").
                var gap=RamLayout.Internal(core).First(r=>r.Kind==RegionKind.Free && r.Size>=0x100);
                int at=gap.Start+0x40;byte[] keep=core.Dump(at,4);Bin(at);
                said=form.TestLoadFile(bin);
                if(core.Dump(at,4) is not [0xde,0xad,0xbe,0xef])throw new Exception("A BIN with the SAVE M header was not written to its address: "+said);
                core.Write(at,keep);
                File.WriteAllBytes(bin,[1,2,3,4]);
                try{form.TestLoadFile(bin);throw new Exception("A BIN without a header was written somewhere");}
                catch(InvalidDataException){}
                Key("ON/BRK");core.Run(1024000);
            }
            // With the auto power off held off, the machine left alone for longer than
            // its ten minutes stays on; with it on (the default), it switches itself off.
            {
                bool Off()=>(Native.e500_status(core.Handle)&1)!=0;
                int SecondsToOff(int limit) {
                    for(int s=1;s<=limit;s++){form.TestRunCycles(768000);form.TestKeepPowerOn();if(Off())return s;}
                    return -1;
                }
                form.TestApplyHardware(form.TestSettings with{AutoPowerOff=false});
                Key("ON/BRK");core.Run(1024000);
                if(SecondsToOff(700) is int held and >=0)throw new Exception($"The machine switched off after {held} s with the auto power off held off");
                form.TestApplyHardware(form.TestSettings with{AutoPowerOff=true});
                if(SecondsToOff(700) is int gone and <0)throw new Exception("The machine never switched itself off with the auto power off on");
                Key("ON/BRK");core.Run(1024000);
                if(Off())throw new Exception("ON did not wake the machine after its auto power off");
                Key("ON/BRK");core.Run(1024000);
            }
            // The status bar: the state, then the machine's parts in fields; the state
            // pauses and resumes, the CE-140F and CE-126P fields open their windows,
            // and the bar is gone with the window's frame.
            {
                var bar=form.TestStatusBar;form.TestRefresh();
                var fields=bar.TestParts.ToList();
                string[] wanted=[L.T("実行中","Running"),"ROM Ver.","CE-140F","CE-126P"];
                foreach(var w in wanted)if(!fields.Any(f=>f.Text.Contains(w,StringComparison.Ordinal)))throw new Exception($"The status bar has no field with {w}: "+bar.Text);
                if(!fields.First(f=>f.Text.Contains("CE-140F",StringComparison.Ordinal)).Clickable)throw new Exception("The CE-140F field does nothing when clicked");
                bar.TestClick(L.T("実行中","Running"));form.TestRefresh();
                if(!form.TestPaused || !bar.Text.Contains(L.T("一時停止","Paused"),StringComparison.Ordinal))throw new Exception("Clicking the state did not pause: "+bar.Text);
                bar.TestClick(L.T("一時停止","Paused"));form.TestRefresh();
                if(form.TestPaused)throw new Exception("Clicking the paused state did not resume");
                foreach(var (field,key) in new[]{("CE-140F","disk"),("CE-126P","printer")}) {
                    bar.TestClick(field);
                    if(!form.TestHasWindow(key))throw new Exception($"Clicking {field} in the status bar did not open its window");
                    form.TestWindow(key).Close();
                }
                // The speed lives in the status bar now: clicked, it goes round 0.5x, 1x, 2x, 4x.
                string Speed()=>bar.TestParts.Select(f=>f.Text).FirstOrDefault(t=>t.StartsWith(L.T("速度","Speed"),StringComparison.Ordinal))??"";
                if(Speed()!=L.T("速度 1x","Speed 1x"))throw new Exception("The status bar does not show the speed: "+bar.Text);
                if(form.TestToolbar.Any(t=>t.StartsWith(L.T("速度","Speed"),StringComparison.Ordinal)))throw new Exception("The speed is still on the toolbar");
                bar.TestClick(L.T("速度","Speed"));
                if(Speed()!=L.T("速度 2x","Speed 2x"))throw new Exception("Clicking the speed did not go to 2x: "+Speed());
                bar.TestClick(L.T("速度","Speed"));bar.TestClick(L.T("速度","Speed"));bar.TestClick(L.T("速度","Speed"));
                if(Speed()!=L.T("速度 1x","Speed 1x"))throw new Exception("Four clicks did not bring the speed back to 1x: "+Speed());
                // Always on top follows the setting at once.
                form.TestApplyHardware(form.TestSettings with{TopMost=true});
                if(!form.TopMost)throw new Exception("Always on top was not applied");
                form.TestApplyHardware(form.TestSettings with{TopMost=false});
                if(form.TopMost)throw new Exception("Always on top stayed after it was cleared");
                form.TestSetView(WindowView.Screen);
                if(bar.Visible)throw new Exception("The status bar stayed in the screen-only view");
                form.TestSetView(WindowView.Normal);
                if(!bar.Visible)throw new Exception("The status bar did not come back with the normal view");
            }
            // The machine-code area: sized through the ROM's own routine, back at the
            // BASIC prompt afterwards, shown in the status bar, given back with 0,
            // and a size larger than the free RAM refused.
            {
                Key("ON/BRK");core.Run(1024000);
                if((Native.e500_symbols(core.Handle,0)&6)==0){Key("BASIC");core.Run(1024000);}
                if(MachineCodeArea.Read(core) is not {Size:0})throw new Exception("A machine-code area was reserved before the test");
                // Its window shows the map and closes with Close (it once stayed open).
                using(var mlWindow=new MachineCodeDialog(RamLayout.Internal(core),0,0x1000)){mlWindow.Show(form);Application.DoEvents();mlWindow.TestClose();Application.DoEvents();if(mlWindow.Visible)throw new Exception("The machine-code area window did not close with Close");}
                string said=form.TestReserveMachineCode(0x1000);
                if(MachineCodeArea.Read(core) is not {Start:0xbec00,Size:0x1000})throw new Exception("The machine-code area did not become 1000h bytes: "+said+" / "+core.ScreenText().Replace(Environment.NewLine," / "));
                if((Native.e500_symbols(core.Handle,0)&6)==0)throw new Exception("Not back at BASIC after reserving the machine-code area: "+core.ScreenText().Replace(Environment.NewLine," / "));
                form.TestRefresh();
                if(!form.TestStatus.Contains(L.T("機械語 4KB","ML 4KB"),StringComparison.Ordinal))throw new Exception("The status bar does not show the machine-code area: "+form.TestStatus);
                Type("1+2");ExpectThree(234,24);
                form.TestReserveMachineCode(0);
                if(MachineCodeArea.Read(core) is not {Size:0})throw new Exception("Size 0 did not give the machine-code area back");
                try{form.TestReserveMachineCode(0x7ffff);throw new Exception("A machine-code area larger than the RAM was accepted");}catch(InvalidOperationException){}
                // A machine-code BIN outside the area: asked; Yes reserves from its start
                // up and writes it once that is done, No writes it as it is, Cancel writes
                // nothing; inside the area, nothing is asked.
                {
                    const int at=0xbec00;
                    byte[] payload=[0x11,0x22,0x33,0x44,0x55,0x66,0x77,0x88];
                    string mlBin=Path.Combine(output,"ml-offer.bin");
                    File.WriteAllBytes(mlBin,[0xff,0x00,0x06,0x01,0x10,8,0,0,at&0xff,(at>>8)&0xff,at>>16,0xff,0xff,0xff,0x00,0x0f,..payload]);
                    string? asked=null;DialogResult answer=DialogResult.Cancel;
                    form.AskMachineCode=q=>{asked=q;return answer;};
                    core.Write(at,new byte[8]);
                    try{form.TestLoadFile(mlBin);throw new Exception("Cancel still loaded the BIN");}catch(OperationCanceledException){}
                    if(asked is null || core.Dump(at,8).Any(v=>v!=0))throw new Exception("A BIN outside the machine-code area was written without asking, or after Cancel");
                    asked=null;answer=DialogResult.Yes;
                    string said2=form.TestLoadFile(mlBin);
                    if(asked is null || MachineCodeArea.Read(core) is not {Start:at,Size:0x1000} || !core.Dump(at,8).SequenceEqual(payload))
                        throw new Exception("Yes did not reserve the area and then load the BIN: "+said2+" / "+form.TestStatus);
                    if((Native.e500_symbols(core.Handle,0)&6)==0)throw new Exception("Not back at BASIC after reserving for a BIN");
                    asked=null;core.Write(at,new byte[8]);form.TestLoadFile(mlBin);
                    if(asked is not null || !core.Dump(at,8).SequenceEqual(payload))throw new Exception("A BIN inside the machine-code area asked, or was not written");
                    form.TestReserveMachineCode(0);
                    asked=null;answer=DialogResult.No;core.Write(at,new byte[8]);form.TestLoadFile(mlBin);
                    if(asked is null || MachineCodeArea.Read(core) is not {Size:0} || !core.Dump(at,8).SequenceEqual(payload))throw new Exception("No did not load the BIN as it is");
                    core.Write(at,new byte[8]);form.AskMachineCode=null;
                }
                Key("ON/BRK");core.Run(1024000);
            }
            // The afterimage: a dot that goes off fades over a few frames, then is off.
            {
                var lcd=form.TestSurface;
                string blankState=Path.Combine(output,"afterimage.e500state");
                Key("ON/BRK");core.Run(1024000);core.Save(blankState);
                Type2("8888");core.Refresh();
                lcd.SetAfterimage(true);lcd.UpdateLcd(1000);
                core.Load(blankState);core.Refresh();
                lcd.UpdateLcd(1000.016);
                int fading=-1;
                for(int i=0;i<240*32;i++)if(core.Pixels[i]==0 && lcd.TestShade(i) is > 0 and < 255){fading=i;break;}
                if(fading<0)throw new Exception("No dot was on its way off one frame after it went off");
                lcd.UpdateLcd(1001);
                for(int i=0;i<240*32;i++)if(lcd.TestShade(i)!=(core.Pixels[i]!=0?255:0))throw new Exception($"Dot {i} still between on and off a second later");
                // Motion: a dot stepping left beside letters that stay put, and a band
                // of every other dot stepping a dot, slide over as long as the last step took.
                var dots=core.Pixels;
                void Picture(int star,int band){
                    Array.Clear(dots);
                    for(int y=8;y<16;y++)dots[y*240+96]=1;
                    dots[12*240+star]=1;
                    for(int x=0;x<160;x++)if((x+band)%2==0)dots[28*240+x]=1;
                }
                Picture(100,0);lcd.UpdateLcd(2000);lcd.UpdateLcd(2001);
                Picture(99,1);lcd.UpdateLcd(2001.2);
                if(lcd.TestSliding!=0)throw new Exception($"{lcd.TestSliding} dots slid on the first step after a still");
                Picture(98,0);
                if(!lcd.UpdateLcd(2001.4))throw new Exception("Nothing on its way after a step");
                int sliding=lcd.TestSliding;
                if(sliding<80)throw new Exception($"Only {sliding} dots slid on the second step (star and band of 80)");
                lcd.UpdateLcd(2001.5);
                if(lcd.TestSliding!=sliding)throw new Exception("Dots stopped sliding half way through the step");
                lcd.UpdateLcd(2001.61);
                if(lcd.TestSliding!=0)throw new Exception($"{lcd.TestSliding} dots still sliding after the step's time");
                // A whole line of text at once is not a move.
                Array.Clear(dots);for(int x=0;x<60;x++)dots[2*240+x]=1;lcd.UpdateLcd(2001.7);
                if(lcd.TestSliding!=0)throw new Exception("Text slid");
                core.Refresh();lcd.UpdateLcd(2002);
                lcd.SetAfterimage(false);form.TestRefresh();
            }
            // Text a face has no ink for outlines to nothing, which cannot be stretched
            // to a span; it goes to GDI's own text drawing instead of an endless scale.
            using(var blank=new Bitmap(40,20))using(var g=Graphics.FromImage(blank))PocketSurface.Lettering(g,"​",2,30,5,12,Color.Black);
            // ÷ and × carry their BASIC characters as data, drawn small beside them.
            foreach(var (id,label,minor) in new[]{("/","÷","/"),("*","×","*")}) {
                var key=form.TestSurface.Keys.Single(k=>k.Id==id);
                if(key.Label!=label || key.Minor!=minor)throw new Exception($"Key {id} reads {key.Label} {key.Minor}");
            }
            Key("ON/BRK");core.Run(1024000);
            // A held key has to keep repeating - it is how a line gets rubbed out or a
            // cursor driven across one - but it must never run ahead of the machine.
            // Windows repeats 31 times a second and a keystroke costs the machine
            // 55 ms, so queueing every repeat used to build a backlog that grew for as
            // long as the key was down: two seconds of a held letter left 1.4 s of
            // typing and clicking still to come out after the key was released.
            const int TickCycles=6144,TicksPerRepeat=4;   // 8 ms ticks, 31 Hz repeat
            int worst=0,typed=0;
            void HoldFor(int ms,Action repeat,Action release) {
                int ticks=ms/8;
                for(int t=0;t<ticks+40;t++) {
                    if(t<ticks && t%TicksPerRepeat==0)repeat();
                    if(t==ticks)release();
                    form.TestRunCycles(TickCycles);
                    worst=Math.Max(worst,form.TestBacklogCycles/768);
                    if(t==ticks)typed=Joined().Length;
                }
            }
            string Joined()=>core.ScreenText().Replace(Environment.NewLine,"").TrimEnd('_');
            Key("ON/BRK");core.Run(1024000);
            // A held PC key holds the machine's key, and Windows' repeats change
            // nothing: the ROM itself decides what repeats. A letter does not (one A
            // for two seconds held, as on the machine); LEFT does, driven by the ROM
            // alone from one press, which is how a held key is told from taps.
            HoldFor(2000,()=>form.TestRepeat(MainForm.TestKey,'A'),()=>form.TestRelease(MainForm.TestKey));
            int letters=Joined().Length-Joined().TrimEnd('A').Length;
            if(letters!=1)throw new Exception($"A held for 2 s typed {letters} letters; the machine's own keys type one");
            Key("ON/BRK");core.Run(1024000);
            Type2("ABCDEFGH");
            bool pressed=false;
            HoldFor(2000,()=>{if(!pressed){form.TestRepeatKey(Keys.Left);pressed=true;}},()=>form.TestRelease(Keys.Left));
            Type2("Z");
            // The cell the cursor sits on reads back as neither letter, so check the
            // two ends: Z landed where A was, and CDEFGH never moved.
            string tail=Joined().Length>=8?Joined()[^8..]:Joined();
            if(tail.Length!=8 || tail[0]!='Z' || tail[2..]!="CDEFGH")
                throw new Exception($"LEFT held for 2 s did not repeat to the start of the line: [{Joined()}]");
            // The allowance is one keystroke in the machine's hand plus one queued
            // behind it: 18000+42000 cycles, or 78 ms. What matters is that it is a
            // constant - the old queue passed 1400 ms by the end of the same hold.
            if(worst>90)throw new Exception($"Held keys queued {worst} ms of backlog; the cap is one keystroke behind one in hand (78 ms)");
            Key("ON/BRK");core.Run(1024000);
            // A controller button holds its key as a PC key does, read again on every
            // tick without pressing it again. The D-pad and the stick on one key hold
            // it once: letting go of one leaves it down until the other is up too.
            var pads=PadConfig.Defaults();
            foreach(var preset in pads.Presets)foreach(var (control,binding) in preset.Map)
                if(!PadConfig.Parse(binding,out _,out _))throw new Exception($"Preset {preset.Name} binds {control} to an unknown key {binding}");
            if(!PadConfig.Parse("SHIFT+A",out int padCode,out int padModifier) || padCode!=Keyboard.Codes["A"] || padModifier!=Keyboard.Codes["SHIFT"])throw new Exception("SHIFT+A did not parse to A with SHIFT");
            if(pads.Current.Map["Up"]!="8")throw new Exception("The default preset is not the number keys 2/4/6/8");
            // The hold is checked on LEFT, which repeats: the cursor-key preset.
            pads.Active=pads.Presets[1].Name;
            form.TestSetPadConfig(pads);
            Type2("ABCDEFGH");
            int padTicks=0;
            HoldFor(2000,()=>{form.TestPad(padTicks++<30?["Left","LStickLeft"]:["LStickLeft"]);if(form.TestPadHeld!=1)throw new Exception($"The D-pad and the stick on LEFT held {form.TestPadHeld} keys; one was expected");},()=>form.TestPad());
            if(form.TestPadHeld!=0)throw new Exception("A controller key stayed held after every button was let go");
            Type2("Z");
            tail=Joined().Length>=8?Joined()[^8..]:Joined();
            if(tail.Length!=8 || tail[0]!='Z' || tail[2..]!="CDEFGH")
                throw new Exception($"LEFT held on the controller for 2 s did not repeat to the start of the line: [{Joined()}]");
            Key("ON/BRK");core.Run(1024000);
            // Settings read back: a broken binding and a second preset of one name are dropped.
            var stored=System.Text.Json.JsonSerializer.Deserialize<PadConfig>(pads.Json)!.Repair();
            if(stored.Presets.Count!=pads.Presets.Count || stored.Current.Map.Count!=pads.Current.Map.Count)throw new Exception("The controller settings did not read back as written");
            var badPads=new PadConfig{Active="none",Presets=[new(){Name="P",Map=new(){["A"]="NOKEY",["B"]="ENTER"}},new(){Name="P"}]}.Repair();
            if(badPads.Presets.Count!=1 || badPads.Active!="P" || badPads.Current.Map.Count!=1)throw new Exception("Repair kept a broken binding or a second preset of one name");
            // The window lists the controls a DirectInput pad has, lights a pressed one and stores a binding.
            // What this PC has plugged in, written down only (the tests pass with none).
            var padLines=new List<string>();var padNow=new HashSet<string>();
            var scanClock=System.Diagnostics.Stopwatch.StartNew();var scanned=GamePad.Scan();padLines.Add($"scan {scanClock.ElapsedMilliseconds} ms");
            foreach(var d in scanned)padLines.Add($"{d.Id}\t{d.Name}\tbuttons={d.Buttons} pov={d.Pov}\tread={GamePad.Read(d,padNow)} pressed=[{string.Join(",",padNow)}]\tcontrols={string.Join(",",GamePad.Controls(d))}");
            File.WriteAllLines(Path.Combine(output,"controllers.txt"),padLines.Count>0?padLines:["(none)"]);
            // Timings of what runs on the window's thread, written down with the scan.
            var padTime=System.Diagnostics.Stopwatch.StartNew();
            var padWindow=form.TestShowPad();
            padLines.Add($"open window {padTime.ElapsedMilliseconds} ms");
            if(scanned.Count>0) {
                padTime.Restart();padWindow.TestShow(scanned[0]);padLines.Add($"show {scanned[0].Id} rows {padTime.ElapsedMilliseconds} ms");
                padTime.Restart();padWindow.TestShow(null);padLines.Add($"show Xbox rows {padTime.ElapsedMilliseconds} ms");
                padTime.Restart();for(int i=0;i<100;i++)GamePad.Read(scanned[0],padNow,new PadAxes());padLines.Add($"read x100 {padTime.ElapsedMilliseconds} ms");
            }
            File.WriteAllLines(Path.Combine(output,"controllers.txt"),padLines);
            try {
                var testPad=new PadDevice("joy:1234:5678","Test pad",PadKind.DirectInput,15,Buttons:4,Pov:true,Axes:[(0,65535),(0,65535),null,(0,65535),null,null]);
                padWindow.TestShow(testPad);
                // Every control the pad has is somewhere in the picture, and nothing it has not.
                var drawn=padWindow.TestView.Drawn.ToHashSet();
                if(!drawn.SetEquals(GamePad.Controls(testPad)))throw new Exception($"The controller picture for a DirectInput pad drew [{string.Join(",",drawn)}], not its controls");
                // The window cannot be widened: there is nothing more to show.
                int padWidth=padWindow.Width;padWindow.Width=padWidth+300;
                if(padWindow.Width!=padWidth)throw new Exception($"The controller window widened from {padWidth} to {padWindow.Width}");
                // ELECOM numbers the face buttons X, Y, A, B: 1 left, 2 top, 3 bottom, 4 right.
                var elecom=PadConfig.Defaults().LayoutFor(new PadDevice("joy:056E:2010","ELECOM",PadKind.DirectInput,0,Buttons:13));
                if(elecom["Left"]!=1 || elecom["Top"]!=2 || elecom["Bottom"]!=3 || elecom["Right"]!=4 || elecom["LB"]!=5)throw new Exception("The ELECOM face-button places are not X Y A B");
                // Matching places: asked top, right, bottom, left, then the rest (skipped here).
                padWindow.TestLearn([2,4,3,1,..Enumerable.Repeat(0,PadView.Positions.Length-4)]);
                var learned=padWindow.TestConfig.Layouts.GetValueOrDefault("1234:5678");
                if(padWindow.TestLearning || learned is null || learned["Top"]!=2 || learned["Right"]!=4 || learned["Bottom"]!=3 || learned["Left"]!=1 || learned.Count!=4)
                    throw new Exception($"Matching button places kept [{(learned is null?"nothing":string.Join(",",learned))}]");
                if(padWindow.TestView.LabelAt("Top")!="2" || padWindow.TestView.LabelAt("Right")!="4")throw new Exception("The picture did not move the numbers to the matched places");
                if(!padWindow.TestView.Drawn.ToHashSet().SetEquals(GamePad.Controls(testPad)))throw new Exception("After matching, the picture lost or doubled a button");
                string[] padRows=padWindow.TestRows;
                foreach(var want in new[]{"Up","LStickLeft","AxisR-","Button4"})if(!padRows.Contains(want))throw new Exception($"The controller window did not list {want} for a DirectInput pad: {string.Join(",",padRows)}");
                if(padRows.Contains("A") || padRows.Contains("Button5") || padRows.Contains("AxisZ+"))throw new Exception("The controller window listed a control the pad has not");
                padWindow.TestPress("Button2");
                if(!padWindow.TestLit("Button2") || padWindow.TestLit("Button1"))throw new Exception("A pressed button did not light its row alone");
                padWindow.TestPress(new PadAxes{RX=0.7f,RY=-0.7f},"Button2","Up","AxisR-");
                using(var diShot=new Bitmap(padWindow.Width,padWindow.Height)){padWindow.DrawToBitmap(diShot,new Rectangle(0,0,padWindow.Width,padWindow.Height));diShot.Save(Path.Combine(output,"controller-di.png"));}
                padWindow.TestSet("Button3","SHIFT","A");
                if(padWindow.TestConfig.Current.Map.GetValueOrDefault("Button3")!="SHIFT+A")throw new Exception("The controller window did not store SHIFT+A for Button 3");
                padWindow.TestShow(null);
                if(!padWindow.TestRows.SequenceEqual(GamePad.XInputControls))throw new Exception("With no controller the window did not show the Xbox layout");
                if(!padWindow.TestView.Drawn.ToHashSet().SetEquals(GamePad.XInputControls))throw new Exception("The Xbox picture does not draw every Xbox control once");
                // What is pressed, in words, with the key each holds, in the order of the list.
                padWindow.TestShow(new PadDevice("xinput:0","Xbox",PadKind.XInput,0));
                padWindow.TestPress(new PadAxes{LX=-0.9f,LY=0.1f,RT=0.6f},"LStickLeft","A");
                var shownMap=padWindow.TestConfig.Current.Map;
                if(padWindow.TestPressedLine!=L.T("押しているボタン: ","Pressed: ")+string.Join(L.T("、",", "),$"A → {shownMap["A"]}",$"{GamePad.ControlName("LStickLeft")} → {shownMap["LStickLeft"]}"))
                    throw new Exception($"The pressed line read [{padWindow.TestPressedLine}]");
                using var padShot=new Bitmap(padWindow.Width,padWindow.Height);padWindow.DrawToBitmap(padShot,new Rectangle(0,0,padWindow.Width,padWindow.Height));padShot.Save(Path.Combine(output,"controller.png"));
            } finally {padWindow.Close();}
            // The case and keys cost 3.9 ms to draw and the LCD repaints 62 times a
            // second, so a frame that changes nothing but the dots must not redraw
            // them. WM_PAINT outranks the WM_TIMER that runs the machine, and a
            // repaint straddling a tick makes that tick late and its audio arrive in
            // a burst that the output buffer then has to throw away.
            var panel=form.TestSurface;
            using(var scratch=new Bitmap(Math.Max(1,panel.Width),Math.Max(1,panel.Height)))
            using(var paint=Graphics.FromImage(scratch)) {
                void Repaint(){paint.ResetTransform();panel.TestPaint(paint);}
                Repaint();int builds=panel.ChromeBuilds,patches=panel.ChromePatches;
                for(int i=0;i<10;i++)Repaint();
                if(panel.ChromeBuilds!=builds || panel.ChromePatches!=patches)throw new Exception($"Ten unchanged frames redrew the case {panel.ChromeBuilds-builds+panel.ChromePatches-patches} times");
                // A key or a legend patches only its own area of the cached case, and
                // the patched picture is the very one a whole redraw makes.
                byte[] Pixels() {
                    var bits=scratch.LockBits(new Rectangle(0,0,scratch.Width,scratch.Height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    try {var b=new byte[bits.Stride*bits.Height];System.Runtime.InteropServices.Marshal.Copy(bits.Scan0,b,0,b.Length);return b;} finally {scratch.UnlockBits(bits);}
                }
                void SameAsWhole(string what) {
                    var patched=Pixels();panel.TestRebuild();Repaint();
                    if(!patched.AsSpan().SequenceEqual(Pixels()))throw new Exception(what+" patched into the case differs from the case drawn whole");
                }
                panel.SetPressed("A",true);Repaint();
                if(panel.ChromeBuilds!=builds || panel.ChromePatches!=patches+1)throw new Exception("Pressing a key redrew the whole case, or nothing");
                SameAsWhole("A key going down");
                panel.SetPressed("A",false);Repaint();SameAsWhole("A key coming up");
                int legends=panel.ChromePatches;
                Key("CAPS");core.Refresh();Repaint();
                if(panel.ChromePatches!=legends+1)throw new Exception("CAPS did not patch the legends");
                SameAsWhole("The CAPS legend");
                Key("CAPS");core.Refresh();Repaint();SameAsWhole("The CAPS legend going out");
                // A frame whose dots and legends are as they were asks for no repaint.
                panel.UpdateLcd();int asked=panel.LcdRepaints;panel.UpdateLcd();panel.UpdateLcd();
                if(panel.LcdRepaints!=asked)throw new Exception("An unchanged LCD was repainted");
            }
            {
                // Every key, down and up, on both cases and at a size that does not fall
                // on whole pixels: a patch never leaves a neighbour's edge behind.
                // 全キーの押し下げと戻しが、どちらの外観でも、半端な大きさでも、全部描き直した絵と同じになる。
                void EveryKey(string where) {
                    using var shot=new Bitmap(Math.Max(1,panel.Width),Math.Max(1,panel.Height));using var pg=Graphics.FromImage(shot);
                    void Paint(){pg.ResetTransform();panel.TestPaint(pg);}
                    byte[] Bytes() {
                        var bits=shot.LockBits(new Rectangle(0,0,shot.Width,shot.Height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        try {var b=new byte[bits.Stride*bits.Height];System.Runtime.InteropServices.Marshal.Copy(bits.Scan0,b,0,b.Length);return b;} finally {shot.UnlockBits(bits);}
                    }
                    Paint();
                    foreach(var k in panel.Keys.ToArray())foreach(bool down in new[]{true,false}) {
                        panel.SetPressed(k.Id,down);Paint();var patched=Bytes();panel.TestRebuild();Paint();var whole=Bytes();
                        if(!patched.AsSpan().SequenceEqual(whole)) {
                            // Where they differ, in control pixels, and the key's patch.
                            int stride=whole.Length/shot.Height,minX=int.MaxValue,minY=int.MaxValue,maxX=-1,maxY=-1;
                            for(int i=0;i<whole.Length;i++)if(whole[i]!=patched[i]){int x=i%stride/4,y=i/stride;minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);}
                            throw new Exception($"{k.Id} {(down?"going down":"coming up")} ({where}) patched into the case differs from the case drawn whole at ({minX},{minY})-({maxX},{maxY}); its patch is {panel.TestKeyArea(k.Id)}, surface {panel.Width}x{panel.Height}");
                        }
                    }
                }
                var size=form.ClientSize;
                EveryKey("PC-E500");
                form.ClientSize=new Size(1013,611);Application.DoEvents();EveryKey("PC-E500, odd size");
                form.TestAppearance("PC-E650");Application.DoEvents();EveryKey("PC-E650, odd size");
                form.TestAppearance("PC-E500");form.ClientSize=size;Application.DoEvents();
            }
            Key("ON/BRK");core.Run(1024000);
            using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"keyboard-quotes.png"));}
            using(var transfer=new ScreenTextDialog(core.ScreenText,_=>"test")) {transfer.Show(form);Application.DoEvents();using var bitmap=new Bitmap(transfer.Width,transfer.Height);transfer.DrawToBitmap(bitmap,new Rectangle(0,0,transfer.Width,transfer.Height));bitmap.Save(Path.Combine(output,"text-transfer.png"));}
            using(var surface=new PocketSurface()) {
                foreach(string model in HardwareSettings.Appearances) {
                    surface.SetModel(model);
                    if(surface.Keys.Select(k=>k.Id).Distinct().Count()!=Keyboard.Codes.Count+1 || surface.Keys.Any(k=>k.Id!="RESET"&&!Keyboard.Codes.ContainsKey(k.Id)) || surface.Keys.Count(k=>k.Id=="RESET")!=1)throw new Exception("Physical keyboard mapping mismatch: "+model);
                    for(int i=0;i<surface.Keys.Count;i++)for(int j=i+1;j<surface.Keys.Count;j++)
                        if(surface.Keys[i].Bounds.IntersectsWith(surface.Keys[j].Bounds))throw new Exception("Overlapping physical keys: "+model);
                }
            }
            string banks=Path.Combine(output,"memory-roundtrip");
            foreach(int kb in HardwareSettings.Sizes) {
                int internalKB=HardwareSettings.CoerceInternal(core.RomKB,kb);
                // A card is an image file: its size is the capacity, its bytes the
                // contents, and the contents go back to the same file.
                string cardImage=Path.Combine(banks,$"card-{kb}KB.ram");
                Core.CreateCardImage(cardImage,kb);
                if(new FileInfo(cardImage).Length!=kb*1024 || File.ReadAllBytes(cardImage).Any(b=>b!=0xff))throw new Exception($"New {kb}KB card image is not blank");
                using(var first=new Core(rom,internalKB,cardImage)) {
                    if(first.CardKB!=kb || !first.HasCard || first.CardName!=$"card-{kb}KB.ram")throw new Exception($"Card image {kb}KB was not attached");
                    for(int slot=0;slot<2;slot++) {
                        int slotKB=slot==0?first.InternalKB:first.CardKB;
                        byte[] pattern=Enumerable.Range(0,slotKB*1024).Select(i=>(byte)(i*17+slotKB)).ToArray();
                        if(Native.e500_memory(first.Handle,slot,pattern,pattern.Length,1)==0)throw new Exception("RAM import failed");
                    }
                    first.MemoryFiles(banks,false);first.SaveCard();
                }
                using var restored=new Core(rom,internalKB,cardImage);restored.MemoryFiles(banks,true);
                for(int slot=0;slot<2;slot++) {
                    int slotKB=slot==0?restored.InternalKB:restored.CardKB;
                    byte[] pattern=Enumerable.Range(0,slotKB*1024).Select(i=>(byte)(i*17+slotKB)).ToArray();
                    var actual=new byte[pattern.Length];Native.e500_memory(restored.Handle,slot,actual,actual.Length,0);
                    if(!actual.SequenceEqual(pattern))throw new Exception($"RAM file roundtrip failed: {kb}KB slot {slot}");
                }
                if(Native.e500_peek(restored.Handle,0x40000)!=(byte)kb || Native.e500_peek(restored.Handle,0x7ffff)!=(byte)((kb*1024-1)*17+kb))throw new Exception($"Card image {kb}KB is not mapped at 40000 with its mirror");
            }
            string cardPath=Path.Combine(output,"memory-roundtrip","card-64KB.ram");
            using(var noCard=new Core(rom,64))
            using(var withCard=new Core(rom,64,cardPath)) {
                if(noCard.CardKB!=0 || noCard.HasCard || withCard.CardKB!=64)throw new Exception("Card slot state mismatch");
                if(withCard.Hash!=noCard.Hash)throw new Exception("A card must not change the ROM identity");
                // A snapshot resumes the CPU and internal RAM; the card stays as its file has it.
                string snapshot=Path.Combine(output,"card-snapshot.e500state");
                withCard.Run(1024000);withCard.Save(snapshot);
                byte[] marker=[0x11,0x22,0x33];withCard.Write(0x40000,marker);
                withCard.Load(snapshot);
                if(!withCard.Dump(0x40000,3).SequenceEqual(marker))throw new Exception("Loading a snapshot replaced the card contents");
                try {noCard.Load(snapshot);throw new Exception("A snapshot taken with a card was resumed without one");}
                catch(InvalidDataException){}
                try {withCard.Load(Path.Combine(output,"roundtrip.e500state"));throw new Exception("A snapshot taken without a card was resumed with one");}
                catch(InvalidDataException){}
            }
            // A raw SAVEM dump of the main ROM opens as the same ROM; an S3B-only dump is explained.
            if(loadedRom.Length==256*1024) {
                string dumped=Path.Combine(output,"S3A.ROM");
                byte[] dumpFile=new byte[16+loadedRom.Length];
                new byte[]{0xff,0,6,1,0x10,0,0,4,0,0,0x0c,0xff,0xff,0xff,0,0x0f}.CopyTo(dumpFile,0);loadedRom.CopyTo(dumpFile,16);
                File.WriteAllBytes(dumped,dumpFile);
                using(var fromDump=new Core(dumped,32))if(fromDump.RomHash!=core.RomHash)throw new Exception("A SAVEM-headered ROM dump did not open as the same ROM");
                string extension=Path.Combine(output,"S3B.ROM");
                byte[] extFile=new byte[16+128*1024];new byte[]{0xff,0,6,1,0x10,0,0,2,0,0,2,0xff,0xff,0xff,0,0x0f}.CopyTo(extFile,0);
                File.WriteAllBytes(extension,extFile);
                try {using var only=new Core(extension,32);throw new Exception("An S3B-only dump was accepted as a ROM");}
                catch(InvalidDataException ex){if(!ex.Message.Contains("S3B"))throw;}
            }
            string badCard=Path.Combine(output,"invalid-card.ram");
            File.WriteAllBytes(badCard,new byte[48*1024]);
            try {using var invalid=new Core(rom,64,badCard);throw new Exception("A 48KB card image was accepted");}
            catch(InvalidDataException){}
            try {using var missing=new Core(rom,64,Path.Combine(output,"no-such-card.ram"));throw new Exception("A missing card image was accepted");}
            catch(FileNotFoundException){}
            if(core.IsE650Family) {
                foreach(int kb in HardwareSettings.InternalSizes(core.RomKB)) {
                    using var expanded=new Core(rom,kb);
                    if(expanded.InternalKB!=kb)throw new Exception($"E650/U6000 internal RAM expansion to {kb}KB was not applied");
                }
            }
            var settings=new HardwareSettings(128,cardPath,true,true,Path.Combine(output,"disk"),false,70,false,"PC-E650",L.Default);
            using(var diskCore=new Core(rom)) {
                diskCore.MountDisk(settings.DiskFolder,false);
                if((Native.e500_disk_status(diskCore.Handle)&1)==0)throw new Exception("Workspace/OneDrive disk mount failed");
            }
            string config=Path.Combine(output,"settings-test.json");settings=settings with{RomFile=rom};settings.Save(config);
            if(HardwareSettings.Load(config)!=settings)throw new Exception("Hardware settings roundtrip failed");
            // The ROM last opened starts again, even from outside the rom folder (an
            // installed copy's is empty); one gone falls back to the rom folder.
            string emptyData=Path.Combine(output,"no-rom-data");Directory.CreateDirectory(Path.Combine(emptyData,"rom"));
            if(MainForm.StartRom(emptyData,settings)!=rom)throw new Exception("The ROM last opened was not opened at the start");
            if(MainForm.StartRom(emptyData,settings with{RomFile=Path.Combine(output,"gone.rom")})!=null)throw new Exception("A ROM that is gone was opened at the start");
            if(MainForm.StartRom(emptyData,HardwareSettings.Defaults)!=null)throw new Exception("An empty rom folder gave a ROM to start");
            // A settings file from before the contrast setting reads with the default contrast.
            File.WriteAllText(config,File.ReadAllText(config).Replace("\"LcdContrast\": 50,","").Replace(",\n  \"LcdContrast\": 50","").Replace(",\r\n  \"LcdContrast\": 50",""));
            if(File.ReadAllText(config).Contains("LcdContrast",StringComparison.Ordinal) || HardwareSettings.Load(config).LcdContrast!=PocketSurface.DefaultContrast)throw new Exception("Settings without a contrast do not read the default");
            // The first start (no file yet) follows the Windows language; a check time in the future is dropped.
            if(HardwareSettings.Load(Path.Combine(output,"no-such-settings.json")).Language!=L.Default)throw new Exception("The first start does not follow the Windows language");
            string later=Path.Combine(output,"settings-future.json");
            (new HardwareSettings{UpdateCheckedAt=DateTime.UtcNow.AddYears(3)}).Save(later);
            if(HardwareSettings.Load(later).UpdateCheckedAt!=default)throw new Exception("A check time in the future was kept");
            // A broken settings file no longer stops the start: it is kept aside, and the defaults are used.
            // 壊れた設定ファイルは別名で残し、既定の設定で始める。
            foreach(string broken in new[]{"{ not json",""}) {
                string folder=Path.Combine(Path.GetTempPath(),"pce500-settings-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                try {
                    string file=Path.Combine(folder,"settings.json");File.WriteAllText(file,broken);
                    var read=HardwareSettings.LoadOrReset(file,out string? problem,out string? kept);
                    if(read!=HardwareSettings.Defaults || problem is null)throw new Exception($"A broken settings file ({broken}) did not reset to the defaults");
                    if(File.Exists(file) || kept is null || !File.Exists(kept))throw new Exception("A broken settings file was not kept aside");
                } finally {Directory.Delete(folder,true);}
            }
            // A value this build does not take goes back to its default alone; the rest stays.
            File.WriteAllText(config,File.ReadAllText(config).Replace($"\"InternalKB\": {settings.InternalKB}","\"InternalKB\": 7").Replace($"\"Appearance\": \"{settings.Appearance}\"","\"Appearance\": \"PC-9999\""));
            if(!File.ReadAllText(config).Contains("\"InternalKB\": 7",StringComparison.Ordinal) || !File.ReadAllText(config).Contains("PC-9999",StringComparison.Ordinal))throw new Exception("The settings test file did not take the bad values");
            var coerced=HardwareSettings.LoadOrReset(config,out string? fine,out _);
            if(fine is not null || coerced.InternalKB!=32 || coerced.Appearance!="PC-E500" || coerced.DiskFolder!=settings.DiskFolder || coerced.CardPath!=settings.CardPath)
                throw new Exception("A settings file with one bad value was not kept with that value at its default");
            using(var dialog=new HardwareDialog(settings,core.RomKB,Path.Combine(output,"memory-roundtrip"))) {
                dialog.Show(form);Application.DoEvents();
                if(dialog.Over(settings)!=settings)throw new Exception("Hardware dialog values mismatch");
                // Settings the dialog does not show come through an apply as they are.
                if(dialog.Over(settings with{PrinterConnected=false}).PrinterConnected)throw new Exception("Hardware dialog reset the CE-126P connection");
                // A folder that cannot be opened refuses the settings and leaves the
                // drive on the folder it had.
                {
                    string good=Path.Combine(output,"apply-disk"),bad=Path.Combine(output,"apply-disk.txt");
                    Directory.CreateDirectory(good);File.WriteAllText(bad,"not a folder");
                    core.MountDisk(good,false);
                    try{form.TestApplyHardware(form.TestSettings with{DiskEnabled=true,DiskFolder=bad});throw new Exception("Settings with a file as the CE-140F folder were applied");}
                    catch(IOException){}
                    if((Native.e500_disk_status(core.Handle)&1)==0 || form.TestSettings.DiskFolder==bad)throw new Exception("A refused CE-140F folder unmounted the drive or was kept");
                    // The same with a new memory layout: refused before anything is switched.
                    int internalKB=form.TestSettings.InternalKB;
                    try{form.TestApplyHardware(form.TestSettings with{InternalKB=internalKB==64?32:64,DiskEnabled=true,DiskFolder=bad});throw new Exception("A new RAM size with a file as the CE-140F folder was applied");}
                    catch(IOException){}
                    if((Native.e500_disk_status(core.Handle)&1)==0 || form.TestSettings.DiskFolder==bad || form.TestSettings.InternalKB!=internalKB || form.TestCore!=core)
                        throw new Exception("A refused CE-140F folder with a new RAM size changed the machine");
                    core.MountDisk(null,false);
                }
                // The LCD's look belongs to the display window now.
                var look=dialog.Over(settings with{LcdDotLines=true,LcdContrast=80});
                if(!look.LcdDotLines || look.LcdContrast!=80)throw new Exception("Hardware dialog reset the LCD display settings");
                using var bitmap=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(bitmap,new Rectangle(0,0,dialog.Width,dialog.Height));bitmap.Save(Path.Combine(output,"hardware-settings.png"));
            }
            short[] tone=Enumerable.Range(0,6144).Select(i=>(short)(i%12<6?6000:-6000)).ToArray();
            using(var backlog=new AudioOutput{Volume=0}) {
                // A stalled frame hands over its catch-up audio all at once. Anything
                // kept beyond the cushion is delay that never drains, because the
                // device consumes at exactly the rate the machine produces.
                for(int i=0;i<300;i++)backlog.Submit(tone,384);
                if(backlog.BufferedSamples>4096)throw new Exception($"Audio backlog grew to {backlog.BufferedSamples} samples ({backlog.BufferedSamples/48}ms of delay)");
            }
            // The two things that made a keystroke take a second to arrive. First:
            // waveOutWrite blocks its caller - 91 ms on average here, 1.4 s at worst -
            // so it must not run on the thread that steps the CPU and reads the
            // keyboard. Second: it only blocks because the device was running dry, and
            // a device running dry also meant 94% of the audio never reached it. Feed
            // one second of 15.6 ms frames the way Tick does and check both.
            // Real time, so a busy PC (a build just done, a scan, a sync) can spoil one
            // second of it: measured up to three times, and failed only if every
            // try fails. A PC with no audio device (a remote session) skips it.
            // 実時間の計測なので、PC が忙しいと1回崩れることがある：3回まで測り直し、全部だめなら失敗。
            // 音声の機器がない PC（リモートのセッションなど）では飛ばす。
            for(int attempt=1;;attempt++) {
                using var pipeline=new AudioOutput{Volume=0};
                if(pipeline.Error is not null) {if(attempt==1)break;throw new Exception(pipeline.Error);}
                const int Frame=740;   // 15.6 ms at 48 kHz
                var handoff=new System.Diagnostics.Stopwatch();
                double slowest=0;long sent=0,until=Environment.TickCount64+1000;
                while(Environment.TickCount64<until) {
                    handoff.Restart();pipeline.Submit(tone,Frame);slowest=Math.Max(slowest,handoff.Elapsed.TotalMilliseconds);
                    sent+=Frame;Thread.Sleep(15);
                }
                Thread.Sleep(100);
                string? problem=slowest>25?$"Handing audio over blocked the caller for {slowest:F0} ms; the device must only be touched from the audio thread"
                    :pipeline.Delivered*2<sent?$"Only {pipeline.Delivered} of {sent} samples reached the device; it is starving and every write is restarting the stream"
                    :pipeline.Restarts>2?$"The device ran dry {pipeline.Restarts} times in a second of steady frames; the cushion is too shallow"
                    :pipeline.Error;
                if(problem is not null){if(attempt<3)continue;throw new Exception(problem+$" (in each of {attempt} tries)");}
                var deadline=Environment.TickCount64+2000;
                while(pipeline.PendingCount>0 && Environment.TickCount64<deadline){Application.DoEvents();Thread.Sleep(10);}
                if(pipeline.PendingCount!=0)throw new Exception("Windows audio buffers did not complete");
                break;
            }
            foreach(string model in HardwareSettings.Appearances) {
                form.TestAppearance(model);Application.DoEvents();
                using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"desktop-"+model+".png"));
            }
            form.TestAppearance("PC-E500");Application.DoEvents();
            form.Size=form.MinimumSize;Application.DoEvents();
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"desktop-minimum.png"));}
            // Space and Enter are pocket-computer keys, so no toolbar button may take
            // focus: a focused button activates itself on Space and eats the keystroke.
            int focusable=form.TestFocusableButtons();
            if(focusable!=0)throw new Exception($"{focusable} toolbar buttons can take focus and would swallow Space");
            // One key must not occupy more emulated time than a person types in,
            // or the queue backs up and every later keystroke arrives late.
            int keyCycles=form.TestKeyCycles("5");
            if(keyCycles>45000)throw new Exception($"A key costs {keyCycles} cycles ({keyCycles/768}ms); typing would back up");
            if(form.TestKeyCycles("BASIC")>keyCycles+204800)throw new Exception("Mode key settling grew past 267ms");
            // Every sub-window is modeless: reaching this line at all proves none of
            // them blocked on ShowDialog, and the machine must still take input.
            string[] keys=["hardware","disk","text","debug","printer","display"];
            var opened=keys.Select(form.TestWindow).ToArray();
            Application.DoEvents();
            for(int i=0;i<keys.Length;i++) {
                if(!opened[i].Visible)throw new Exception("Sub-window did not open: "+keys[i]);
                if(form.TestWindow(keys[i])!=opened[i])throw new Exception("Sub-window opened a second instance: "+keys[i]);
            }
            byte[] beforeInput=core.Pixels.ToArray();
            Type("PRINT 7");Application.DoEvents();core.Refresh();
            if(core.Pixels.SequenceEqual(beforeInput))throw new Exception("Main window stopped accepting input while sub-windows were open");
            using(var bitmap=new Bitmap(opened[1].Width,opened[1].Height)) {
                opened[1].DrawToBitmap(bitmap,new Rectangle(0,0,opened[1].Width,opened[1].Height));bitmap.Save(Path.Combine(output,"disk-trace.png"));
            }
            // LPRINT reaches the CE-126P paper through the ROM's own printer driver.
            // No message loop runs in between: the core hands the lines over itself.
            Type("LPRINT \"SMOKE 0O\"");
            if(form.TestPaper.Count==0 || !form.TestPaper.Text().EndsWith("SMOKE 0O\r\n",StringComparison.Ordinal))throw new Exception("LPRINT did not reach the CE-126P paper: "+form.TestPaper.Text());
            // The ROM sends E8h-EBh and F1h-F4h as F5h-F8h and F4h, F1h-F3h; they come
            // back as the PC-E500 codes and print with the ROM's glyphs.
            Type("LPRINT CHR$(232);CHR$(241)");
            var symbolsLine=form.TestPaper.Lines[^1];
            if(!symbolsLine.SequenceEqual(new byte[]{0xe8,0xf1}) || !form.TestPaper.Text().EndsWith("♠円\r\n",StringComparison.Ordinal))throw new Exception("CE-126P symbols: "+Convert.ToHexString(symbolsLine));
            using(var symbols=form.TestPaper.Render(form.TestCore.Font,PrinterPaper.Margin+(form.TestPaper.Count-1)*PrinterPaper.LineHeight,8)) {
                for(int col=0;col<2;col++) {
                    bool ink=false;
                    for(int x=0;x<PrinterPaper.CellWidth;x++)for(int y=0;y<8;y++)ink|=symbols.GetPixel(PrinterPaper.Margin+col*PrinterPaper.CellWidth+x,y).ToArgb()!=Color.White.ToArgb();
                    if(!ink)throw new Exception($"CE-126P printed column {col} of the symbols blank");
                }
            }
            // 5Ch and 7Ch print as the ROM's ¥ and ¦, and the text taken from the paper says so.
            Type("LPRINT CHR$(92);CHR$(124)");
            if(!form.TestPaper.Text().EndsWith("¥¦\r\n",StringComparison.Ordinal))throw new Exception("CE-126P text of 5Ch and 7Ch: "+form.TestPaper.Text()[^8..]);
            if(PrinterPaper.Columns!=24 || PrinterPaper.Width!=PrinterPaper.Margin*2+24*PrinterPaper.CellWidth)throw new Exception("CE-126P columns not taken from the core: "+PrinterPaper.Columns);
            {
                // The lines go on to a printer as well: a USB thermal printer in ESC/POS,
                // or a Windows printer. Never the user's paper here - the writes are caught.
                // 印字はプリンタへも送る（USB のサーマルプリンタか Windows のプリンタ）。テストでは書き込みを横取りする。
                byte[] font=form.TestCore.Font;
                byte[] Codes(string s)=>[..s.Select(c=>(byte)c)];
                // The raster is the ESP32-S3 build's: GS v 0, 48 bytes by 32 rows, the glyph at three times.
                var raster=ThermalRaster.Line(Codes("A"),font);
                if(raster.Length!=8+32*48 || !raster.AsSpan(0,8).SequenceEqual(new byte[]{0x1d,0x76,0x30,0,48,0,32,0}) || raster.Skip(8).All(b=>b==0))throw new Exception("The thermal raster of a line is wrong");
                var sample=new[]{"PC-E500 + CE-126P","10 PRINT \"HELLO\"","0123456789ABCDEFGHIJKLMN"}.Select(Codes).Append(form.TestPaper.Lines[^2]).ToArray();
                using(var preview=ThermalRaster.Preview(sample,font))preview.Save(Path.Combine(output,"thermal-preview.png"));
                // A USB printer: reset, the lines, and after the pause the feed, then let go of.
                var caught=new MemoryStream();
                var usb=new PrintOutput{OpenUsb=_=>caught};
                try {
                    usb.Select(new PrintTarget(PrintTarget.Usb,@"\\?\test","Test"));
                    usb.Line(sample[0],font);usb.Line(sample[1],font);
                    if(!usb.Flush(5000))throw new Exception("The printer output did not flush");
                    byte[] sent=[..ThermalRaster.Reset,..ThermalRaster.Line(sample[0],font),..ThermalRaster.Line(sample[1],font),..ThermalRaster.Feed(ThermalRaster.FeedDots)];
                    if(!caught.ToArray().SequenceEqual(sent) || usb.Sent!=2 || usb.Failed)throw new Exception("The USB thermal printer was not sent reset, lines and feed: "+usb.Status);
                    // A printer that cannot be opened is reported; the machine goes on.
                    usb.OpenUsb=_=>throw new IOException("unplugged");
                    usb.Line(sample[0],font);usb.Flush(5000);
                    if(!usb.Failed || !usb.Status.Contains("unplugged",StringComparison.Ordinal))throw new Exception("A printer that could not be opened was not reported");
                    // A write that fails lets go of the device: the next line opens it again.
                    int opens=0;
                    usb.OpenUsb=_=>{opens++;return opens==1?new FailingStream():new MemoryStream();};
                    usb.Line(sample[0],font);usb.Line(sample[1],font);usb.Flush(5000);
                    if(opens!=2 || usb.Failed)throw new Exception($"A device that failed a write was used again ({opens} opens): "+usb.Status);
                    // A write the printer never takes (out of paper) gives up, and says why.
                    usb.WriteTimeout=300;usb.OpenUsb=_=>new HangingStream();
                    usb.Line(sample[0],font);
                    if(!usb.Flush(10000) || !usb.Failed)throw new Exception("A write the printer never took held the printer thread");
                } finally {usb.Dispose();}
                // A Windows printer: the lines gathered into one job, page after page at 4 mm a line.
                string? queue=System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().FirstOrDefault();
                if(queue is not null) {
                    int pages=0,jobs=0;
                    var page=new PrintOutput{PrintPages=document=>{
                        var preview=new System.Drawing.Printing.PreviewPrintController();document.PrintController=preview;document.Print();
                        pages=preview.GetPreviewPageInfo().Length;jobs++;
                    }};
                    try {
                        page.Select(new PrintTarget(PrintTarget.Windows,queue,queue));
                        // Lines that never pause still print, a page's worth (60) at a time.
                        for(int i=0;i<150;i++)page.Line(Codes($"LINE {i}"),font);
                        page.Flush(30000);
                        if(page.Failed || jobs!=3 || page.Sent!=150)throw new Exception($"150 unpaused lines did not print as 3 jobs ({jobs} jobs): "+page.Status);
                        // The whole paper at once runs on over the pages.
                        page.Sheet([..Enumerable.Range(0,150).Select(i=>Codes($"SHEET {i}"))],font);
                        page.Flush(30000);
                        if(page.Failed || pages<2 || jobs!=4)throw new Exception($"A Windows printer did not print 150 lines over pages ({pages} pages): "+page.Status);
                        // Lines waiting for a job print before the paper does, not inside it again.
                        int sent=page.Sent;
                        for(int i=0;i<5;i++)page.Line(Codes($"WAIT {i}"),font);
                        page.Sheet([..Enumerable.Range(0,5).Select(i=>Codes($"PAPER {i}"))],font);page.Flush(30000);
                        if(jobs!=6 || page.Sent!=sent+10)throw new Exception($"Lines waiting were printed again with the paper ({jobs} jobs, {page.Sent-sent} lines)");
                    } finally {page.Dispose();}
                }
                // What this machine has: every printer found, and whether a USB one opens
                // for writing (opened and closed at once - nothing is written to it).
                // この PC のプリンタの一覧と、USB のものが書き込み用に開けるか（開いてすぐ閉じる。何も書かない）。
                var found=PrintTarget.Find();
                if(System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().Any(q=>!found.Any(t=>t.Kind==PrintTarget.Windows && t.Id==q)))throw new Exception("A Windows printer is missing from the list");
                File.WriteAllLines(Path.Combine(output,"print-targets.txt"),found.Select(t=>{
                    if(t.Kind!=PrintTarget.Usb)return t.Label;
                    try {using(PrintOutput.TestOpen(t.Id)){}return t.Label+"  [opens]  "+t.Id;} catch(Exception ex){return t.Label+"  [does not open: "+ex.Message+"]  "+t.Id;}
                }));
                // The chosen printer is kept in the settings.
                string kept=Path.Combine(output,"settings-print.json");
                var withPrinter=new HardwareSettings{PrintTo=new PrintTarget(PrintTarget.Usb,@"\\?\usb#test","YICHIP -Printer demo")};
                withPrinter.Save(kept);
                if(HardwareSettings.Load(kept).PrintTo!=withPrinter.PrintTo)throw new Exception("The chosen printer did not survive the settings file");
                // A USB printer found under another path keeps its place by name.
                var moved=new PrintTarget(PrintTarget.Usb,@"\\?\usb#other","YICHIP -Printer demo");
                if(PrintTarget.Resolve(withPrinter.PrintTo,[moved])!=moved)throw new Exception("A USB printer on another port was not found by its name");
                // LPRINT on the machine reaches the chosen printer.
                var machine=new MemoryStream();form.TestPrintOut.OpenUsb=_=>machine;
                form.TestPrintOut.Select(new PrintTarget(PrintTarget.Usb,@"\\?\test","Test"));
                try {
                    Type("LPRINT \"TO THE PRINTER\"");form.TestPrintOut.Flush(5000);
                    if(machine.ToArray().AsSpan().IndexOf(ThermalRaster.Line(Codes("TO THE PRINTER"),font))<0)throw new Exception("LPRINT did not reach the chosen printer");
                } finally {form.TestPrintOut.Select(null);form.TestPrintOut.Flush(5000);}
            }
            var printer=(PrinterDialog)opened[4];
            printer.Render();Application.DoEvents();
            if(!printer.TestView.AtBottom)throw new Exception("CE-126P view does not show the newest line");
            using(var bitmap=new Bitmap(opened[4].Width,opened[4].Height)) {
                opened[4].DrawToBitmap(bitmap,new Rectangle(0,0,opened[4].Width,opened[4].Height));bitmap.Save(Path.Combine(output,"printer.png"));
            }
            // A full roll: past the 32767-pixel window limit at 3x, and each new
            // line costs only the rows in view.
            var roll=Enumerable.Range(0,PrinterPaper.MaxLines+100).SelectMany(i=>System.Text.Encoding.ASCII.GetBytes($"LINE {i,5} 0123456789AB\n")).ToArray();
            form.TestPaper.Feed(roll);printer.Render();Application.DoEvents();
            if(form.TestPaper.Count!=PrinterPaper.MaxLines || !printer.TestView.AtBottom)throw new Exception("CE-126P full roll is not shown at its newest line");
            printer.TestView.Refresh();
            int visibleDots=printer.TestView.ClientSize.Height/PaperView.Zoom;
            if(printer.TestView.RenderedDots<=0 || printer.TestView.RenderedDots>visibleDots)throw new Exception($"CE-126P drew {printer.TestView.RenderedDots} dots of a full roll for {visibleDots} in view");
            using(var bitmap=new Bitmap(opened[4].Width,opened[4].Height)) {
                opened[4].DrawToBitmap(bitmap,new Rectangle(0,0,opened[4].Width,opened[4].Height));bitmap.Save(Path.Combine(output,"printer-full-roll.png"));
            }
            // Reading back up is not interrupted by more printing, even as the full
            // roll loses its top line; at the bottom the view follows.
            printer.TestView.ScrollTo(10);var reading=form.TestPaper.Lines[10];
            form.TestPaper.Feed("HOLD\n"u8);printer.Render();
            if(!ReferenceEquals(form.TestPaper.Lines[printer.TestView.FirstLine],reading))throw new Exception($"CE-126P new line moved the text in view (now at line {printer.TestView.FirstLine})");
            // Touchpads scroll in steps finer than a wheel notch (120 = three lines).
            int before=printer.TestView.FirstLine;
            printer.TestView.TestWheel(-30);printer.TestView.TestWheel(-30);
            if(printer.TestView.FirstLine!=before+1)throw new Exception($"CE-126P two 30-unit wheel steps scrolled {printer.TestView.FirstLine-before} lines");
            // A notch the other way is three lines still: the remainder is dropped on the turn.
            printer.TestView.TestWheel(120);
            if(printer.TestView.FirstLine!=before-2)throw new Exception($"CE-126P a wheel notch after them scrolled to {printer.TestView.FirstLine} from {before+1}");
            printer.TestView.ScrollTo(int.MaxValue);form.TestPaper.Feed("FOLLOW\n"u8);printer.Render();
            if(!printer.TestView.AtBottom)throw new Exception("CE-126P view stopped following the newest line");
            // A message under the paper stays while more lines come in.
            var attach=printer.TestAttach;
            attach.Checked=false;string message=printer.TestResult;
            form.TestPaper.Feed("MORE\n"u8);printer.Render();
            if(printer.TestResult!=message || form.TestCore.PrinterConnected)throw new Exception("CE-126P message was overwritten, or disconnecting did not reach the core: "+printer.TestResult);
            if(form.TestPrinterSetting)throw new Exception("CE-126P disconnection is not kept in the settings");
            attach.Checked=true;
            if(!form.TestCore.PrinterConnected || !form.TestPrinterSetting)throw new Exception("CE-126P did not reconnect");
            form.TestPaper.Clear();printer.Render();
            if(printer.TestView.FirstLine!=0)throw new Exception("CE-126P cleared paper did not return to the top");
            // The screen-text decoder keeps to its own set: a cell drawn with the
            // printer-only symbol glyphs (E8h) reads as unknown, letters still read.
            var decoder=new LcdTextDecoder(loadedRom);var cells=new byte[240*32];
            foreach(var (code,col) in new[]{(0xe8,0),(0x41,1)})
                for(int x=0;x<6;x++)for(int y=0;y<8;y++)cells[y*240+col*6+x]=(byte)(decoder.Font[(code-0x20)*6+x]>>y&1);
            if(!decoder.Decode(cells).StartsWith("□A",StringComparison.Ordinal))throw new Exception("LCD text decoder read a printer symbol: "+decoder.Decode(cells));
            // A ROM without a font found prints blank paper, and the window says why.
            using(var blind=new PrinterDialog(new PrinterPaper(),()=>[],()=>true,null,_=>{},_=>{})) {
                blind.Render();
                if(blind.TestNote==printer.TestNote)throw new Exception("CE-126P window did not tell that the ROM font is missing");
            }
            // Switched to a ROM whose font is found, the note follows with nothing printed.
            byte[] romFont=[];
            using(var switching=new PrinterDialog(new PrinterPaper(),()=>romFont,()=>true,null,_=>{},_=>{})) {
                switching.Render();string blindNote=switching.TestNote;
                romFont=form.TestCore.Font;switching.TestFollow();
                if(switching.TestNote==blindNote || switching.TestNote!=printer.TestNote)throw new Exception("CE-126P window kept the note of the ROM before");
            }
            // A state saved in the middle of a line - paused there, say - resumes
            // the line: the printer's place goes into the snapshot.
            var lineCore=form.TestCore;int enter=Keyboard.Codes["ENTER"];const string printedLine="ABCDEFGHIJKLMNOPQRSTUVWX\r\n";
            form.TestTypeOnly("LPRINT \"ABCDEFGHIJKLMNOPQRSTUVWX\"");
            lineCore.Key(enter,true);
            int busy=0;
            for(int i=0;i<4000 && busy<12;i++){lineCore.Run(1024);if((Native.e500_printer_status(lineCore.Handle)&2)!=0)busy++;}
            lineCore.Key(enter,false);
            if(busy<12 || !lineCore.CanSnapshot)throw new Exception($"CE-126P line held off a snapshot, or never started: busy {busy}, can save {lineCore.CanSnapshot}, paper [{form.TestPaper.Text()}], screen [{lineCore.ScreenText()}]");
            string midline=Path.Combine(output,"midline.e500state");lineCore.Save(midline);
            lineCore.Run(4096000);form.TestPaper.Clear();
            lineCore.Load(midline);lineCore.Run(4096000);
            if(form.TestPaper.Text()!=printedLine)throw new Exception("CE-126P line restored from mid-line: "+form.TestPaper.Text());
            // A state in a layout this build does not know is an invalid file, not a
            // RAM card at odds with the slot.
            var future=System.Text.Json.JsonSerializer.Deserialize<SavedState>(File.ReadAllText(midline))!;
            byte[] futureData=[..future.Data];"E500ST99"u8.CopyTo(futureData);BitConverter.GetBytes(64).CopyTo(futureData,12);
            string futurePath=Path.Combine(output,"future.e500state");
            File.WriteAllText(futurePath,System.Text.Json.JsonSerializer.Serialize(future with{DataSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(futureData)),Data=futureData}));
            try {lineCore.Load(futurePath);throw new Exception("A state of an unknown layout loaded");}
            catch(InvalidDataException ex) when(!ex.Message.Contains("RAM",StringComparison.Ordinal)) {}
            foreach(var window in opened)window.Close();
            Application.DoEvents();
            var debugger=form.TestDebugger();
            try {
                Application.DoEvents();debugger.Render();Application.DoEvents();
                using var bitmap=new Bitmap(debugger.Width,debugger.Height);
                debugger.DrawToBitmap(bitmap,new Rectangle(0,0,debugger.Width,debugger.Height));bitmap.Save(Path.Combine(output,"debugger.png"));
                if(form.TestDebugger()!=debugger)throw new Exception("Debug window was opened twice");
                // The registers open at their top, not scrolled to the end as if following a trace.
                if(debugger.TestCpuTopLine!=0)throw new Exception($"The register view opened scrolled to line {debugger.TestCpuTopLine}");
                byte[] romBytes=core.Dump(0xc0000,16),ram=core.Dump(0xc0000-core.InternalKB*1024,16);
                for(int i=0;i<16;i++) {
                    if(romBytes[i]!=Native.e500_peek(core.Handle,0xc0000+i))throw new Exception("Debug dump disagrees with the bus in ROM");
                    if(ram[i]!=Native.e500_peek(core.Handle,0xc0000-core.InternalKB*1024+i))throw new Exception("Debug dump disagrees with the bus in internal RAM");
                }
                // The LCD command window must be read behind the controller, so a
                // dump there sees the raw bus while a bus read answers the LCD.
                if(core.Dump(0x2005,1)[0]!=0xff || Native.e500_peek(core.Handle,0x2005)==0xff)throw new Exception("Debug dump went through the LCD controller");
                if(core.Cycles==0 || core.InternalMemory().Length!=256)throw new Exception("Debug CPU state unavailable");
                try {core.Dump(0xfffff,2);throw new Exception("Debug dump accepted a range past the address space");}
                catch(ArgumentOutOfRangeException){}
                foreach(string entered in new[]{"B0000"," b0000 ","&HB0000","0xB0000","$B0000"})
                    if(!DebugDialog.TryParseAddress(entered,out int parsed) || parsed!=0xb0000)throw new Exception("Debug address parsing failed: "+entered);
                if(DebugDialog.TryParseAddress("zz",out _))throw new Exception("Debug address parsing accepted a non-hex value");
                // Writes: RAM only, through the bus mirror, and the hex view types them in place.
                int ramTop=0xc0000-core.InternalKB*1024;
                byte[] keep=core.Dump(ramTop,32);
                core.Write(ramTop,[0xde,0xad]);
                if(core.Dump(ramTop,2) is not [0xde,0xad] || Native.e500_peek(core.Handle,0x80000)!=0xde)throw new Exception("Debug write did not reach internal RAM");
                try {core.Write(0xc0000,[0]);throw new Exception("Debug write patched ROM");}
                catch(InvalidOperationException){}
                try {core.Write(0x10000,[0]);throw new Exception("Debug write landed in unmapped space");}
                catch(InvalidOperationException){}
                if(MemoryMap.Kind(core,0xc0000)!=MemoryKind.Rom || MemoryMap.Kind(core,0x10000)!=MemoryKind.Unmapped || MemoryMap.Kind(core,ramTop)!=MemoryKind.Ram || MemoryMap.Kind(core,0x40000)!=(core.CardKB>0?MemoryKind.Ram:MemoryKind.Unmapped))throw new Exception("Memory map colouring is wrong");
                var hex=debugger.Memory;
                hex.Jump(ramTop+2);
                if(hex.Caret!=ramTop+2 || hex.Address>ramTop+2 || hex.TestScrollValue!=hex.Address/16)throw new Exception("Hex view caret/scrollbar did not follow a jump");
                // A row: two groups of eight bytes, each with its checksum (the sum's low byte).
                {
                    var row=new System.Text.RegularExpressions.Regex("^[0-9A-F]{5}  (([0-9A-F]{2} ){8}: [0-9A-F]{2})(  - ([0-9A-F]{2} ){8}: [0-9A-F]{2})?$");
                    core.Write(ramTop,[0xff,0xff,0x01,0x10,0,0,0,0,1,2,3,4,5,6,7,8]);hex.Jump(ramTop);hex.Reload();
                    string line=hex.TestRowText(0);
                    if(!row.IsMatch(line) || !line.Contains(" : 0F  - ",StringComparison.Ordinal) || !line.EndsWith(" : 24",StringComparison.Ordinal))throw new Exception("A 16-byte row is not two groups with their checksums: "+line);
                    debugger.TestRowWidth(1);hex.Reload();line=hex.TestRowText(1);
                    if(hex.Columns!=8 || !row.IsMatch(line) || line.Contains(" - ",StringComparison.Ordinal) || !line.EndsWith(" : 24",StringComparison.Ordinal) || hex.TestScrollValue!=hex.Address/8)throw new Exception("An 8-byte row is wrong: "+line);
                    debugger.TestRowWidth(0);core.Write(ramTop,keep[..16]);hex.Jump(ramTop+2);
                }
                byte unedited=core.Dump(ramTop+2,1)[0];
                debugger.TestType(Keys.D5);
                if(core.Dump(ramTop+2,1)[0]!=unedited || hex.Pending!=5)throw new Exception("The high digit was written before the low one");
                foreach(var key in new[]{Keys.A,Keys.NumPad7,Keys.F})debugger.TestType(key);
                if(core.Dump(ramTop+2,2) is not [0x5a,0x7f] || hex.Caret!=ramTop+4)throw new Exception("Typing hex into the memory view did not edit RAM");
                // Ctrl+C or Ctrl+A is a shortcut, not a digit.
                hex.Jump(ramTop+2);debugger.TestType(Keys.Control|Keys.C);debugger.TestType(Keys.Control|Keys.A);
                if(core.Dump(ramTop+2,1)[0]!=0x5a || hex.Pending>=0)throw new Exception("A shortcut typed a digit into memory");
                // A touchpad's small steps add up to a scroll.
                hex.Jump(ramTop+0x200);int top=hex.Address;for(int i=0;i<4;i++)hex.TestWheel(-30);
                if(hex.Address!=top+3*16)throw new Exception($"Four 30-unit wheel steps moved the memory view to {hex.Address:X5}, not three rows on from {top:X5}");
                hex.Jump(0xc0000);debugger.TestType(Keys.D0);
                if(Native.e500_peek(core.Handle,0xc0000)!=romBytes[0])throw new Exception("Typing over ROM changed it");
                hex.Jump(0xfffff);
                if(hex.Address+hex.VisibleRows()*16!=0x100000 || hex.TestScrollValue!=hex.TestScrollMax)throw new Exception("Hex view cannot scroll to the end of memory");
                core.Write(ramTop,keep);
                // BIN transfer: a headerless file goes to the typed address, a SAVE M
                // file back to its own, and a dump comes out with the header it needs.
                string rawBin=Path.Combine(output,"debug-raw.bin");File.WriteAllBytes(rawBin,[1,2,3,4,5]);
                using(var transfer=new BinaryTransferDialog(()=>core,false,ramTop)) {transfer.TestSet(rawBin,(ramTop+16).ToString("X5"),false);transfer.TestRun();if(transfer.Done!=ramTop+16)throw new Exception("BIN load reported the wrong address");}
                if(core.Dump(ramTop+16,5) is not [1,2,3,4,5])throw new Exception("BIN load did not write the file");
                string headered=Path.Combine(output,"debug-savem.bin");
                using(var transfer=new BinaryTransferDialog(()=>core,true,ramTop+16)) {transfer.TestSet(headered,(ramTop+16).ToString("X5"),true,"5");transfer.TestRun();}
                byte[] saved=File.ReadAllBytes(headered);
                if(saved.Length!=21 || !BinaryTransfer.TryReadHeader(saved,out int hs,out int hl) || hs!=ramTop+16 || hl!=5 || !saved.Skip(16).SequenceEqual(new byte[]{1,2,3,4,5}))throw new Exception("BIN save did not write a SAVEM file");
                core.Write(ramTop+16,new byte[5]);
                using(var transfer=new BinaryTransferDialog(()=>core,false,0)) {transfer.TestSet(headered,"00000",true);transfer.TestRun();if(transfer.Done!=ramTop+16)throw new Exception("SAVEM header address was not used");}
                if(core.Dump(ramTop+16,5) is not [1,2,3,4,5])throw new Exception("SAVEM BIN did not return to its own address");
                using(var transfer=new BinaryTransferDialog(()=>core,false,0)) {
                    transfer.TestSet(rawBin,"C0000",false);
                    try {transfer.TestRun();throw new Exception("BIN load into ROM was accepted");}
                    catch(InvalidOperationException){}
                }
                core.Write(ramTop,keep);
            } finally {debugger.Close();}
            int capacity=core.InternalKB;form.TestPhysicalKey("RESET");
            if(core.InternalKB!=capacity || (Native.e500_status(core.Handle)&4)!=0)throw new Exception("RESET pin did not warm-reset the CPU");
            // ON+RESET hands the machine to the ROM own all-reset prompt; answering
            // N must leave it running, so the LCD has to differ from a plain reset.
            byte[] warm=core.Pixels.ToArray();form.TestAllReset();core.Refresh();
            if(core.Pixels.SequenceEqual(warm) || (Native.e500_status(core.Handle)&4)!=0)throw new Exception("ON+RESET did not reach the all-reset prompt");
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"all-reset.png"));}
            Key("N");core.Run(4096000);
            if(core.InternalKB!=capacity || (Native.e500_status(core.Handle)&4)!=0)throw new Exception("Declining the all-reset did not resume the machine");
            {
                // The autosave writes off the UI thread: the files come out as a save on
                // the spot would write them, an unchanged RAM file is not rewritten, and a
                // file that cannot be written is reported, not thrown into the tick.
                // 自動保存：裏で書いたファイルがその場で書いたものと同じ、変わらない RAM は書き直さない、書けなければ報告する。
                string folder=Path.Combine(Path.GetTempPath(),"pce500-autosave-"+Guid.NewGuid().ToString("N"));
                try {
                    var writer=new PersistentWriter();
                    string ram=Path.Combine(folder,"internal.ram"),state=Path.Combine(folder,"auto.e500state");
                    byte[] image=core.InternalImage(),snapshot=core.Snapshot();
                    if(!writer.Start([new(ram,image),new(state,snapshot,core.Hash)]))throw new Exception("The autosave did not start");
                    writer.Wait();
                    if(writer.Failure is not null)throw new Exception("The background autosave failed: "+writer.Failure.Message);
                    if(!File.ReadAllBytes(ram).AsSpan().SequenceEqual(image))throw new Exception("The background autosave wrote other RAM bytes");
                    using(var check=new Core(rom,core.InternalKB,null))check.Load(state);
                    var stamp=File.GetLastWriteTimeUtc(ram);
                    writer.WriteNow([new(ram,image)]);
                    if(File.GetLastWriteTimeUtc(ram)!=stamp || Directory.GetFiles(folder,"internal.ram.*.tmp").Length!=0)throw new Exception("An unchanged RAM file was written again");
                    // Written by something else in between, it is written again even with the same bytes.
                    File.WriteAllBytes(ram,new byte[image.Length]);
                    writer.WriteNow([new(ram,image)]);
                    if(!File.ReadAllBytes(ram).AsSpan().SequenceEqual(image))throw new Exception("A RAM file changed by something else was not written again");
                    // A folder where the file should be: the write fails, the failure is kept, and a later good one clears it.
                    string blocked=Path.Combine(folder,"blocked.ram");Directory.CreateDirectory(blocked);
                    writer.Start([new(blocked,image)]);writer.Wait();
                    if(writer.Failure is null)throw new Exception("A failed autosave was not reported");
                    writer.Start([new(Path.Combine(folder,"other.ram"),image)]);writer.Wait();
                    if(writer.Failure is not null)throw new Exception("A good autosave did not clear the failure");
                } finally {try{Directory.Delete(folder,true);}catch{}}
            }
            // Dispose without FormClosing: tests must not create or overwrite the user's automatic save.
            File.WriteAllText(Path.Combine(output,"smoke-result.txt"),"PASS: ROM boot and trailer version display, BASIC calculation/program, CE-126P LPRINT paper, symbols, full-roll view and mid-line snapshot, snapshot restore, UI shifted input and fast multiline paste, LCD ROM-font text recognition, all physical key mappings/bounds, all appearances, all six internal RAM files and card image roundtrips, card image attach/detach/snapshot rules, settings/dialog, Windows waveOut and its bounded backlog, focus-free toolbar, key input budget, modeless sub-windows, debug register/memory window with in-place hex editing, 8- and 16-byte rows with per-8-byte checksums, BIN load/save, and UI renders.\n");
        } catch(Exception ex) {File.WriteAllText(Path.Combine(output,"smoke-result.txt"),ex.ToString());Environment.ExitCode=1;}
    }
}

// Streams for the printer tests: one whose writes fail, and one whose writes never finish.
/// <summary>書き込みが必ず失敗するストリーム（テスト用）。</summary>
sealed class FailingStream : MemoryStream
{
    public override void Write(byte[] buffer,int offset,int count)=>throw new IOException("write failed");
    public override Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken token)=>Task.FromException(new IOException("write failed"));
}
/// <summary>書き込みが終わらないストリーム（テスト用。取り消しで終わる）。</summary>
sealed class HangingStream : MemoryStream
{
    public override Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken token)=>Task.Delay(Timeout.Infinite,token);
}
