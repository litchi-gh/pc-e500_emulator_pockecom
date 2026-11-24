"""コマンドライン（pokecom）と MCP サーバーのテスト。src/cli をビルドしたものを使う。"""
import base64, json, os, pathlib, subprocess, tempfile, unittest, wave
ROOT = pathlib.Path(__file__).resolve().parents[1]
EXE = pathlib.Path(os.environ.get('POKECOM_CLI') or ROOT / 'src/cli/bin/Release/net10.0' / ('pokecom.exe' if os.name == 'nt' else 'pokecom'))
ROMS = sorted(p for p in (ROOT / 'app/rom').iterdir() if p.suffix.lower() in ('.rom', '.bin') and p.stat().st_size in (256 * 1024, 256 * 1024 + 16, 384 * 1024))
ROM = next((p for p in ROMS if p.name == 'PC-E500(7.2).ROM'), ROMS[0] if ROMS else None)
PNG = b'\x89PNG\r\n\x1a\n'


def cli(*args, text=None, rom=ROM):
    """Runs pokecom; returns (exit code, output, errors)."""
    extra = ['--rom', str(rom)] if rom and args and args[0] not in ('help', 'version') else []
    r = subprocess.run([str(EXE), *args, *extra, '--lang', 'en'], input=text, capture_output=True, encoding='utf-8', timeout=120)
    return r.returncode, r.stdout, r.stderr


def saved_bin(address, data):
    """A BIN with the SAVE M header."""
    return bytes([0xff, 0, 6, 1, 0x10]) + len(data).to_bytes(3, 'little') + address.to_bytes(3, 'little') + bytes([0xff, 0xff, 0xff, 0, 0x0f]) + data


class Folder(unittest.TestCase):
    def setUp(self):
        d = tempfile.TemporaryDirectory()
        self.addCleanup(d.cleanup)
        self.dir = pathlib.Path(d.name)

    def path(self, name):
        return str(self.dir / name)

    def script(self, text, *args, code=0, rom=ROM):
        got, out, err = cli('script', '-', *args, text=text, rom=rom)
        self.assertEqual(got, code, out + err)
        return out if code == 0 else err


