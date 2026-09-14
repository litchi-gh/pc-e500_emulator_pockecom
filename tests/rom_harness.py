from pathlib import Path
import ctypes as c
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
MATRIX=['TEXT Q MENU A BASIC Z SHIFT CTRL','W E S D X C CAPS KANA','R T F G V B SPACE DOWN','Y U H J N M UP LEFT','I O K L , ; RIGHT ENTER','RCL HYP HEX EXP 7 4 1 0','STO SIN DEG ^ 8 5 2 +/-','C-CE COS LN SQRT 9 6 3 .','STAT TAN LOG SQUARE / * - +',') FSE INV ( DEL BS INS =','P 2ndF F5 F4 F3 F2 F1','OFF ON/BRK']
KEYS={s:(i,j) for i,row in enumerate(MATRIX) for j,s in enumerate(row.split())}
SHIFT={'"':'W',':':';','!':'Q','<':'I','>':'O','$':'R','?':',','#':'E'}
class Emulator:
 def __init__(self,ram=32,card=0,disk=None,rom=None):
  self.capture=None
  self.lib=c.CDLL(str(ROOT/'bin/e500core.dll'));l=self.lib
  for n,types,result in [('create_config',[c.c_void_p,c.c_int,c.c_int,c.c_int],c.c_void_p),('run',[c.c_void_p,c.c_int],c.c_int),('key',[c.c_void_p,c.c_int,c.c_int,c.c_int],None),('pixels',[c.c_void_p,c.c_void_p],None),('reg',[c.c_void_p,c.c_int],c.c_uint32),('disk_mount',[c.c_void_p,c.c_wchar_p,c.c_int],c.c_int),('disk_trace',[c.c_void_p,c.c_void_p,c.c_int],c.c_int),('peek',[c.c_void_p,c.c_int],c.c_int),('audio',[c.c_void_p,c.c_void_p,c.c_int],c.c_int),('reset',[c.c_void_p,c.c_int],None),('imem',[c.c_void_p,c.c_void_p],c.c_int),('cycles',[c.c_void_p],c.c_uint64),('destroy',[c.c_void_p],None)]:
   f=getattr(l,'e500_'+n);f.argtypes=types;f.restype=result
  rom_path=Path(rom) if rom else ROOT/'app'/'rom'/'PC-E500.rom'
  rom_data=rom_path.read_bytes();self.m=l.e500_create_config(rom_data,len(rom_data),ram,card)
  if not self.m:raise ValueError(f'Unsupported ROM image: {rom_path} ({len(rom_data)} bytes)')
  if disk: l.e500_disk_mount(self.m,str(Path(disk).resolve()),0)
 def run(self,cycles=1024000):
  while cycles>0:
   n=min(cycles,10240000);r=self.lib.e500_run(self.m,n);cycles-=n
   if r:raise RuntimeError(f'CPU stopped PC {self.lib.e500_reg(self.m,9):05x}')
   audio=(c.c_int16*48000)();count=self.lib.e500_audio(self.m,audio,48000)
   if self.capture is not None:self.capture.extend(audio[:count])
 def key(self,key):
  r,b=KEYS[key];self.lib.e500_key(self.m,r,b,1);self.run(150000);self.lib.e500_key(self.m,r,b,0);self.run(180000)
 def type(self,text):
  for ch in text:
   if ch in SHIFT:
    r,b=KEYS['SHIFT'];self.lib.e500_key(self.m,r,b,1);self.run(20000);self.key(SHIFT[ch]);self.lib.e500_key(self.m,r,b,0);self.run(20000)
   else:self.key('SPACE' if ch==' ' else ch.upper())
  self.key('ENTER');self.run(2048000)
 def boot(self):
  self.run();self.key('F1');self.run();self.key('BASIC');self.run()
 def screenshot(self,name):
  buf=(c.c_ubyte*7680)();self.lib.e500_pixels(self.m,buf);p=ROOT/'tests/output'/name;p.parent.mkdir(exist_ok=True)
  Image.frombytes('L',(240,32),bytes(30 if x else 192 for x in buf)).resize((1200,160),Image.Resampling.NEAREST).save(p)
 def pixels(self):
  buf=(c.c_ubyte*7680)();self.lib.e500_pixels(self.m,buf);return bytes(buf)
 def trace(self):
  b=c.create_string_buffer(16385);self.lib.e500_disk_trace(self.m,b,len(b));return b.value.decode()
 def close(self):self.lib.e500_destroy(self.m)
if __name__=='__main__':
 import tempfile
 with tempfile.TemporaryDirectory() as d:
  e=Emulator(disk=d);e.boot();e.type('FILES "X:"');e.screenshot('ce-files.png');print(e.trace());e.close()
