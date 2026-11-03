"""ROM を使うテストの土台：e500core.dll を ctypes で直接動かす。

既定では app/rom/PC-E500(7.2).ROM（なければ以前の名前の PC-E500.rom）を使う（ROM はリポジトリに含まれない）。
"""
from pathlib import Path
import ctypes as c
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
# キーの行列（Keyboard.cs の Matrix と同じ並び）。KEYS はキーの名前から（行, ビット）を引く表。
MATRIX=['TEXT Q MENU A BASIC Z SHIFT CTRL','W E S D X C CAPS KANA','R T F G V B SPACE DOWN','Y U H J N M UP LEFT','I O K L , ; RIGHT ENTER','RCL HYP HEX EXP 7 4 1 0','STO SIN DEG ^ 8 5 2 +/-','C-CE COS LN SQRT 9 6 3 .','STAT TAN LOG SQUARE / * - +',') FSE INV ( DEL BS INS =','P 2ndF F5 F4 F3 F2 F1','OFF ON/BRK']
KEYS={s:(i,j) for i,row in enumerate(MATRIX) for j,s in enumerate(row.split())}
# SHIFT を押して入力する文字と、その元のキー。
# The ROM the tests use when none is named: Ver.7.2 named model(version).ROM, or the older PC-E500.rom.
DEFAULT_ROM=next((p for p in (ROOT/'app'/'rom'/n for n in ('PC-E500(7.2).ROM','PC-E500.rom')) if p.exists()),ROOT/'app'/'rom'/'PC-E500.rom')
SHIFT={'"':'W',':':';','!':'Q','<':'I','>':'O','$':'R','?':',','#':'E'}
class Emulator:
 """エミュレーションコア1台。ram・card は内蔵 RAM とカードの容量（KB）、disk は CE-140F のフォルダ、rom は ROM のパス。"""
 def __init__(self,ram=32,card=0,disk=None,rom=None):
  self.capture=None
  self.lib=c.CDLL(str(ROOT/'bin/e500core.dll'));l=self.lib
  for n,types,result in [('create_config',[c.c_void_p,c.c_int,c.c_int,c.c_int],c.c_void_p),('run',[c.c_void_p,c.c_int],c.c_int),('key',[c.c_void_p,c.c_int,c.c_int,c.c_int],None),('pixels',[c.c_void_p,c.c_void_p],None),('reg',[c.c_void_p,c.c_int],c.c_uint32),('disk_mount',[c.c_void_p,c.c_wchar_p,c.c_int],c.c_int),('disk_trace',[c.c_void_p,c.c_void_p,c.c_int],c.c_int),('printer_read',[c.c_void_p,c.c_void_p,c.c_int],c.c_int),('printer_connect',[c.c_void_p,c.c_int],None),('peek',[c.c_void_p,c.c_int],c.c_int),('audio',[c.c_void_p,c.c_void_p,c.c_int],c.c_int),('reset',[c.c_void_p,c.c_int],None),('imem',[c.c_void_p,c.c_void_p],c.c_int),('cycles',[c.c_void_p],c.c_uint64),('symbols',[c.c_void_p,c.c_int],c.c_int),('destroy',[c.c_void_p],None)]:
   f=getattr(l,'e500_'+n);f.argtypes=types;f.restype=result
  rom_path=Path(rom) if rom else DEFAULT_ROM
  rom_data=rom_path.read_bytes()
  # A main ROM dumped with SAVEM keeps its 16-byte header; the desktop takes it off as well.
  # SAVEM のヘッダー付きの本体 ROM（C0000 から 256KB）は、デスクトップと同じくヘッダーを外す。
  if len(rom_data)==256*1024+16 and rom_data[:5]==bytes.fromhex('ff 00 06 01 10') and rom_data[8:11]==bytes.fromhex('00 00 0c'):rom_data=rom_data[16:]
  self.m=l.e500_create_config(rom_data,len(rom_data),ram,card)
  if not self.m:raise ValueError(f'Unsupported ROM image: {rom_path} ({len(rom_data)} bytes)')
  if disk: l.e500_disk_mount(self.m,str(Path(disk).resolve()),0)
 def run(self,cycles=1024000):
  """cycles サイクル実行する（CPU が止まったら例外）。capture がリストなら音声のサンプルを足していく。"""
  while cycles>0:
   n=min(cycles,10240000);r=self.lib.e500_run(self.m,n);cycles-=n
   if r:raise RuntimeError(f'CPU stopped PC {self.lib.e500_reg(self.m,9):05x}')
   audio=(c.c_int16*48000)();count=self.lib.e500_audio(self.m,audio,48000)
   if self.capture is not None:self.capture.extend(audio[:count])
 def key(self,key):
  """キーを1回押して離す（ROM のキー走査に間に合う長さで）。"""
  r,b=KEYS[key];self.lib.e500_key(self.m,r,b,1);self.run(150000);self.lib.e500_key(self.m,r,b,0);self.run(180000)
 def type(self,text,enter=True):
  """文字列をキーで入力する（必要なら SHIFT 付き）。enter なら最後に ENTER を押して待つ。"""
  for ch in text:
   if ch in SHIFT:
    r,b=KEYS['SHIFT'];self.lib.e500_key(self.m,r,b,1);self.run(20000);self.key(SHIFT[ch]);self.lib.e500_key(self.m,r,b,0);self.run(20000)
   else:self.key('SPACE' if ch==' ' else ch.upper())
  if enter:self.key('ENTER');self.run(2048000)
 def boot(self):
  """起動して PF1 で初期化し、BASIC の RUN モードにする。"""
  self.run();self.key('F1');self.run();self.key('BASIC');self.run()
 def screenshot(self,name):
  """LCD を5倍に拡大して tests/output/name に保存する。"""
  buf=(c.c_ubyte*7680)();self.lib.e500_pixels(self.m,buf);p=ROOT/'tests/output'/name;p.parent.mkdir(exist_ok=True)
  Image.frombytes('L',(240,32),bytes(30 if x else 192 for x in buf)).resize((1200,160),Image.Resampling.NEAREST).save(p)
 def pixels(self):
  """LCD の 240×32 ドット（1 なら点灯）。"""
  buf=(c.c_ubyte*7680)();self.lib.e500_pixels(self.m,buf);return bytes(buf)
 def symbols(self,index):
  """表示シンボルの点灯ビット（index は 0・4・5 の段）。"""
  return self.lib.e500_symbols(self.m,index)
 def trace(self):
  """CE-140F のトレース。"""
  b=c.create_string_buffer(16385);self.lib.e500_disk_trace(self.m,b,len(b));return b.value.decode()
 def printed(self):
  """CE-126P が印字した行を取り出す（PC-E500 の文字コード、行は改行で終わる）。"""
  b=c.create_string_buffer(65536);n=self.lib.e500_printer_read(self.m,b,len(b));return b.raw[:n]
 def close(self):
  """コアを破棄する。"""
  self.lib.e500_destroy(self.m)
if __name__=='__main__':
 import tempfile
 with tempfile.TemporaryDirectory() as d:
  e=Emulator(disk=d);e.boot();e.type('FILES "X:"');e.screenshot('ce-files.png');print(e.trace());e.close()