class CommandLine(Folder):
    def test_exec(self):
        code, out, err = cli('exec', 'PRINT 12*34')
        self.assertEqual((code, out.split()[-2], err), (0, '408', ''))

    def test_run_with_print_lprint_and_blank_lines(self):
        code, out, _ = cli('run', '-', text='10 FOR I=1 TO 3\r\n20 PRINT I*I\n\n30 LPRINT "N=";I\n40 NEXT I\n')
        self.assertEqual(code, 0)
        self.assertEqual(out.split('Printed:')[0].split()[:3], ['1', '4', '9'])
        self.assertEqual(out.split('Printed:\n')[1].split('\n')[:3], ['N= 1', 'N= 2', 'N= 3'])

    def test_run_signs_and_kana(self):
        # Through LOAD, so the characters are the machine's codes and not keys.
        code, out, _ = cli('run', '-', text='10 PRINT "a!#$%&\'()<>?@[]^_{}ｱｲｳ"\n')
        self.assertEqual(code, 0)
        self.assertIn('a!#$%&\'()<>?@[]^_{}ｱｲｳ', out)

    def test_type_every_sign(self):
        signs = '!"#$%&\'()*+,-./:;<=>?@[]^_{}~'
        code, out, _ = cli('type', signs)
        self.assertEqual(code, 0)
        self.assertIn(signs, out)

    def test_run_with_input_and_program_text(self):
        code, out, _ = cli('run', '--program', '10 INPUT "NAME";A$\\n20 PRINT "HI ";A$', '--input', 'BOB\\n')
        self.assertEqual(code, 0)
        self.assertIn('HI BOB', out)

    def test_not_ended(self):
        code, _, err = cli('run', '-', '--seconds', '2', text='10 GOTO 10\n')
        self.assertEqual(code, 3)
        self.assertIn('did not end', err)

    def test_basic_errors_have_a_code(self):
        for text, word in [('10 GOTO 99\n', 'Undefined line in 10'), ('10 A=1/0\n', 'Division by Zero in 10'), ('10 NEXT I\n', 'NEXT without FOR in 10'), ('10 PRINT "Syntax error"\n20 GOTO 20.5E9\n', ' in 20')]:
            code, out, err = cli('run', '-', text=text)
            self.assertEqual(code, 4, out + err)
            self.assertIn(word, err)
        code, _, err = cli('exec', 'PRIMT 5')
        self.assertEqual(code, 4)
        self.assertIn('Syntax error', err)
        # A long message goes on over a second line.
        for rom in ROMS:
            if rom.stem.startswith('PC-E650'):
                code, _, err = cli('run', '-', rom=rom, text='10 GOSUB 10\n')
                self.assertEqual((code, 'GOSUB,FOR,REPEAT,WHILE or SWITCH nesting exceeded in 10' in err), (4, True), err)
        # The same words printed by a program are not an error, nor is a value or an assignment.
        self.assertEqual(cli('run', '-', text='10 PRINT "Syntax error"\n')[0], 0)
        self.assertEqual(cli('exec', 'A=2')[0], 0)
        # One the program handles itself is none.
        self.assertEqual(cli('run', '-', text='10 ON ERROR GOTO 100\n20 A=1/0\n30 END\n100 PRINT "OK"\n110 END\n')[0], 0)

    def test_bad_programs(self):
        for text, word in [('PRINT 1\n', 'no line number'), ('', 'empty'), ('10 PRINT "漢"\n', 'does not have')]:
            code, _, err = cli('run', '-', text=text)
            self.assertEqual(code, 1, text)
            self.assertIn(word, err)

    def test_usage(self):
        for args in [('fly',), ('run',), ('exec',), ('exec', 'A', 'B'), ('exec', 'PRINT 1', '--speed', '3'), ('wait', 'long'), ('exec', 'A', '--png'), ('key-down', 'A'), ('start',), ('run', 'a.bas', '--program', '10 A=1'), ()]:
            code, out, err = cli(*args)
            self.assertIn(code, (1, 2), args)
            self.assertNotIn('   at ', err)
        self.assertEqual(cli('fly')[0], 2)
        self.assertEqual(cli('exec')[0], 2)
        for args in [('help',), ('help', 'run'), ('run', '--help'), ('version',)]:
            self.assertEqual(cli(*args)[0], 0, args)
        for args in [('exec', 'PRINT 1', '--ram', '33'), ('type', '漢'), ('run', 'no-such.bas'), ('key', 'NOKEY'), ('exec', 'A=1', '--card', 'no-such.ram'), ('load-state', 'x.txt')]:
            code, _, err = cli(*args)
            self.assertEqual(code, 1, args)
            self.assertTrue(err.strip())
            self.assertNotIn('   at ', err)
        self.assertEqual(cli('exec', 'PRINT 1', rom='no-such.rom')[0], 1)
        # Every command is in the help, and has its own.
        overview = cli('help')[1]
        for name in ['run', 'load', 'program', 'exec', 'reserve', 'type', 'key', 'wait-text', 'screen', 'record', 'printed', 'drive-get', 'card-create', 'registers', 'memory', 'poke-imem', 'ram-map', 'save-bin', 'script', 'mcp']:
            self.assertIn('  ' + name, overview)

    def test_json(self):
        code, out, _ = cli('exec', 'PRINT 6*7', '--json')
        answer = json.loads(out)
        self.assertEqual((code, answer['code']), (0, 0))
        self.assertIn('42', ' '.join(answer['screen']).split())
        self.assertIn('RUN', answer['symbols'])

    def test_state_goes_on(self):
        state, png = self.path('s.e500state'), self.dir / 's.png'
        self.assertEqual(cli('exec', 'A=77', '--state', state)[0], 0)
        code, out, _ = cli('exec', 'PRINT A+1', '--state', state)
        self.assertEqual((code, out.split()[-2]), (0, '78'))
        cli('key', 'BASIC', '--state', state)
        self.assertEqual(cli('type', '10 PRINT 5\\n', '--state', state)[0], 0)
        cli('key', 'BASIC', '--state', state)
        code, out, _ = cli('type', 'RUN\\n', '--state', state, '--png', str(png))
        self.assertIn('5', out.split())
        data = png.read_bytes()
        self.assertEqual((data[:8], int.from_bytes(data[16:20], 'big'), int.from_bytes(data[20:24], 'big')), (PNG, 960, 128))
        try:
            from PIL import Image
        except ImportError:
            return
        self.assertGreater(dict((c, n) for n, c in Image.open(png).convert('RGB').getcolors())[(53, 75, 48)], 100)

    def test_every_rom_and_ram(self):
        for rom in ROMS:
            for ram in ('32', '256'):
                with self.subTest(rom=rom.name, ram=ram):
                    code, out, err = cli('run', '-', '--ram', ram, rom=rom, text='10 A=6\n20 PRINT A*7\n')
                    self.assertEqual((code, '42' in out.split()), (0, True), out + err)
            with self.subTest(rom=rom.name, what='error'):
                self.assertEqual(cli('run', '-', rom=rom, text='10 GOTO 99\n')[0], 4)
            with self.subTest(rom=rom.name, what='program, machine code, all reset'):
                out = self.script('load --program "10 PRINT 5"\nprogram\nreserve 1000\ninfo\nprogram\nexec "A=9"\nreset\nkey BASIC\nexec "PRINT A"\nall-reset yes\ninfo\n', rom=rom)
                self.assertEqual(out.count('10 PRINT 5\n'), 2, out)
                self.assertIn('BEC00-BFBFF', out)


