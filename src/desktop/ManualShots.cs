// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing.Imaging;
namespace PokeCom;

// `PC-E500.exe --manual-shots <rom> <folder>` renders the pictures the HTML
// manual (app/manual) embeds: the main window, the LCD, every sub-window and
// dialog, each in a known state. Runs in testing mode so the user's own state
// files are never read or written.
/// <summary>HTML のマニュアル（app/manual）に載せる画像を、決まった状態で撮る。</summary>
internal static class ManualShots
{
    /// <summary>画像を撮る。</summary>
    /// <param name="rom">ROM ファイル</param>
    /// <param name="output">画像を書くフォルダ</param>
    /// <param name="language">表示言語（ja か en）</param>
    internal static void Run(string rom,string output,string language="ja")
    {
        L.Set(language);
        Directory.CreateDirectory(output);
        string scratch=Path.Combine(Path.GetTempPath(),"pce500-manual-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try {
            using var form=new MainForm(rom,testing:true);
            form.CreateControl();form.Show();Application.DoEvents();
            var core=form.TestCore;
            void Key(string key){int code=Keyboard.Codes[key];core.Key(code,true);core.Run(100000);core.Key(code,false);core.Run(100000);}
            void Settle(){form.TestClearNotice();form.TestRefresh();Application.DoEvents();}
            void Shot(Control window,string name) {
                Application.DoEvents();
                using var bitmap=new Bitmap(window.Width,window.Height);
                window.DrawToBitmap(bitmap,new Rectangle(0,0,window.Width,window.Height));
                // A see-through colour (the case-only view's corners) is see-through in the picture too.
                if(window is Form{TransparencyKey.IsEmpty:false} keyed)bitmap.MakeTransparent(keyed.TransparencyKey);
                bitmap.Save(Path.Combine(output,name+".png"),ImageFormat.Png);
            }
            void Crop(Form window,Rectangle area,string name) {
                Application.DoEvents();
                using var bitmap=new Bitmap(window.Width,window.Height);
                window.DrawToBitmap(bitmap,new Rectangle(0,0,window.Width,window.Height));
                using var part=bitmap.Clone(area,bitmap.PixelFormat);
                part.Save(Path.Combine(output,name+".png"),ImageFormat.Png);
            }
            // DrawToBitmap paints the whole window, frame included, from its screen
            // origin, so a control rectangle maps by subtracting the window position.
            Rectangle Client(Rectangle inControl,Control c){var p=c.PointToScreen(inControl.Location);return new Rectangle(p.X-form.Left,p.Y-form.Top,inControl.Width,inControl.Height);}

            form.TestAppearance("PC-E500");Settle();
            // The surface lays itself out on its first paint; draw once so the LCD
            // rectangle is measured against the final scale.
            using(var warm=new Bitmap(form.Width,form.Height))form.DrawToBitmap(warm,new Rectangle(0,0,form.Width,form.Height));
            Settle();
            // Fresh machine: the NEW CARD prompt, then PF1 and BASIC as the manual tells it.
            core.Run(2048000);Settle();
            Crop(form,Client(form.TestSurface.LcdArea(),form.TestSurface),"lcd-new-card");
            Key("F1");core.Run(2048000);Settle();
            Crop(form,Client(form.TestSurface.LcdArea(),form.TestSurface),"lcd-initialized");
            Key("BASIC");core.Run(1024000);Settle();
            form.TestText("1+2");Settle();
            Crop(form,Client(form.TestSurface.LcdArea(),form.TestSurface),"lcd-run");
            Shot(form,"main-e500");
            // The toolbar alone: title bar plus every row of buttons under it (it wraps to two).
            var bar=form.TestToolbarPanel;
            Crop(form,new Rectangle(0,0,form.Width,Client(bar.ClientRectangle,bar).Bottom+4),"toolbar");
            Key("BASIC");core.Run(1024000);Settle();
            form.TestText("10 PRINT 123");Settle();
            Crop(form,Client(form.TestSurface.LcdArea(),form.TestSurface),"lcd-pro");
            Key("BASIC");core.Run(1024000);form.TestText("RUN");Settle();
            Crop(form,Client(form.TestSurface.LcdArea(),form.TestSurface),"lcd-program");
            form.TestAppearance("PC-E650");Settle();Shot(form,"main-e650");
            form.TestAppearance("PC-E500");form.TestDotLines(true);Settle();
            Crop(form,Client(form.TestSurface.LcdArea(),form.TestSurface),"lcd-dot-lines");
            form.TestDotLines(false);Settle();
            // The right-click menu, opened over the case.
            var menu=form.TestMenu;menu.Show(form.TestSurface,new Point(40,40));Shot(menu,"context-menu");menu.Close();
            // The display window, and the window with only the case, then only the screen, left.
            using(var display=new DisplayDialog(false,PocketSurface.DefaultContrast,WindowView.Normal,true)){display.Show(form);Shot(display,"display");display.Close();}
            form.TestSetView(WindowView.Body);Settle();Shot(form,"window-body");
            form.TestSetView(WindowView.Screen);Settle();Shot(form,"window-screen");
            form.TestSetView(WindowView.Normal);Settle();
            // The new-version notice, for a release one past this one.
            var next=new Version(UpdateCheck.Current.Major,UpdateCheck.Current.Minor,UpdateCheck.Current.Build+1);
            var sample=new Release(next,"v"+next,"PC-E500 Emulator "+next,UpdateCheck.Releases,L.T("## 変更点\r\n- （リリースの本文がここに表示されます）","## Changes\r\n- (the release notes show here)"));
            using(var update=new UpdateDialog(sample)){update.StartPosition=FormStartPosition.CenterScreen;update.Show(form);Shot(update,"update");update.Close();}

            // Settings with a card in the slot, and the create-card prompt.
            // Shown with relative app/ram and app/drive-x paths rather than this
            // machine's full ones; a sample card is created there only for the shot.
            Directory.SetCurrentDirectory(Path.GetDirectoryName(form.TestRoot)!);
            string cardFolder=Path.Combine(form.TestRoot,"ram");Directory.CreateDirectory(cardFolder);
            string card=Path.Combine("app","ram","card-64KB.ram");bool sampleCard=!File.Exists(card);
            if(sampleCard)Core.CreateCardImage(card,64);
            try {
                var settings=new HardwareSettings(32,card,true,false,Path.Combine("app","drive-x"),false,50,false,"PC-E500",L.Code);
                using var dialog=new HardwareDialog(settings,core.RomKB,cardFolder);dialog.Show(form);Shot(dialog,"settings");dialog.Close();
            } finally {if(sampleCard)File.Delete(card);}
            using(var ask=new CardSizeDialog()){ask.StartPosition=FormStartPosition.CenterScreen;ask.Show(form);Shot(ask,"card-size");ask.Close();}

            using(var transfer=new ScreenTextDialog(core.ScreenText,_=>"")){transfer.Show(form);Shot(transfer,"text-transfer");transfer.Close();}
            // A real FILES exchange in the trace, against a scratch folder, shown as app\drive-x.
            string diskFolder=Path.Combine(scratch,"drive-x");Directory.CreateDirectory(diskFolder);
            File.WriteAllBytes(Path.Combine(diskFolder,"TEST.BAS"),[0xff,0x10,0x0d,0x00,0x0a,0x00,0x03,0x91,0x20,0x31,0x32,0x33,0x0d,0xff]);
            core.MountDisk(diskFolder,false);form.TestText("FILES \"X:\"");for(int i=0;i<4;i++)core.Run(10240000);Settle();
            using(var disk=new DiskTraceDialog(()=>core,()=>Path.Combine("app","drive-x"),a=>a())){disk.Show(form);Shot(disk,"disk-trace");disk.Close();}
            core.MountDisk(null,false);Key("ON/BRK");core.Run(1024000);
            // The CE-126P's paper: a line, the program listed, a number.
            form.TestText("LPRINT \"PC-E500 + CE-126P\"");form.TestText("LLIST");form.TestText("LPRINT 1/3");Settle();
            var printer=(PrinterDialog)form.TestWindow("printer");printer.Render();Shot(printer,"printer");printer.Close();

            var debugger=form.TestDebugger();
            Application.DoEvents();debugger.Render();
            debugger.Memory.Jump(0xc0000-core.InternalKB*1024+0x10);debugger.Render();
            Shot(debugger,"debugger");
            // Region colours at two boundaries: RAM into ROM, and the empty card
            // slot into the internal RAM mirror.
            void Regions(int at,string name) {
                debugger.Memory.Jump(at);debugger.Render();Application.DoEvents();
                var origin=debugger.Memory.PointToScreen(Point.Empty);
                Crop(debugger,new Rectangle(origin.X-debugger.Left,origin.Y-debugger.Top,debugger.Memory.Width,debugger.Memory.LineHeight*8+4),name);
            }
            Regions(0xbffc0,"debugger-ram-rom");Regions(0x7ffc0,"debugger-slot-mirror");
            // Sample files sit in a folder the reader can picture, only for the shot.
            string samples=Path.GetRelativePath(Directory.GetCurrentDirectory(),Path.Combine(form.TestRoot,"drive-x"));
            string savem=Path.Combine(samples,"PROGRAM.BIN"),dump=Path.Combine(samples,"MEMORY.BIN");
            bool sampleBin=!File.Exists(savem);
            if(sampleBin)File.WriteAllBytes(savem,BinaryTransfer.Save(core,0xc0000-core.InternalKB*1024,0x100,true));
            try {
                using(var load=new BinaryTransferDialog(()=>core,false,0xc0000-core.InternalKB*1024)) {
                    load.StartPosition=FormStartPosition.CenterScreen;load.Show(form);load.TestSet(savem,(0xc0000-core.InternalKB*1024).ToString("X5"),true);Shot(load,"bin-load");load.Close();
                }
                using(var save=new BinaryTransferDialog(()=>core,true,0xc0000-core.InternalKB*1024)) {
                    save.StartPosition=FormStartPosition.CenterScreen;save.Show(form);save.TestSet(dump,(0xc0000-core.InternalKB*1024).ToString("X5"),true,"100");Shot(save,"bin-save");save.Close();
                }
            } finally {if(sampleBin)File.Delete(savem);}
            debugger.Close();
            // The machine-code area window, with a size set so the map shows the change.
            using(var ml=new MachineCodeDialog(RamLayout.Internal(core),0,0x6000)){ml.StartPosition=FormStartPosition.CenterScreen;ml.Show(form);ml.TestSet(0x1000);Shot(ml,"machine-code");ml.Close();}

            // The controller window: an Xbox pad drawn as if plugged in, with A held,
            // so the picture, the pressed line and the table's row all light.
            var pad=form.TestShowPad();
            pad.TestShow(new PadDevice("xinput:0",L.T("Xbox コントローラー","Xbox controller"),PadKind.XInput,0));pad.TestPress(new PadAxes(),"A");
            Shot(pad,"controller");pad.Close();

            form.TestAllReset();Settle();
            Crop(form,Client(form.TestSurface.LcdArea(),form.TestSurface),"lcd-all-reset");
            Key("N");core.Run(4096000);Settle();
            File.WriteAllText(Path.Combine(output,"manual-shots.txt"),"OK\n");
        } catch(Exception ex) {File.WriteAllText(Path.Combine(output,"manual-shots.txt"),ex.ToString());Environment.ExitCode=1;}
        finally {try{Directory.Delete(scratch,true);}catch{}}
    }
}
