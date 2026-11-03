"""Every ROM image in app/rom, through the same steps.

Each ROM is booted and initialised with PF1, then made to calculate, to take
and run a BASIC program, to SAVE it to and LOAD it from a CE-140F (a fresh
temporary folder), and to LPRINT on the CE-126P. A screenshot of each step is
left in tests/output/roms/ to look at.

app/rom にあるすべての ROM で、同じ手順を試す：起動と PF1 の初期化、計算、BASIC のプログラムの
入力と実行、CE-140F への SAVE と LOAD（毎回新しい一時フォルダ）、CE-126P への LPRINT。
各段階の画面を tests/output/roms/ に残す。
実行：py tests/test_roms.py
"""
import pathlib
import tempfile
import unittest
from rom_harness import Emulator,ROOT
from test_hardware import has_three

ROMS=sorted(p for p in (ROOT/'app'/'rom').iterdir() if p.suffix.lower() in ('.rom','.bin') and p.stat().st_size in (256*1024,256*1024+16,384*1024))

def version(rom):
    """ROM の末尾にある版（ROM Ver.）。読めなければ None。"""
    data=rom.read_bytes()
    if len(data)==256*1024+16:data=data[16:]
    major,minor=data[-16],data[-15]
    return f'{major}.{minor}' if major<=99 and minor<=99 else None

class RomTests(unittest.TestCase):
    def test_every_rom(self):
        self.assertTrue(ROMS,'No ROM image in app/rom')
        for rom in ROMS:
            with self.subTest(rom=rom.name):
                self.check(rom)

    def check(self,rom):
        # Version 8 is the E650/U6000 generation (the E500S's too): its 256KB
        # main ROM (S3A) calls into the 128KB extension (S3B), and alone never
        # gets past the start. Such an image is incomplete, not unsupported:
        # S3B dumped from the same machine and merged in front of it
        # (tools/merge_savem_rom.py) makes the 384KB image.
        # Ver.8 は E650/U6000（E500S も）の世代で、256KB の本体（S3A）は 128KB の拡張（S3B）を呼ぶため、
        # 本体だけでは起動を終えない。足りない ROM なので、同じ機械の S3B を結合した 384KB にする。
        size=rom.stat().st_size;ver=version(rom)
        if size!=384*1024 and ver and int(ver.split('.')[0])>=8:
            self.skipTest(f'{rom.name} is Ver.{ver}, the main ROM (S3A) of a 384KB set: merge its S3B (tools/merge_savem_rom.py)')
        name=rom.stem
        def shot(step):e.screenshot(f'roms/{name}-{step}.png')
        with tempfile.TemporaryDirectory() as disk:
            # The E650/U6000's 384KB ROM comes with 64KB inside as standard.
            e=Emulator(rom=rom,ram=64 if rom.stat().st_size==384*1024 else 32,disk=disk)
            try:
                e.run(10240000);shot('1-start')
                e.key('F1');e.run(10240000);shot('2-initialised')
                e.key('BASIC');e.run();e.type('1+2');shot('3-calculation')
                self.assertTrue(has_three(e),f'{rom.name}: 1+2 does not show 3')
                # PRO mode, a line, back to RUN and run it.
                e.key('BASIC');e.run();e.type('10 PRINT 123')
                e.key('BASIC');e.run();e.type('RUN');shot('4-program')
                self.assertTrue(has_three(e),f'{rom.name}: the program does not print 123')
                # SAVE to the CE-140F, NEW, LOAD it back and run it.
                e.type('SAVE "X:ROMTEST.BAS"',enter=False);e.key('ENTER');e.run(20480000);shot('5-save')
                saved=pathlib.Path(disk)/'ROMTEST.BAS'
                self.assertTrue(saved.exists() and saved.stat().st_size>0,f'{rom.name}: SAVE "X:" wrote nothing; trace: {e.trace()[-300:]}')
                e.type('NEW');e.type('LOAD "X:ROMTEST.BAS"',enter=False);e.key('ENTER');e.run(20480000)
                e.type('RUN');shot('6-load')
                self.assertTrue(has_three(e),f'{rom.name}: the program LOADed back does not print 123')
                # LPRINT on the CE-126P.
                e.printed();e.type('LPRINT "ROM OK"');e.run(10240000);shot('7-lprint')
                self.assertEqual(e.printed(),b'ROM OK\n',f'{rom.name}: LPRINT did not reach the printer')
            finally:e.close()

if __name__=='__main__':
    for rom in ROMS:print(f'{rom.name}: {rom.stat().st_size//1024}KB, ROM Ver.{version(rom)}')
    unittest.main(verbosity=2)