class Scripts(Folder):
    def test_words_and_comments(self):
        out = self.script('# a comment\n\nexec "PRINT 1+1"   # another\n  wait 0.5\n')
        self.assertIn('2', out.split())
        self.assertIn('HI', self.script('exec "PRINT ""HI"""\n').split())
        self.assertIn('Line 2', self.script('wait 0.1\nfly\n', code=1))
        self.assertIn('not closed', self.script('type "AB\n', code=1))
        # A BASIC error ends it with its code, and nothing after it is done.
        self.assertIn('Syntax error', self.script(f'exec "PRIMT 5"\ncard-create "{self.path("c.ram")}" 8\n', code=4))
        self.assertFalse((self.dir / 'c.ram').exists())

    def test_keys_held_and_text_awaited(self):
        out = self.script(f'load "{self.save("k.bas", "10 A$=INKEY$:IF A$=CHR$ 0 OR A$=CHR$ 13 THEN 10" + chr(10) + "20 IF A$<>CHR$ 90 THEN 10" + chr(10) + "30 PRINT CHR$ 75;A$" + chr(10))}"\ntype "RUN\\n"\nkey-down Z\nwait-text KZ --seconds 10\nkey-up\n')
        self.assertIn('KZ', out)
        self.assertIn('did not come up', self.script('wait-text NEVER --seconds 1\n', code=3))

    def save(self, name, text):
        (self.dir / name).write_text(text, encoding='utf-8')
        return self.path(name)

    def test_program_in_and_out(self):
        bas, out_text, out_saved = self.path('a.bas'), self.dir / 'o.txt', self.dir / 'o.bas'
        (self.dir / 'a.bas').write_text('10 REM ｶﾅ ¥\n20 PRINT "HELLO"\n', encoding='utf-8')
        out = self.script(f'load "{bas}"\nexec "Z=3"\nprogram\nprogram --to "{out_text}"\nprogram --saved --to "{out_saved}"\nexec "PRINT Z"\n')
        self.assertIn('20 PRINT "HELLO"', out)
        self.assertIn('3', out.split())
        self.assertIn('ｶﾅ', out_text.read_text(encoding='utf-8'))
        self.assertEqual(out_saved.read_bytes()[0], 0xff)
        # The machine's own form goes straight into RAM and is the same program.
        out = self.script(f'run "{out_saved}"\nprogram\n')
        self.assertIn('HELLO', out.split('\n'))
        self.assertIn('20 PRINT "HELLO"', out)
        self.assertIn('no program', self.script('program\n'))

    def test_machine_code(self):
        # MV A,5 / MV [BE100],A / RETF at BE000.
        code = bytes([0x08, 0x05, 0xa8, 0x00, 0xe1, 0x0b, 0x07])
        (self.dir / 'm.bin').write_bytes(saved_bin(0xbe000, code))
        out = self.script(f'load "{self.path("m.bin")}"\ninfo\nexec "CALL &HBE000"\nmemory BE100 1\nram-map\nreserve 0\ninfo\n')
        self.assertIn('Reserved the machine-code area and wrote BE000-BE006', out)
        self.assertIn('BE000-BFBFF (1C00h bytes)', out)
        self.assertIn('BE100: 05', out)
        self.assertIn('Machine code', out)
        self.assertIn('Machine-code area: none', out)
        # Not over what is in use, and not where there is no RAM; by its address it goes all the same.
        (self.dir / 'u.bin').write_bytes(saved_bin(0xb8020, code))
        self.assertIn('parts in use', self.script(f'load "{self.path("u.bin")}" --no-reserve\n', code=1))
        (self.dir / 'n.bin').write_bytes(saved_bin(0xa0000, code))
        self.assertIn('not in this machine', self.script(f'load "{self.path("n.bin")}"\n', code=1))
        (self.dir / 'raw.bin').write_bytes(b'\x01\x02\x03')
        self.assertIn('needs an address', self.script(f'load "{self.path("raw.bin")}"\n', code=1))
        out = self.script(f'load-bin "{self.path("raw.bin")}" BD000\nmemory bd000 3\nsave-bin "{self.path("s.bin")}" BD000 3 --header\nsave-bin "{self.path("t.bin")}" &HBD000 3\n')
        self.assertIn('BD000: 01 02 03', out)
        self.assertEqual((self.dir / 's.bin').read_bytes(), saved_bin(0xbd000, b'\x01\x02\x03'))
        self.assertEqual((self.dir / 't.bin').read_bytes(), b'\x01\x02\x03')
        # A machine of 256KB has it.
        self.script(f'load "{self.path("n.bin")}"\nmemory A0000 7\n', '--ram', '256')

    def test_debugger(self):
        out = self.script('registers\nset-register X 12345\nregisters\nimem\npoke-imem 10 AB CD\n'.replace('AB CD', '"AB CD"') + 'imem\npoke BD000 "12 34"\nmemory BD000 2\nram-map\n')
        self.assertIn('X=12345', out)
        self.assertIn('10: AB CD', out)
        self.assertIn('BD000: 12 34', out)
        self.assertIn('Free', out)
        for bad in ['poke F0000 00', 'poke BD000 1', 'poke-imem EE "00 00"', 'set-register PC 1', 'memory 100000', 'memory BD000 5000', 'memory XYZ']:
            self.script(bad + '\n', code=1)

    def test_drive(self):
        (self.dir / 'in.dat').write_bytes(b'12345')
        drive = self.dir / 'drive'
        drive.mkdir()
        out = self.script(f'drive-files\ndrive-put "{self.path("in.dat")}" DATA.TXT\ndrive-files\n'
                          f'load --program "10 PRINT 1"\ndrive-files\ndrive-get DATA.TXT "{self.path("back.dat")}"\ndrive-trace\n', '--disk', str(drive))
        self.assertIn('(no files)', out)
        self.assertEqual((self.dir / 'back.dat').read_bytes(), b'12345')
        # Loading a program leaves nothing in a folder of the user's.
        self.assertEqual(sorted(p.name for p in drive.iterdir()), ['DATA.TXT'])
        for bad in ['drive-get NONE.TXT x', 'drive-get ../in.dat x', f'drive-put "{self.path("none")}"']:
            self.script(bad + '\n', '--disk', str(drive), code=1)
        # The machine saves to it, and not when it is protected or away.
        (self.dir / 'p.bas').write_text('10 OPEN "X:OUT.DAT" FOR OUTPUT AS #2\n20 PRINT #2,"HI"\n30 CLOSE #2\n', encoding='utf-8')
        self.script(f'run "{self.path("p.bas")}"\n', '--disk', str(drive))
        self.assertEqual((drive / 'OUT.DAT').read_bytes(), b'HI\r\n')
        (drive / 'OUT.DAT').unlink()
        self.script(f'run "{self.path("p.bas")}"\n', '--disk', str(drive), '--disk-read-only', code=4)
        self.assertFalse((drive / 'OUT.DAT').exists())
        self.assertIn('not connected', self.script('configure --disk none\ninfo\n'))
        self.script('configure --disk none\ndrive-files\n', code=1)
        self.script('configure --disk none\nload --program "10 A=1"\nprogram\n')

    def test_card(self):
        card = self.path('c.ram')
        self.assertEqual(cli('card-create', card, '64')[0], 0)
        self.assertEqual(cli('card-create', card, '64')[0], 1)
        self.assertEqual((self.dir / 'c.ram').read_bytes(), b'\xff' * 65536)
        out = self.script('info\npoke 48000 "AA BB"\nmemory 48000 2\n', '--card', card)
        self.assertIn('64KB (', out)
        self.assertEqual((self.dir / 'c.ram').read_bytes()[0x8000:0x8002], b'\xaa\xbb')
        # Put in and taken out of a machine in use: the internal RAM stays.
        out = self.script(f'exec "A=41"\npoke BD000 77\nconfigure --card "{card}"\nmemory 48000 2\nmemory BD000 1\nconfigure --card none\ninfo\nmemory BD000 1\n')
        self.assertIn('48000: AA BB', out)
        self.assertEqual(out.count('BD000: 77'), 2)
        self.assertIn('RAM card: none', out)

    def test_configure_and_printer(self):
        out = self.script('configure --ram 128\nexec "PRINT 7*6"\nconfigure --printer off\ninfo\n')
        self.assertIn('Internal RAM: 128KB', out)
        self.assertIn('42', out.split())
        self.assertIn('CE-126P: not connected', out)
        self.assertIn('nothing printed', self.script('exec "LPRINT 5"\nprinted\n', '--no-printer'))
        out = self.script('exec "LPRINT 5"\nconfigure --printer off\nexec "LPRINT 6"\nconfigure --printer on\nprinted\n')
        self.assertEqual(out.strip().split('\n')[-1].strip(), '5')
        out = self.script(f'exec "LPRINT 11"\nexec "LPRINT 22"\nprinted --to "{self.path("p.txt")}" --png "{self.path("p.png")}"\npaper-cut\nprinted\n')
        self.assertIn('nothing printed', out)
        self.assertEqual((self.dir / 'p.txt').read_text(encoding='utf-8').split(), ['11', '22'])
        self.assertEqual((self.dir / 'p.png').read_bytes()[:8], PNG)
        self.script('configure --ram 33\n', code=1)

    def test_sound(self):
        self.script(f'record "{self.path("b.wav")}" --line "BEEP 1" --seconds 1\nrecord "{self.path("q.wav")}" --seconds 1\n')
        for name, sounding in [('b.wav', True), ('q.wav', False)]:
            with wave.open(self.path(name)) as w:
                self.assertEqual((w.getframerate(), w.getnchannels(), w.getsampwidth()), (48000, 1, 2))
                data = w.readframes(w.getnframes())
                self.assertAlmostEqual(w.getnframes() / 48000, 1, delta=0.6)
            samples = [int.from_bytes(data[i:i + 2], 'little', signed=True) for i in range(0, len(data), 2)]
            changes = sum(a != b for a, b in zip(samples, samples[1:]))
            self.assertEqual(changes > 500, sounding, f'{name}: {changes}')

    def test_resets_and_power(self):
        out = self.script('exec "A=5"\nreset\nkey BASIC\nexec "PRINT A"\nall-reset no\nkey BASIC\nexec "PRINT A+1"\nkey OFF --seconds 2\ninfo\nexec "PRINT A+2"\n')
        for n in ('5', '6', '7'):
            self.assertIn(n, out.split())
        self.assertIn('12', self.script('key OFF --seconds 0\nexec "PRINT 5+7"\n').split())
        self.assertIn('Power: off', out)
        out = self.script('load --program "10 A=1"\nall-reset yes\nprogram\n')
        self.assertIn('(no program)', out)
        out = self.script('all-reset\n')
        self.assertIn('Y', out)
        # Left alone it stays on, unless it is let switch itself off.
        self.assertIn('Power: on', self.script('wait 700\ninfo\n'))
        self.assertIn('Power: off', self.script('configure --auto-power-off on\nwait 700\ninfo\n'))

    def test_states(self):
        s = self.path('a.e500state')
        out = self.script(f'exec "Z=5"\nsave-state "{s}"\nexec "Z=6"\nload-state "{s}"\nexec "PRINT Z+100"\n')
        self.assertIn('105', out.split())
        self.assertIn('105', cli('exec', 'PRINT Z+100', '--state', s)[1].split())


