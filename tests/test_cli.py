"""コマンドライン（pokecom）と MCP サーバーのテスト。src/cli をビルドしたものを使う。"""
import base64, json, os, pathlib, subprocess, tempfile, unittest
ROOT = pathlib.Path(__file__).resolve().parents[1]
EXE = pathlib.Path(os.environ.get('POKECOM_CLI') or ROOT / 'src/cli/bin/Release/net10.0' / ('pokecom.exe' if os.name == 'nt' else 'pokecom'))
ROMS = sorted(p for p in (ROOT / 'app/rom').iterdir() if p.suffix.lower() in ('.rom', '.bin') and p.stat().st_size in (256 * 1024, 256 * 1024 + 16, 384 * 1024))
ROM = next((p for p in ROMS if p.name == 'PC-E500(7.2).ROM'), ROMS[0] if ROMS else None)


def cli(*args, text=None, rom=ROM):
    """Runs pokecom; returns (exit code, output, errors)."""
    extra = ['--rom', str(rom)] if rom and args and args[0] not in ('help', 'version', 'roms') else []
    r = subprocess.run([str(EXE), *args, *extra, '--lang', 'en'], input=text, capture_output=True, encoding='utf-8', timeout=120)
    return r.returncode, r.stdout, r.stderr


class CommandLine(unittest.TestCase):
    def test_exec(self):
        code, out, err = cli('exec', 'PRINT 12*34')
        self.assertEqual((code, out.split()[-2], err), (0, '408', ''))

    def test_run_with_print_lprint_and_blank_lines(self):
        code, out, _ = cli('run', '-', text='10 FOR I=1 TO 3\r\n20 PRINT I*I\n\n30 LPRINT "N=";I\n40 NEXT I\n')
        self.assertEqual(code, 0)
        self.assertEqual(out.split('--- printed ---')[0].split()[:3], ['1', '4', '9'])
        self.assertEqual(out.split('--- printed ---')[1].split('\n')[1:4], ['N= 1', 'N= 2', 'N= 3'])

    def test_run_every_typed_sign_and_kana(self):
        # Through LOAD, so the characters are the machine's codes and not keys.
        code, out, _ = cli('run', '-', text='10 PRINT "a!#$%&\'()<>?@[]^_{}ｱｲｳ"\n')
        self.assertEqual(code, 0)
        self.assertIn('a!#$%&\'()<>?@[]^_{}ｱｲｳ', out)

    def test_type_every_sign(self):
        signs = '!"#$%&\'()*+,-./:;<=>?@[]^_{}~'
        code, out, _ = cli('type', signs)
        self.assertEqual(code, 0)
        self.assertIn(signs, out)

    def test_run_with_input(self):
        code, out, _ = cli('run', '-', '--input', 'BOB\\n', text='10 INPUT "NAME";A$\n20 PRINT "HI ";A$\n')
        self.assertEqual(code, 0)
        self.assertIn('HI BOB', out)

    def test_not_ended(self):
        code, _, err = cli('run', '-', '--seconds', '2', text='10 GOTO 10\n')
        self.assertEqual(code, 3)
        self.assertIn('did not end', err)

    def test_bad_programs(self):
        for text, word in [('PRINT 1\n', 'no line number'), ('', 'empty'), ('10 PRINT "漢"\n', 'does not have')]:
            code, _, err = cli('run', '-', text=text)
            self.assertEqual(code, 1, text)
            self.assertIn(word, err)

    def test_usage(self):
        self.assertEqual(cli('fly')[0], 2)
        self.assertEqual(cli('run')[0], 2)
        self.assertEqual(cli('exec', 'PRINT 1', '--speed', '3')[0], 2)
        self.assertEqual(cli('wait', 'long')[0], 2)
        self.assertEqual(cli('help')[0], 0)
        self.assertEqual(cli('exec', 'PRINT 1', rom='no-such.rom')[0], 1)
        self.assertEqual(cli('type', '漢')[0], 1)

    def test_state_goes_on(self):
        with tempfile.TemporaryDirectory() as d:
            state, png = str(pathlib.Path(d, 's.e500state')), pathlib.Path(d, 's.png')
            self.assertEqual(cli('exec', 'A=77', '--state', state)[0], 0)
            code, out, _ = cli('exec', 'PRINT A+1', '--state', state)
            self.assertEqual((code, out.split()[-2]), (0, '78'))
            cli('key', 'BASIC', '--state', state)
            self.assertEqual(cli('type', '10 PRINT 5\\n', '--state', state)[0], 0)
            cli('key', 'BASIC', '--state', state)
            code, out, _ = cli('type', 'RUN\\n', '--state', state, '--png', str(png))
            self.assertIn('5', out.split())
            data = png.read_bytes()
            self.assertEqual((data[:8], int.from_bytes(data[16:20], 'big'), int.from_bytes(data[20:24], 'big')), (b'\x89PNG\r\n\x1a\n', 960, 128))
            try:
                from PIL import Image
                picture = Image.open(png).convert('RGB')
                self.assertGreater(dict((c, n) for n, c in picture.getcolors())[(53, 75, 48)], 100)
            except ImportError:
                pass

    def test_every_rom_and_ram(self):
        for rom in ROMS:
            for ram in ('32', '256'):
                with self.subTest(rom=rom.name, ram=ram):
                    code, out, err = cli('run', '-', '--ram', ram, rom=rom, text='10 A=6\n20 PRINT A*7\n')
                    self.assertEqual((code, '42' in out.split()), (0, True), out + err)


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


