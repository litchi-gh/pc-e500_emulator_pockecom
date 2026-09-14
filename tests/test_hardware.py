"""End-to-end hardware regressions using the user's locally supplied ROM.

All disk mutations use a fresh temporary directory, never the user's disk folder.
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
    p=e.pixels()
    return any(all(bool(p[(y+dy)*240+234+dx])==(THREE[dy][dx]=='#') for dy in range(8) for dx in range(6)) for y in [0,8,16,24])

class HardwareTests(unittest.TestCase):
    def test_pc_u6000_384k_rom_map_and_basic(self):
        rom_path=ROOT/'app'/'rom'/'PC-U6000-full.rom'
        if not rom_path.exists():self.skipTest('PC-U6000-full.rom is not installed')
        rom=rom_path.read_bytes();self.assertEqual(len(rom),384*1024)
        main=(ROOT/'app'/'rom'/'PC-U6000.rom').read_bytes()
        extension=(ROOT/'app'/'rom'/'S3B.ROM').read_bytes()
        self.assertEqual(extension[:5],bytes.fromhex('ff 00 06 01 10'))
        self.assertEqual(int.from_bytes(extension[5:8],'little'),128*1024)
        self.assertEqual(int.from_bytes(extension[8:11],'little'),0x20000)
        self.assertEqual(rom[:128*1024],extension[16:])
        self.assertEqual(rom[128*1024:],main)
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
        rom_path=next((p for p in sorted((ROOT/'app'/'rom').glob('*.rom')) if p.stat().st_size==384*1024),None)
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
        self.assertAlmostEqual((off[0]-on[0])/1024.0,13.5,delta=1.5)
        on,_=clicks('RIGHT',5)
        for i in range(1,4):
            self.assertAlmostEqual((on[i+1]-on[i])/1024.0,62.5,delta=3.0)

    def test_shipped_key_timing_never_drops_a_key(self):
        """The UI queues 24000 cycles down and 32000 up. Every press must register.

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
                        e.lib.e500_key(e.m,row,bit,1);advance(24000)
                        e.lib.e500_key(e.m,row,bit,0);advance(32000)
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
                e.type('NEW');e.type('LOAD "X:ASCII.BAS"');e.run(10240000)
                e.key('BASIC');e.run();e.type('RUN');self.assertTrue(has_three(e))
                e.screenshot('ce-ascii-roundtrip.png')
                self.assertNotIn('ERROR:',e.trace());self.assertNotIn('timeout',e.trace())
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

if __name__=='__main__':unittest.main(verbosity=2)
