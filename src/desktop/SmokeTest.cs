// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

internal static class SmokeTest
{
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
            using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(output,"desktop-dot-lines.png"));}
            form.TestDotLines(false);Application.DoEvents();
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
            using(var dialog=new HardwareDialog(settings,core.RomKB,Path.Combine(output,"memory-roundtrip"))) {
                dialog.Show(form);Application.DoEvents();
                if(dialog.Value!=settings)throw new Exception("Hardware dialog values mismatch");
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
            string[] keys=["hardware","disk","text","debug"];
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
            File.WriteAllText(Path.Combine(output,"smoke-result.txt"),"PASS: ROM boot and trailer version display, BASIC calculation/program, snapshot restore, UI shifted input and fast multiline paste, LCD ROM-font text recognition, all physical key mappings/bounds, all appearances, all six internal RAM files and card image roundtrips, card image attach/detach/snapshot rules, settings/dialog, Windows waveOut and its bounded backlog, focus-free toolbar, key input budget, modeless sub-windows, debug register/memory window with in-place hex editing and BIN load/save, and UI renders.\n");
        } catch(Exception ex) {File.WriteAllText(Path.Combine(output,"smoke-result.txt"),ex.ToString());Environment.ExitCode=1;}
    }
}
