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
                    if(form.TestRoot!=dataRoot)throw new Exception($"Data in {form.TestRoot}, not {dataRoot} (installed: {AppFolders.Installed})");
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
                if(!json.Contains("\"Version\":3",StringComparison.Ordinal))throw new Exception("State file version is not 3: "+json[..Math.Min(40,json.Length)]);
                string newer=Path.Combine(output,"newer.e500state");
                File.WriteAllText(newer,json.Replace("\"Version\":3","\"Version\":4",StringComparison.Ordinal));
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
            const int TickCycles=8192,TicksPerRepeat=4;   // 8 ms ticks, 31 Hz repeat
            int worst=0,typed=0;
            void HoldFor(int ms,Action repeat,Action release) {
                int ticks=ms/8;
                for(int t=0;t<ticks+40;t++) {
                    if(t<ticks && t%TicksPerRepeat==0)repeat();
                    if(t==ticks)release();
                    form.TestRunCycles(TickCycles);
                    worst=Math.Max(worst,form.TestBacklogCycles/1024);
                    if(t==ticks)typed=Joined().Length;
                }
            }
            string Joined()=>core.ScreenText().Replace(Environment.NewLine,"").TrimEnd('_');
            Key("ON/BRK");core.Run(1024000);
            HoldFor(2000,()=>form.TestRepeat(MainForm.TestKey,'A'),()=>form.TestRelease(MainForm.TestKey));
            int letters=Joined().Length-Joined().TrimEnd('A').Length;
            // Eighteen a second is all the machine can take; the point is that the key
            // repeats at all, and that releasing it stops the typing then and there.
            if(letters<10)throw new Exception($"A held for 2 s only typed {letters} letters; a held key must repeat");
            if(Joined().Length>typed+1)throw new Exception($"Typing carried on after the key came up: {typed} characters at release, {Joined().Length} after");
            Key("ON/BRK");core.Run(1024000);
            Type2("ABCDEFGH");
            HoldFor(2000,()=>form.TestRepeatKey(Keys.Left),()=>form.TestRelease(Keys.Left));
            Type2("Z");
            // The cell the cursor sits on reads back as neither letter, so check the
            // two ends: Z landed where A was, and CDEFGH never moved.
            string tail=Joined().Length>=8?Joined()[^8..]:Joined();
            if(tail.Length!=8 || tail[0]!='Z' || tail[2..]!="CDEFGH")
                throw new Exception($"LEFT held for 2 s did not repeat to the start of the line: [{Joined()}]");
            // The allowance is one keystroke in the machine's hand plus one queued
            // behind it: 24000+56000 cycles, or 78 ms. What matters is that it is a
            // constant - the old queue passed 1400 ms by the end of the same hold.
            if(worst>90)throw new Exception($"Held keys queued {worst} ms of backlog; the cap is one keystroke behind one in hand (78 ms)");
            Key("ON/BRK");core.Run(1024000);
            // The case and keys cost 3.9 ms to draw and the LCD repaints 62 times a
            // second, so a frame that changes nothing but the dots must not redraw
            // them. WM_PAINT outranks the WM_TIMER that runs the machine, and a
            // repaint straddling a tick makes that tick late and its audio arrive in
            // a burst that the output buffer then has to throw away.
            var panel=form.TestSurface;
            using(var scratch=new Bitmap(Math.Max(1,panel.Width),Math.Max(1,panel.Height)))
            using(var paint=Graphics.FromImage(scratch)) {
                void Repaint(){paint.ResetTransform();panel.TestPaint(paint);}
                Repaint();int builds=panel.ChromeBuilds;
                for(int i=0;i<10;i++)Repaint();
                if(panel.ChromeBuilds!=builds)throw new Exception($"Ten unchanged frames redrew the case {panel.ChromeBuilds-builds} times");
                panel.SetPressed("A",true);Repaint();
                if(panel.ChromeBuilds!=builds+1)throw new Exception("Pressing a key did not redraw the case");
                panel.SetPressed("A",false);Repaint();
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
            string config=Path.Combine(output,"settings-test.json");settings.Save(config);
            if(HardwareSettings.Load(config)!=settings)throw new Exception("Hardware settings roundtrip failed");
            // A settings file from before the contrast setting reads with the default contrast.
            File.WriteAllText(config,File.ReadAllText(config).Replace("\"LcdContrast\": 50,","").Replace(",\n  \"LcdContrast\": 50","").Replace(",\r\n  \"LcdContrast\": 50",""));
            if(File.ReadAllText(config).Contains("LcdContrast",StringComparison.Ordinal) || HardwareSettings.Load(config).LcdContrast!=PocketSurface.DefaultContrast)throw new Exception("Settings without a contrast do not read the default");
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
            using(var pipeline=new AudioOutput{Volume=0}) {
                if(pipeline.Error is not null)throw new Exception(pipeline.Error);
                const int Frame=740;   // 15.6 ms at 48 kHz
                var handoff=new System.Diagnostics.Stopwatch();
                double slowest=0;long sent=0,until=Environment.TickCount64+1000;
                while(Environment.TickCount64<until) {
                    handoff.Restart();pipeline.Submit(tone,Frame);slowest=Math.Max(slowest,handoff.Elapsed.TotalMilliseconds);
                    sent+=Frame;Thread.Sleep(15);
                }
                if(slowest>25)throw new Exception($"Handing audio over blocked the caller for {slowest:F0} ms; the device must only be touched from the audio thread");
                Thread.Sleep(100);
                if(pipeline.Delivered*2<sent)throw new Exception($"Only {pipeline.Delivered} of {sent} samples reached the device; it is starving and every write is restarting the stream");
                if(pipeline.Restarts>2)throw new Exception($"The device ran dry {pipeline.Restarts} times in a second of steady frames; the cushion is too shallow");
                if(pipeline.Error is not null)throw new Exception(pipeline.Error);
                var deadline=Environment.TickCount64+2000;
                while(pipeline.PendingCount>0 && Environment.TickCount64<deadline){Application.DoEvents();Thread.Sleep(10);}
                if(pipeline.PendingCount!=0)throw new Exception("Windows audio buffers did not complete");
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
            if(keyCycles>60000)throw new Exception($"A key costs {keyCycles} cycles ({keyCycles/1024}ms); typing would back up");
            if(form.TestKeyCycles("BASIC")>keyCycles+204800)throw new Exception("Mode key settling grew past 200ms");
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
            var attach=printer.Controls.OfType<FlowLayoutPanel>().SelectMany(p=>p.Controls.OfType<CheckBox>()).Single();
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
            using(var blind=new PrinterDialog(new PrinterPaper(),()=>[],()=>true,_=>{})) {
                blind.Render();
                if(blind.TestNote==printer.TestNote)throw new Exception("CE-126P window did not tell that the ROM font is missing");
            }
            // Switched to a ROM whose font is found, the note follows with nothing printed.
            byte[] romFont=[];
            using(var switching=new PrinterDialog(new PrinterPaper(),()=>romFont,()=>true,_=>{})) {
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
                foreach(var key in new[]{Keys.D5,Keys.A,Keys.NumPad7,Keys.F})debugger.TestType(key);
                if(core.Dump(ramTop+2,2) is not [0x5a,0x7f] || hex.Caret!=ramTop+4)throw new Exception("Typing hex into the memory view did not edit RAM");
                hex.Jump(0xc0000);debugger.TestType(Keys.D0);
                if(Native.e500_peek(core.Handle,0xc0000)!=romBytes[0])throw new Exception("Typing over ROM changed it");
                hex.Jump(0xfffff);
                if(hex.Address+hex.VisibleRows()*16!=0x100000 || hex.TestScrollValue!=hex.TestScrollMax)throw new Exception("Hex view cannot scroll to the end of memory");
                // Checksums: the sums of a known range, both ends inclusive, and a
                // refused range that leaves the dialog with a message rather than a sum.
                core.Write(ramTop,[0xff,0xff,0x01,0x10]);
                var sums=Checksum.Of(core,ramTop,ramTop+3);
                if(sums.Length!=4 || sums.Sum8!=0x0f || sums.Sum16!=0x020f || sums.Sum32!=0x020f || sums.Xor8!=0x11)throw new Exception("Checksum arithmetic is wrong");
                if(Checksum.Of(new byte[0x10000].Select(_=>(byte)0xff).ToArray()).Sum32!=0xff0000L)throw new Exception("32-bit checksum overflowed");
                using(var dialog=new ChecksumDialog(()=>core,ramTop)) {
                    dialog.TestSet(ramTop.ToString("X5"),(ramTop+3).ToString("X5"));dialog.Compute();
                    if(dialog.Last!=sums || !dialog.TestResult.Contains("020F"))throw new Exception("Checksum dialog did not show the sums");
                    dialog.TestSet("C0000","BFFFF");dialog.Compute();
                    if(dialog.Last is not null)throw new Exception("Checksum dialog accepted an end below the start");
                    dialog.TestSet("zz","0");dialog.Compute();
                    if(dialog.Last is not null)throw new Exception("Checksum dialog accepted a non-hex address");
                }
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
            // Dispose without FormClosing: tests must not create or overwrite the user's automatic save.
            File.WriteAllText(Path.Combine(output,"smoke-result.txt"),"PASS: ROM boot and trailer version display, BASIC calculation/program, CE-126P LPRINT paper, symbols, full-roll view and mid-line snapshot, snapshot restore, UI shifted input and fast multiline paste, LCD ROM-font text recognition, all physical key mappings/bounds, all appearances, all six internal RAM files and card image roundtrips, card image attach/detach/snapshot rules, settings/dialog, Windows waveOut and its bounded backlog, focus-free toolbar, key input budget, modeless sub-windows, debug register/memory window with in-place hex editing, range checksums and BIN load/save, and UI renders.\n");
        } catch(Exception ex) {File.WriteAllText(Path.Combine(output,"smoke-result.txt"),ex.ToString());Environment.ExitCode=1;}
    }
}
