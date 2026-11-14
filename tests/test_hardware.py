"""End-to-end hardware regressions using the user's locally supplied ROM.

All disk mutations use a fresh temporary directory, never the user's disk folder.

手元の ROM を使った、実機の動きに近い通しのテスト（ROM・CPU・周辺機器を一緒に動かす）。
ディスクを書き換えるテストは、毎回新しい一時フォルダを使い、利用者のフォルダには触れない。
実行：py tests/test_hardware.py
"""
import ctypes as c
import pathlib
import tempfile
import unittest
import wave
import struct
from rom_harness import Emulator,ROOT,KEYS

THREE=['#####.','...#..','..#...','.###..','....#.','....#.','####..','......']

def has_three(e):
    """LCD の右端の桁に、どれかの行で「3」が表示されているか。"""
    p=e.pixels()
    return any(all(bool(p[(y+dy)*240+234+dx])==(THREE[dy][dx]=='#') for dy in range(8) for dx in range(6)) for y in [0,8,16,24])

class HardwareTests(unittest.TestCase):
    def test_pc_u6000_384k_rom_map_and_basic(self):
        # The merged 384KB image, named model(version).ROM or, as before, PC-U6000-full.rom.
        # 結合した 384KB の PC-U6000 の ROM（「機種名(版).ROM」か、以前の名前の PC-U6000-full.rom）。
        rom_path=next((p for p in sorted((ROOT/'app'/'rom').iterdir()) if p.name.upper().startswith('PC-U6000') and p.stat().st_size==384*1024),None)
        if rom_path is None:self.skipTest('No 384KB PC-U6000 ROM is installed')
        rom=rom_path.read_bytes()
        # The dumps it was merged from, when they are kept beside it: S3B in front, then S3A.
        main,extension=ROOT/'app'/'rom'/'PC-U6000.rom',ROOT/'app'/'rom'/'S3B.ROM'
        if main.exists() and extension.exists():
            extension=extension.read_bytes()
            self.assertEqual(extension[:5],bytes.fromhex('ff 00 06 01 10'))
            self.assertEqual(int.from_bytes(extension[5:8],'little'),128*1024)
            self.assertEqual(int.from_bytes(extension[8:11],'little'),0x20000)
            self.assertEqual(rom[:128*1024],extension[16:])
            self.assertEqual(rom[128*1024:],main.read_bytes())
        e=Emulator(rom=rom_path,ram=64)
        try:
            self.assertEqual(e.lib.e500_peek(e.m,0x20000),rom[0])
            self.assertEqual(e.lib.e500_peek(e.m,0x3ffff),rom[0x1ffff])
            self.assertEqual(e.lib.e500_peek(e.m,0xc0000),rom[0x20000])
            self.assertEqual(e.lib.e500_peek(e.m,0xfffff),rom[-1])
            e.boot();e.type('1+2')
            self.assertTrue(has_three(e))
            e.screenshot('u6000-basic.png')
        finally:e.close()

    def test_workspace_cloud_disk(self):
        with tempfile.TemporaryDirectory(prefix='cloud-disk-',dir=ROOT/'tests/output') as folder:
            e=Emulator(disk=folder)
            try:
                e.boot();e.key('BASIC');e.run();e.type('10 PRINT 123')
                e.type('SAVE "X:CLOUD.BAS"')
                self.assertGreater(pathlib.Path(folder,'CLOUD.BAS').stat().st_size,0)
                e.type('NEW');e.type('LOAD "X:CLOUD.BAS"');e.key('BASIC');e.run();e.type('RUN')
                self.assertTrue(has_three(e));self.assertNotIn('ERROR:',e.trace())
            finally:e.close()

    def test_all_ram_capacities_and_cards_boot(self):
        for kind in ['ram','card']:
            for kb in [8,16,32,64,128,256]:
                with self.subTest(kind=kind,kb=kb):
                    e=Emulator(**{kind:kb})
                    try:
                        e.run(10240000);e.key('F1');e.run(10240000)
                        if kind=='card':e.key('F1');e.run(10240000)
                        e.key('BASIC');e.run();e.type('1+2')
                        self.assertTrue(has_three(e))
                    finally:e.close()

    def test_e650_internal_ram_expansion(self):
        """A 628128 SRAM swap takes the stock 64KB E650/U6000 to 128KB or 256KB."""
        rom_path=next((p for p in sorted((ROOT/'app'/'rom').iterdir()) if p.suffix.lower()=='.rom' and p.stat().st_size==384*1024),None)
        if rom_path is None:self.skipTest('No 384KB PC-E650/PC-U6000 ROM is installed')
        for kb in [64,128,256]:
            with self.subTest(kb=kb):
                e=Emulator(rom=rom_path,ram=kb)
                try:
                    e.run(10240000);e.key('F1');e.run(10240000)
                    e.key('BASIC');e.run();e.type('1+2')
                    self.assertTrue(has_three(e))
                finally:e.close()
        for kb in [8,16,32]:
            with self.subTest(kb=kb):
                with self.assertRaises(ValueError):Emulator(rom=rom_path,ram=kb)

    def test_all_reset_on_plus_reset(self):
        """Releasing RESET with ON held reaches the ROM own all-reset prompt."""
        def all_reset(e):
            row,bit=KEYS['ON/BRK']
            e.lib.e500_reset(e.m,0)
            e.lib.e500_key(e.m,row,bit,1);e.run(2048000);e.lib.e500_key(e.m,row,bit,0);e.run(1024000)
        for answer,survives in [('N',True),('Y',False)]:
            with self.subTest(answer=answer):
                e=Emulator()
                try:
                    e.boot();e.key('BASIC');e.run();e.type('10 PRINT 123')
                    e.key('BASIC');e.run();e.type('RUN')
                    self.assertTrue(has_three(e))
                    all_reset(e)
                    e.key(answer);e.run(4096000)
                    e.key('BASIC');e.run(2048000);e.type('RUN')
                    self.assertEqual(has_three(e),survives)
                finally:e.close()

    def test_key_click_timing_matches_the_real_machine(self):
        """IMG_2776.MOV of a real PC-E500: 13.5ms click, auto-repeat every 62.5ms.

        Both follow the 64Hz sub timer, so a wrong timer period is audible.
        """
        def clicks(key,count):
            e=Emulator();buf=(c.c_ubyte*256)()
            try:
                e.boot()
                row,bit=KEYS[key];e.lib.e500_key(e.m,row,bit,1)
                def tone():
                    e.lib.e500_imem(e.m,buf);return (buf[0xfd]>>4)&7
                last=tone();start=e.lib.e500_cycles(e.m);on=[];off=[]
                for _ in range(200000):
                    e.lib.e500_run(e.m,64);now=tone()
                    if now!=last:
                        (on if last==0 else off).append(e.lib.e500_cycles(e.m)-start)
                        last=now
                        if len(on)>=count and len(off)>=count:break
                return on,off
            finally:e.close()
        on,off=clicks('5',1)
        self.assertAlmostEqual((off[0]-on[0])/768.0,13.5,delta=1.5)
        on,_=clicks('RIGHT',5)
        for i in range(1,4):
            self.assertAlmostEqual((on[i+1]-on[i])/768.0,62.5,delta=3.0)

    def test_shipped_key_timing_never_drops_a_key(self):
        """The UI queues 18000 cycles down and 24000 up. Every press must register.

        The ROM only counts a key whose press and release fall in different 64Hz
        scans with one scan between, so a shorter budget silently loses presses.
        """
        for key in ['5','A','RIGHT','ENTER']:
            with self.subTest(key=key):
                e=Emulator();buf=(c.c_ubyte*256)()
                try:
                    e.boot()
                    row,bit=KEYS[key];clicks=0;last=0
                    def tone():
                        e.lib.e500_imem(e.m,buf);return (buf[0xfd]>>4)&7
                    def advance(cycles):
                        nonlocal clicks,last
                        done=0
                        while done<cycles:
                            step=min(256,cycles-done);e.lib.e500_run(e.m,step);done+=step
                            now=tone()
                            if now!=last:
                                if last==0:clicks+=1
                                last=now
                    last=tone()
                    for _ in range(20):
                        e.lib.e500_key(e.m,row,bit,1);advance(18000)
                        e.lib.e500_key(e.m,row,bit,0);advance(24000)
                    advance(200000)
                    self.assertEqual(clicks,20)
                finally:e.close()

    def test_beep_waveform(self):
        e=Emulator()
        try:
            e.boot();e.capture=[];e.type('BEEP 1');a=e.capture
            edges=sum(a[i]!=a[i-1] for i in range(1,len(a)))
            self.assertGreater(edges,200);self.assertLess(edges,10000)
            # Every 4kHz half period must be six samples wide at 48kHz. Sampling
            # XOUT once per instruction detunes whole beeps into 5/7 half periods.
            runs=[];length=1
            for i in range(1,len(a)):
                if a[i]==a[i-1]:length+=1
                else:runs.append(length);length=1
            tone=[r for r in runs if 5<=r<=7]
            self.assertGreater(len(tone),1000)
            self.assertLess(sum(1 for r in tone if r!=6),len(tone)//100)
            with wave.open(str(ROOT/'tests/output/beep.wav'),'wb') as w:
                w.setnchannels(1);w.setsampwidth(2);w.setframerate(48000)
                w.writeframes(struct.pack('<'+'h'*len(a),*a))
        finally:e.close()

    def test_angle_mode_symbols(self):
        """角度の単位の表示シンボル。画面の DE・G・RAD の3つの区画を、ROM は DEG で DE と G、
        GRAD で G と RAD、RAD で RAD だけ点ける（デスクトップの描画はこのビットの割り当てに合わせている）。"""
        e=Emulator()
        try:
            e.boot();self.assertEqual(e.symbols(5)&0x1c,0x18)
            for command,bits in [('RADIAN',0x04),('GRAD',0x0c),('DEGREE',0x18)]:
                e.type(command);self.assertEqual(e.symbols(5)&0x1c,bits,command)
        finally:e.close()
    def test_ce140f_binary_multiblock_and_ascii(self):
        with tempfile.TemporaryDirectory(prefix='pce500-disk-test-') as folder:
            e=Emulator(disk=folder)
            try:
                e.boot();e.key('BASIC');e.run();e.type('10 PRINT 123')
                for i in range(2,25):e.type(f'{i*10} REM TEST DATA {i}')
                e.type('SAVE "X:BLOCKS.BAS"');e.run(10240000)
                binary=pathlib.Path(folder,'BLOCKS.BAS').read_bytes()
                self.assertGreater(len(binary),256);self.assertEqual(binary[0],255)
                e.type('NEW');e.type('LOAD "X:BLOCKS.BAS"');e.run(10240000)
                e.key('BASIC');e.run();e.type('RUN');self.assertTrue(has_three(e))
                e.screenshot('ce-binary-roundtrip.png')
                e.key('BASIC');e.run();e.type('SAVE "X:ASCII.BAS",A');e.run(10240000)
                text=pathlib.Path(folder,'ASCII.BAS').read_bytes()
                self.assertIn(b'10 PRINT 123\r\n',text);self.assertIn(b'240 REM TEST DATA 24\r\n',text)
                # No 1Ah (EOF) at the end: a hardware CE-140F emulator cannot LOAD a file with one.
                self.assertTrue(text.endswith(b'240 REM TEST DATA 24\r\n'),text[-8:]);self.assertNotIn(b'\x1a',text)
                e.type('NEW');e.type('LOAD "X:ASCII.BAS"');e.run(10240000)
                e.key('BASIC');e.run();e.type('RUN');self.assertTrue(has_three(e))
                e.screenshot('ce-ascii-roundtrip.png')
                self.assertNotIn('ERROR:',e.trace());self.assertNotIn('timeout',e.trace())
            finally:e.close()

    def test_ce140f_ascii_load_stops_at_eof(self):
        """A text file ending with 1Ah (EOF) LOADs up to it, without Disk full; one without it as before.
        SAVE writes no 1Ah, as the hardware CE-140F emulator does.
        1Ah で終わるテキストは、そこまで読んで Disk full にならない。1Ah のないファイルも読める。SAVE は 1Ah を書かない。"""
        for label,data,saved in [('EOF',b'10 PRINT 123\r\n20 PRINT 456\r\n\x1a',b'10 PRINT 123\r\n20 PRINT 456\r\n'),
                                 ('EOF right after the last line',b'10 PRINT 123\r\n20 PRINT 456\x1a',b'10 PRINT 123\r\n20 PRINT 456\r\n'),
                                 ('padded after EOF',b'10 PRINT 123\r\n\x1a\x00\x00\xff99 REM\r\n',b'10 PRINT 123\r\n'),
                                 ('no EOF',b'10 PRINT 123\r\n',b'10 PRINT 123\r\n'),
                                 ('no EOF, no line end',b'10 PRINT 123\r\n20 PRINT 456',b'10 PRINT 123\r\n20 PRINT 456\r\n')]:
            with self.subTest(label),tempfile.TemporaryDirectory(prefix='pce500-eof-') as folder:
                pathlib.Path(folder,'A.BAS').write_bytes(data)
                e=Emulator(disk=folder)
                try:
                    e.boot();e.type('LOAD "X:A.BAS"',enter=False);e.key('ENTER');e.run(20480000)
                    # With the 1Ah sent twice the ROM showed Disk full and left the drive waiting.
                    self.assertNotIn('timeout',e.trace());self.assertNotIn('ERROR:',e.trace())
                    e.type('RUN');self.assertTrue(has_three(e))
                    e.key('BASIC');e.run();e.type('SAVE "X:B.BAS",A');e.run(10240000)
                    self.assertEqual(pathlib.Path(folder,'B.BAS').read_bytes(),saved)
                finally:e.close()

    def test_ce140f_files_pages_down_and_up(self):
        """FILES "X:" lists short names, and pages down to the end and back up without an error.
        FILES "X:" で短い名前も一覧に出て、↓で最後まで進み↑で先頭まで戻ってもエラーにならない。"""
        with tempfile.TemporaryDirectory(prefix='pce500-files-') as folder:
            for i in range(10):pathlib.Path(folder,f'F{i}.BAS').write_bytes(b'10 PRINT 1\r\n\x1a')
            e=Emulator(disk=folder)
            try:
                e.boot();e.type('FILES "X:"',enter=False);e.key('ENTER');e.run(10240000)
                # The ROM asks for each name it shows; with none listed it asks for none.
                self.assertIn('CMD 06',e.trace())
                for key in ['DOWN']*12+['UP']*12:e.key(key);e.run(4096000)
                self.assertIn('CMD 07',e.trace());self.assertNotIn('ERROR:',e.trace());self.assertNotIn('timeout',e.trace())
                e.screenshot('ce-files-back-to-top.png')
            finally:e.close()

    def test_ce140f_savem_memory_dumps(self):
        with tempfile.TemporaryDirectory(prefix='savem-dump-',dir=ROOT/'tests/output') as folder:
            e=Emulator(disk=folder)
            try:
                e.boot();e.type('SAVE M "X:MEM64.BIN",65536,131071')
                path=pathlib.Path(folder,'MEM64.BIN')
                for _ in range(8):
                    e.run(10240000)
                    if path.exists():break
                self.assertTrue(path.exists(),'64KB SAVE M did not finish')
                dump=path.read_bytes()
                self.assertEqual(len(dump),16+64*1024)
                self.assertEqual(dump[:16],bytes.fromhex('ff 00 06 01 10 00 00 01 00 00 01 ff ff ff 00 0f'))
                self.assertEqual(dump[16:],bytes([0xff])*(64*1024))
                e.type('SAVE M "X:MEM192.BIN",65536,262143')
                full_path=pathlib.Path(folder,'MEM192.BIN')
                for _ in range(24):
                    e.run(10240000)
                    if full_path.exists():break
                self.assertTrue(full_path.exists(),'192KB SAVE M did not finish')
                full=full_path.read_bytes()
                self.assertEqual(len(full),16+192*1024)
                self.assertEqual(full[:16],bytes.fromhex('ff 00 06 01 10 00 00 03 00 00 01 ff ff ff 00 0f'))
                self.assertEqual(full[16:],bytes([0xff])*(192*1024))
                self.assertNotIn('ERROR:',e.trace())
            finally:e.close()

    def test_ce140f_files_and_sequential_io(self):
        with tempfile.TemporaryDirectory(prefix='pce500-disk-test-') as folder:
            e=Emulator(disk=folder)
            try:
                e.boot()
                for line in ['OPEN "X:DATA.DAT" FOR OUTPUT AS #2','PRINT #2,"HELLO"','PRINT #2,"WORLD"','CLOSE #2']:
                    e.type(line);e.run(3072000)
                self.assertEqual(pathlib.Path(folder,'DATA.DAT').read_bytes(),b'HELLO\r\nWORLD\r\n')
                for line in ['OPEN "X:DATA.DAT" FOR INPUT AS #2','INPUT #2,A$','INPUT #2,B$','CLOSE #2','PRINT A$+B$']:
                    e.type(line);e.run(3072000)
                e.screenshot('ce-sequential-io.png')
                e.type('COPY "X:DATA.DAT" TO "X:COPY.DAT"')
                e.type('NAME "X:COPY.DAT" AS "X:NEW.DAT"')
                self.assertEqual(pathlib.Path(folder,'NEW.DAT').read_bytes(),b'HELLO\r\nWORLD\r\n')
                self.assertFalse(pathlib.Path(folder,'COPY.DAT').exists())
                e.type('SET "X:NEW.DAT","P"')
                self.assertTrue(c.windll.kernel32.GetFileAttributesW(str(pathlib.Path(folder,'NEW.DAT')))&1)
                e.type('SET "X:NEW.DAT"," "')
                # Verify the actual ROM attribute-clear variant before deleting the test file.
                self.assertFalse(c.windll.kernel32.GetFileAttributesW(str(pathlib.Path(folder,'NEW.DAT')))&1)
                e.type('KILL "X:NEW.DAT"');self.assertFalse(pathlib.Path(folder,'NEW.DAT').exists())
                self.assertNotIn('ERROR:',e.trace())
            finally:
                e.close()
                for p in pathlib.Path(folder).iterdir():c.windll.kernel32.SetFileAttributesW(str(p),128)

    def test_ce126p_lprint_llist_and_disconnect(self):
        e=Emulator()
        try:
            e.boot()
            e.type('LPRINT "HELLO 0O"');e.run(4096000)
            self.assertEqual(e.printed(),b'HELLO 0O\n')
            # 24 columns, then the ROM wraps; katakana passes through unchanged.
            e.type('LPRINT "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123"');e.run(4096000)
            self.assertEqual(e.printed(),b'ABCDEFGHIJKLMNOPQRSTUVWX\nYZ0123\n')
            e.type('LPRINT CHR$(177)');e.run(4096000)
            self.assertEqual(e.printed(),b'\xb1\n')
            # The ROM recodes these symbols for the printer; they come back as sent.
            e.type('LPRINT CHR$(232);CHR$(235);CHR$(241);CHR$(244)');e.run(4096000)
            self.assertEqual(e.printed(),b'\xe8\xeb\xf1\xf4\n')
            # BEEP moves XOUT too, and must not select the printer.
            e.type('BEEP 3');e.run(4096000);self.assertEqual(e.printed(),b'')
            e.key('BASIC');e.run();e.type('10 PRINT 123');e.key('BASIC');e.run()
            e.type('LLIST');e.run(4096000)
            self.assertIn(b'PRINT 123',e.printed())
            e.lib.e500_printer_connect(e.m,0)
            e.type('LPRINT 1');e.run(4096000)
            self.assertEqual(e.printed(),b'')
            e.screenshot('ce126p-disconnected.png')
        finally:e.close()

    def test_ce126p_shares_the_bus_with_ce140f(self):
        with tempfile.TemporaryDirectory(prefix='pce500-disk-test-') as folder:
            e=Emulator(disk=folder)
            try:
                e.boot();e.key('BASIC');e.run();e.type('10 PRINT 123');e.key('BASIC');e.run()
                # The printer's codes pass the CE-140F by without a trace (the
                # ROM's own 41h at power-on is already there).
                before=e.trace()
                e.type('LPRINT "BEFORE"');e.run(4096000)
                self.assertEqual(e.trace(),before)
                e.type('SAVE "X:SHARE.BAS"');e.run(4096000)
                e.type('LPRINT "AFTER"');e.run(4096000)
                e.forget_program();e.type('RUN');self.assertFalse(has_three(e))
                e.type('CLS');e.type('LOAD "X:SHARE.BAS"');e.run(4096000)
                e.type('CLS');e.type('RUN');e.screenshot('ce126p-with-ce140f.png');self.assertTrue(has_three(e))
                self.assertEqual(e.printed(),b'BEFORE\nAFTER\n')
                self.assertGreater(pathlib.Path(folder,'SHARE.BAS').stat().st_size,0)
                self.assertNotIn('ERROR:',e.trace());self.assertNotIn('timeout',e.trace())
            finally:e.close()

    def test_ce126p_snapshot_mid_line_resumes_the_line(self):
        # The printer's place on the bus goes into the snapshot, so a save in the
        # middle of a line (paused there, say) is always possible, and restoring
        # it prints the rest of the line instead of ending in a printer error.
        self.snapshot_mid_line(None)

    def test_ce126p_snapshot_mid_line_with_ce140f(self):
        # The CE-140F handshakes the printer's codes too; it must not refuse a
        # save while one goes by, nor lose its own place in it.
        with tempfile.TemporaryDirectory(prefix='pce500-disk-test-') as folder:
            self.snapshot_mid_line(folder)

    def snapshot_mid_line(self,disk):
        e=Emulator(disk=disk)
        try:
            for name in ('e500_save','e500_load'):
                getattr(e.lib,name).argtypes=[c.c_void_p,c.c_void_p,c.c_int];getattr(e.lib,name).restype=c.c_int
            e.lib.e500_printer_status.argtypes=[c.c_void_p];e.lib.e500_printer_status.restype=c.c_int
            e.lib.e500_state_size.restype=c.c_int
            state=(c.c_ubyte*e.lib.e500_state_size())();midline=(c.c_ubyte*len(state))()
            # Two lines, so that the second one's device code comes while sampling.
            line=b'ABCDEFGHIJKLMNOPQRSTUVWX\nYZ0123456789\n'
            e.boot();e.type('LPRINT "'+line.replace(b'\n',b'').decode()+'"',enter=False);r,b=KEYS['ENTER']
            e.lib.e500_key(e.m,r,b,1);e.run(150000);e.lib.e500_key(e.m,r,b,0)
            # Status bit 1 is the printer busy on the bus: a code or a line.
            # Fine steps, so that some land on the bits of the device codes too.
            busy=saved=0
            for _ in range(32000):
                e.run(128)
                if e.lib.e500_printer_status(e.m)&2:
                    busy+=1;saved+=e.lib.e500_save(e.m,state,len(state))
                    if busy==96:c.memmove(midline,state,len(state))
            self.assertGreater(busy,96);self.assertEqual(saved,busy)
            self.assertEqual(e.printed(),line)
            self.assertEqual(e.lib.e500_load(e.m,midline,len(midline)),1)
            e.run(4096000)
            # Against a printer that had forgotten its place, the ROM would end the
            # line in a printer error with nothing printed.
            self.assertEqual(e.printed(),line)
        finally:e.close()

    def test_beep_does_not_hold_off_saves(self):
        # Every high half of a tone looks like the start of a device code to the
        # CE-140F; with a disk on the bus, saving must stay possible.
        with tempfile.TemporaryDirectory(prefix='pce500-disk-test-') as folder:
            e=Emulator(disk=folder)
            try:
                e.lib.e500_can_save.argtypes=[c.c_void_p];e.lib.e500_can_save.restype=c.c_int
                e.boot();e.type('BEEP 5',enter=False);r,b=KEYS['ENTER']
                e.lib.e500_key(e.m,r,b,1);e.run(150000);e.lib.e500_key(e.m,r,b,0)
                e.capture=[];refused=0
                for _ in range(2000):
                    e.run(512);refused+=e.lib.e500_can_save(e.m)==0
                self.assertGreater(len(set(e.capture)),1)
                self.assertEqual(refused,0)
            finally:e.close()

    def test_ce126p_does_not_stand_in_for_a_missing_ce140f(self):
        # The printer handshakes the bits of every device code, the CE-140F's 41h
        # included. With no disk the ROM must still fail X: access the same way,
        # at the same moment, as with nothing on the bus.
        results=[]
        for connected in (0,1):
            e=Emulator()
            try:
                e.lib.e500_printer_connect(e.m,connected)
                e.boot();e.key('BASIC');e.run();e.type('10 PRINT 1');e.key('BASIC');e.run()
                e.type('SAVE "X:A.BAS"',enter=False);r,b=KEYS['ENTER']
                e.lib.e500_key(e.m,r,b,1);e.run(150000);e.lib.e500_key(e.m,r,b,0)
                start=e.lib.e500_cycles(e.m);last=e.pixels();changed=0
                for _ in range(100):
                    e.run(51200);p=e.pixels()
                    if p!=last:changed=e.lib.e500_cycles(e.m)-start;last=p
                e.screenshot(f'ce126p-{connected}-no-ce140f.png')
                results.append((last,changed,e.printed()))
            finally:e.close()
        self.assertGreater(results[0][1],0)
        self.assertEqual(results[0][0],results[1][0])
        self.assertLess(abs(results[0][1]-results[1][1]),51200*2)
        self.assertEqual(results[1][2],b'')

    def test_ce140f_does_not_stand_in_for_a_missing_ce126p(self):
        # The other way round: the disk stays silent through a code whose first
        # bit is 0, the printer's 20h, so with the printer unplugged LPRINT fails
        # the same way, at the same moment, whether a disk is on the bus or not.
        # Answering it, the ROM went on to wait out the data phase, and PF1 at
        # power-on took long enough to swallow the keys typed after it.
        # プリンタを外した LPRINT は、ディスクの有無にかかわらず同じ時点で同じエラーになる。
        results=[]
        with tempfile.TemporaryDirectory(prefix='pce500-disk-test-') as folder:
            for disk in (None,folder):
                e=Emulator(disk=disk)
                try:
                    e.lib.e500_printer_connect(e.m,0)
                    e.boot();e.type('LPRINT "A"',enter=False);r,b=KEYS['ENTER']
                    # Timed from the press: with nothing on the bus the error comes
                    # while ENTER is still down.
                    start=e.lib.e500_cycles(e.m);last=e.pixels();changed=0
                    e.lib.e500_key(e.m,r,b,1)
                    for i in range(1250):
                        if i==37:e.lib.e500_key(e.m,r,b,0)
                        e.run(4096);p=e.pixels()
                        if p!=last:changed=e.lib.e500_cycles(e.m)-start;last=p
                    e.screenshot(f'ce126p-missing-{"with" if disk else "no"}-ce140f.png')
                    results.append((last,changed))
                finally:e.close()
        self.assertGreater(results[0][1],0)
        self.assertEqual(results[0][0],results[1][0])
        self.assertLess(abs(results[0][1]-results[1][1]),4096*2)

if __name__=='__main__':unittest.main(verbosity=2)