class Client:
    def __init__(self):
        self.p = subprocess.Popen([str(EXE), 'mcp', '--rom', str(ROM), '--lang', 'en'], stdin=subprocess.PIPE, stdout=subprocess.PIPE, encoding='utf-8')
        self.n = 0

    def send(self, line):
        self.p.stdin.write(line + '\n'); self.p.stdin.flush()

    def ask(self, method, **params):
        self.n += 1
        self.send(json.dumps({'jsonrpc': '2.0', 'id': self.n, 'method': method, 'params': params}))
        r = json.loads(self.p.stdout.readline())
        assert r['id'] == self.n
        return r

    def call(self, tool, **arguments):
        return self.ask('tools/call', name=tool, arguments=arguments)['result']

    def close(self):
        self.p.stdin.close()
        code = self.p.wait(20)
        self.p.stdout.close()
        return code


class McpServer(Folder):
    def setUp(self):
        super().setUp()
        self.c = Client()

    def tearDown(self):
        self.assertEqual(self.c.close(), 0)

    def text(self, result):
        self.assertFalse(result.get('isError'), result)
        return result['content'][0]['text']

    def test_handshake_and_errors(self):
        r = self.c.ask('initialize', protocolVersion='2025-03-26', capabilities={}, clientInfo={'name': 't', 'version': '1'})['result']
        self.assertEqual((r['protocolVersion'], r['serverInfo']['name']), ('2025-03-26', 'pokecom'))
        self.assertEqual(self.c.ask('initialize', protocolVersion='1999-01-01')['result']['protocolVersion'], '2025-06-18')
        # A notification is not answered: the next line is the ping's.
        self.c.send('{"jsonrpc":"2.0","method":"notifications/initialized"}')
        self.assertEqual(self.c.ask('ping')['result'], {})
        self.assertEqual(self.c.ask('no/such')['error']['code'], -32601)
        self.assertEqual(self.c.ask('tools/call', name='nope', arguments={})['error']['code'], -32602)
        self.c.send('{bad')
        self.assertEqual(json.loads(self.c.p.stdout.readline())['error']['code'], -32700)
        self.c.send('[1]')
        self.assertEqual(json.loads(self.c.p.stdout.readline())['error']['code'], -32600)

    def test_every_command_is_a_tool(self):
        tools = {t['name']: t for t in self.c.ask('tools/list')['result']['tools']}
        for t in tools.values():
            self.assertEqual(t['inputSchema']['type'], 'object')
            self.assertTrue(set(t['inputSchema']['required']) <= set(t['inputSchema']['properties']))
            self.assertTrue(t['description'])
        overview = cli('help')[1].split('Commands')[1].split('On one machine')[0]
        commands = {line.split()[0].replace('-', '_') for line in overview.split('\n')[1:] if line.startswith('  ')}
        self.assertEqual(set(tools) - commands, {'start', 'key_down', 'key_up'})
        self.assertEqual(commands - set(tools), set())

    def test_run_and_screen(self):
        out = self.text(self.c.call('run', program='10 INPUT N\n20 PRINT N*N\n30 LPRINT "OK"', input='12'))
        self.assertIn('144', out); self.assertIn('Printed:\nOK', out)
        r = self.c.call('screen', picture=True)
        self.assertIn('144', self.text(r))
        self.assertEqual(base64.b64decode(r['content'][1]['data'])[:8], PNG)
        self.assertEqual(len(self.c.call('screen')['content']), 1)
        # A second program replaces the first, and the first one's printing is not told again.
        out = self.text(self.c.call('run', program='5 PRINT "SECOND"'))
        self.assertIn('SECOND', out); self.assertNotIn('Printed', out)
        self.assertEqual(self.text(self.c.call('program')), '5 PRINT "SECOND"')
        self.assertIn('OK', self.text(self.c.call('printed')))
        self.assertIn('BASIC error: Undefined line in 10', self.text(self.c.call('run', program='10 GOTO 5')))

    def test_running_program_is_stopped(self):
        self.assertIn('did not end', self.text(self.c.call('run', program='10 GOTO 10', seconds=1)))
        self.assertIn('BUSY', self.text(self.c.call('wait', seconds=0.1)))
        self.assertIn('9', self.text(self.c.call('exec', line='PRINT 4+5')).split())

    def test_keys_memory_start_and_state(self):
        self.assertIn('Internal RAM: 64KB', self.text(self.c.call('start', ram=64)))
        self.assertIn('ABC', self.text(self.c.call('key', keys=['a', 'B', 'c'])))
        self.assertIn('"', self.text(self.c.call('key', keys=['SHIFT+W'])))
        self.text(self.c.call('key', keys=['C-CE']))
        self.assertIn('77', self.text(self.c.call('type', text='PRINT 70+7\n')).split())
        self.text(self.c.call('poke', address='&HBD000', bytes='12 34 AB'))
        self.assertEqual(self.text(self.c.call('memory', address='bd000', length=3)), 'BD000: 12 34 AB')
        self.assertEqual(self.text(self.c.call('memory', address=0xbd000, length=1)), 'BD000: 12')
        path = self.path('a.e500state')
        self.text(self.c.call('exec', line='Z=5'))
        self.text(self.c.call('save_state', path=path))
        self.text(self.c.call('exec', line='Z=6'))
        self.text(self.c.call('load_state', path=path))
        self.assertIn('105', self.text(self.c.call('exec', line='PRINT Z+100')).split())
        # A fresh machine has none of it, and keeps the size asked for before.
        self.assertIn('64KB', self.text(self.c.call('start')))
        self.assertIn('100', self.text(self.c.call('exec', line='PRINT Z+100')).split())

    def test_refusals_are_told(self):
        for tool, arguments, word in [
                ('run', {}, 'program'), ('run', {'program': 'HELLO'}, 'line number'), ('run', {'file': '-'}, 'standard input'), ('key', {'keys': ['NOKEY']}, 'NOKEY'), ('key', {'keys': ['A', 'NOKEY']}, 'NOKEY'),
                ('key', {'keys': 'A'}, 'keys'), ('key', {'keys': [1]}, 'keys'), ('type', {'text': '漢'}, 'key'), ('type', {'text': 5}, 'text'), ('poke', {'address': 'F0000', 'bytes': '00'}, 'RAM'),
                ('poke', {'address': 'BD000', 'bytes': '0'}, 'hex'), ('memory', {'address': '123456'}, 'address'), ('wait', {'seconds': -1}, 'seconds'), ('wait', {'secs': 1}, 'secs'),
                ('save_state', {'path': 'x.txt'}, '.e500state'), ('load_state', {'path': 'no-such.e500state'}, 'no-such'), ('start', {'rom': 'none.rom'}, 'none.rom'), ('start', {'ram': 7}, 'size')]:
            with self.subTest(tool=tool, arguments=arguments):
                r = self.c.call(tool, **arguments)
                self.assertTrue(r.get('isError'), r)
                self.assertIn(word, r['content'][0]['text'])
        # And the server is still well.
        self.assertIn('3', self.text(self.c.call('exec', line='PRINT 1+2')).split())


if __name__ == '__main__':
    if ROM is None:
        raise SystemExit('No ROM in app/rom')
    unittest.main(verbosity=2)