class McpServer(unittest.TestCase):
    def setUp(self):
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
        tools = self.c.ask('tools/list')['result']['tools']
        for t in tools:
            self.assertEqual(t['inputSchema']['type'], 'object')
            self.assertTrue(set(t['inputSchema']['required']) <= set(t['inputSchema']['properties']))
        self.assertIn('run_basic', [t['name'] for t in tools])

    def test_run_basic_and_screen(self):
        out = self.text(self.c.call('run_basic', program='10 INPUT N\n20 PRINT N*N\n30 LPRINT "OK"', input='12'))
        self.assertIn('144', out); self.assertIn('Printed:\nOK', out); self.assertIn('Ended.', out)
        r = self.c.call('screen')
        self.assertIn('144', self.text(r))
        self.assertEqual(base64.b64decode(r['content'][1]['data'])[:4], b'\x89PNG')
        self.assertEqual(len(self.c.call('screen', picture=False)['content']), 1)
        # A second program replaces the first, and the first one's printing is not told again.
        out = self.text(self.c.call('run_basic', program='5 PRINT "SECOND"'))
        self.assertIn('SECOND', out); self.assertNotIn('Printed', out)
        self.assertNotIn('INPUT', self.text(self.c.call('exec_basic', line='LIST')))
        self.assertIn('OK', self.text(self.c.call('printed')))

    def test_running_program_is_stopped(self):
        self.assertIn('Not ended', self.text(self.c.call('run_basic', program='10 GOTO 10', seconds=1)))
        self.assertIn('BUSY', self.text(self.c.call('wait', seconds=0.1)))
        self.assertIn('9', self.text(self.c.call('exec_basic', line='PRINT 4+5')).split())

    def test_keys_text_memory_and_state(self):
        self.assertIn('Started', self.text(self.c.call('start', ram=64)))
        self.assertIn('64KB', self.text(self.c.call('start', ram=64)))
        self.assertIn('ABC', self.text(self.c.call('press_keys', keys=['a', 'B', 'c'])))
        self.assertIn('"', self.text(self.c.call('press_keys', keys=['SHIFT+W'])))
        self.text(self.c.call('press_keys', keys=['C-CE']))
        self.assertIn('77', self.text(self.c.call('type_text', text='PRINT 70+7\n')).split())
        self.text(self.c.call('write_memory', address='&HB9000', bytes='12 34 AB'))
        self.assertEqual(self.text(self.c.call('read_memory', address='b9000', length=3)), 'B9000: 12 34 AB')
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, 'a.e500state')
            self.text(self.c.call('exec_basic', line='Z=5'))
            self.text(self.c.call('save_state', path=path))
            self.text(self.c.call('exec_basic', line='Z=6'))
            self.text(self.c.call('load_state', path=path))
            self.assertIn('5', self.text(self.c.call('exec_basic', line='PRINT Z')).split())

    def test_refusals_are_told(self):
        for tool, arguments, word in [
                ('run_basic', {}, 'program is missing'), ('run_basic', {'program': 'HELLO'}, 'line number'), ('press_keys', {'keys': ['NOKEY']}, 'NOKEY'),
                ('press_keys', {'keys': 'A'}, 'keys'), ('type_text', {'text': '漢'}, 'key'), ('write_memory', {'address': 'F0000', 'bytes': '00'}, 'RAM'),
                ('write_memory', {'address': 'B9000', 'bytes': '0'}, 'hex'), ('read_memory', {'address': '123456'}, 'address'), ('wait', {'seconds': -1}, 'seconds'),
                ('save_state', {'path': 'x.txt'}, '.e500state'), ('load_state', {'path': 'no-such.e500state'}, 'no-such'), ('start', {'rom': 'none.rom'}, 'none.rom')]:
            with self.subTest(tool=tool, arguments=arguments):
                r = self.c.call(tool, **arguments)
                self.assertTrue(r.get('isError'), r)
                self.assertIn(word, r['content'][0]['text'])
        # And the server is still well.
        self.assertIn('3', self.text(self.c.call('exec_basic', line='PRINT 1+2')).split())


if __name__ == '__main__':
    if ROM is None:
        raise SystemExit('No ROM in app/rom')
    unittest.main(verbosity=2)
