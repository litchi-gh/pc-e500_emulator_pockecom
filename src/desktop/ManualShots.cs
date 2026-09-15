// SPDX-License-Identifier: GPL-2.0-or-later
using System.Drawing.Imaging;
namespace PokeCom;

// `PC-E500.exe --manual-shots <rom> <folder>` renders the pictures the HTML
// manual (app/manual) embeds: the main window, the LCD, every sub-window and
// dialog, each in a known state. Runs in testing mode so the user's own state
// files are never read or written.
internal static class ManualShots
{
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
            void Settle(){form.TestRefresh();Application.DoEvents();}
            void Shot(Form window,string name) {
                Application.DoEvents();
                using var bitmap=new Bitmap(window.Width,window.Height);
                window.DrawToBitmap(bitmap,new Rectangle(0,0,window.Width,window.Height));
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
            // The toolbar row alone: title bar plus the buttons under it.
            int chrome=form.Height-form.ClientSize.Height;
            Crop(form,new Rectangle(0,0,form.Width,chrome+52),"toolbar");
            Key("BASIC");core.Run(1024000);Settle();
            form.TestText("10 PRINT 123");Settle();
            Crop(form,Client(form.TestSurface.LcdArea(),form.TestSurface),"lcd-pro");
            Key("BASIC");core.Run(1024000);form.TestText("RUN");Settle();
            Crop(form,Client(form.TestSurface.LcdArea(),form.TestSurface),"lcd-program");
            form.TestAppearance("PC-E650");Settle();Shot(form,"main-e650");
            form.TestAppearance("PC-E500");form.TestDotLines(true);Settle();
            Crop(form,Client(form.TestSurface.LcdArea(),form.TestSurface),"lcd-dot-lines");
            form.TestDotLines(false);Settle();

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
            using(var sums=new ChecksumDialog(()=>core,0xc0000-core.InternalKB*1024)) {
                sums.StartPosition=FormStartPosition.CenterScreen;sums.Show(form);sums.Compute();Shot(sums,"checksum");sums.Close();
            }
            debugger.Close();

            form.TestAllReset();Settle();
            Crop(form,Client(form.TestSurface.LcdArea(),form.TestSurface),"lcd-all-reset");
            Key("N");core.Run(4096000);Settle();
            File.WriteAllText(Path.Combine(output,"manual-shots.txt"),"OK\n");
        } catch(Exception ex) {File.WriteAllText(Path.Combine(output,"manual-shots.txt"),ex.ToString());Environment.ExitCode=1;}
        finally {try{Directory.Delete(scratch,true);}catch{}}
    }
}
