// SPDX-License-Identifier: GPL-2.0-or-later
// Memory, keyboard and LCD wiring adapted from PockEmul e500.cpp / Lcdc_e500.cpp.
// 本体（Machine）の実装と、デスクトップ側に公開する e500_* 関数（DLL の API）。
// メモリ・キーボード・LCD の配線は PockEmul の e500.cpp・Lcdc_e500.cpp から移植した。
#include "machine.h"
#include <new>
#include <cstddef>

BYTE Lcd::command(int cmd) {
    if ((cmd&0x300)==0x300) {auto v=ram[page*64+((column+63)&63)]; column=(column+1)&63;return v;}
    if (cmd&0x200) return on?0:0x20;
    if (cmd&0x100) {ram[page*64+column]=cmd;column=(column+1)&63;}
    else if ((cmd&0xc0)==0xc0) start=cmd&63;
    else if ((cmd&0xf8)==0xb8) page=cmd&7;
    else if ((cmd&0xc0)==0x40) column=cmd&63;
    else if ((cmd&0xfe)==0x3e) on=cmd&1;
    return 0;
}
// LCD の I/O：アドレスの下位4ビットで、どちらのコントローラに、何を送るかが決まる。
// 0・2：両方へコマンド・データ、4・6：コントローラ2へ、8・10：コントローラ1へ、
// 5・9：状態の読み出し、7・11：データの読み出し。
BYTE Machine::display(int cmd,BYTE d) {
    switch(cmd) {
    case 0:lcd[0].command(d);return lcd[1].command(d);
    case 2:lcd[0].command(0x100|d);return lcd[1].command(0x100|d);
    case 4:return lcd[1].command(d);
    case 6:return lcd[1].command(0x100|d);
    case 8:return lcd[0].command(d);
    case 10:return lcd[0].command(0x100|d);
    case 5:return lcd[1].command(0x200);
    case 9:return lcd[0].command(0x200);
    case 7:return lcd[1].command(0x300);
    case 11:return lcd[0].command(0x300);
    default:return 0;
    }
}
// メモリの読み出し。00000-0FFFF のうち 2000h 台（ミラーを含む）は LCD の I/O。
BYTE Machine::Get_8(UINT32 a) {
    a&=0xfffff;
    if(a==watchAddr)watchHits++;
    a=mirror(a);
    if(!readBits.empty() && (readBits[a>>3]>>(a&7)&1))readHit=a;
    if(a<0x10000 && (a&0x6000)==0x2000) return display(a&15,0);
    return mem[a];
}
void Machine::Set_8(UINT32 a,BYTE d) {
    a=mirror(a);
    if(a<0x10000 && (a&0x6000)==0x2000) {display(a&15,d);return;}
    if(isRam(a))mem[a]=d;
}
// キーボード：KOL・KOH で選ばれた行の押下状態を KI に入れる。何か押されていればキー割り込み。
// ON キー（行11 のビット1）は電源 OFF 中でも割り込みを起こし、電源を入れる。
void Machine::scanKeys() {
    int strobe=cpu.imem[IMEM_KOL]|(cpu.imem[IMEM_KOH]<<8);
    BYTE input=0;
    for(int r=0;r<12;r++) if(strobe&(1<<r)) input|=keys[r];
    cpu.imem[IMEM_KI]=input;
    if(input) cpu.imem[IMEM_ISR]|=4;
    if(keys[11]&2) {cpu.imem[IMEM_ISR]|=8;cpu.imem[IMEM_SSR]|=8;cpu.off=false;}
    else cpu.imem[IMEM_SSR]&=~8;
}
// Both SC62015 timers divide the 32.768 kHz sub-clock: the main timer to 2 Hz
// (32768/16384) and the sub timer to 64 Hz (32768/512). The sub timer sets the
// pace of the ROM key handling, so its period is audible: a key click lasts one
// sub-timer period and auto-repeat fires every four. Against a recording of a
// real PC-E500 that is a 13.5 ms click repeating every 62.5 ms; at the 500 Hz
// PockEmul uses they collapse to a 2 ms tick repeating every 16 ms.
static constexpr uint64_t MainTimerCycles=CpuHz/2,SubTimerCycles=CpuHz/64;
// Snapshots taken while the core ran at 1.024 MHz counted the timers up to these;
// they still load, with the counts folded into the periods above.
// 1.024 MHz で動いていたころの状態は、タイマーをこの値まで数えていた（読み込みでは今の周期に畳む）。
static constexpr uint64_t OldMainTimerCycles=512000,OldSubTimerCycles=16000;
// 命令にかかったサイクル数を加え、2つのタイマーの割り込み要因を立て、周辺機器を動かす。
void Csc62015::AddState(BYTE n) {
    cycles+=n;ticks+=n;ticks2+=n;
    if(ticks>=MainTimerCycles) {imem[IMEM_ISR]|=2;ticks%=MainTimerCycles;}
    if(ticks2>=SubTimerCycles) {imem[IMEM_ISR]|=1;ticks2%=SubTimerCycles;}
    pPC->peripherals(n);
}
void Machine::peripherals(BYTE n) {
    bool cassetteActive=tape.playing()||tape.recording();
    auto cassetteInput=[&]() {
        bool high=(cpu.imem[IMEM_SCR]&0x40)&&tape.inputHigh();
        cpu.imem[IMEM_SSR]=(cpu.imem[IMEM_SSR]&~2)|(high?2:0);
    };
    cassetteInput();
    // 音のモードは SCR のビット4〜6。モードが変わったら波形の位相を最初からにする。
    int mode=soundMode();
    if(mode!=lastSoundMode){tonePhase=0;lastSoundMode=mode;}
    // 48 kHz mono, sampled from the actual XOUT line (also handles software BEEP).
    // Each sample takes the level of the cycle it falls on. Holding one level for a
    // whole instruction moves every edge by up to an instruction, and a 4 kHz BEEP
    // half period is exactly six samples, so edges that land on the sample grid flip
    // side to side and detune the occasional beep into 5/7-sample half periods.
    for(BYTE i=0;i<n;i++) {
        if(cassetteActive) {
            tape.advance(cpu.cycles-n+i+1);cassetteInput();
        }
        tonePhase++;samplePhase+=48000;
        if(tape.recording())tape.observeOutput(cpu.cycles-n+i+1,toneLevel(mode));
        if(samplePhase>=CpuHz) {
            samplePhase-=CpuHz;
            if(audio.size()<48000)audio.push_back(cpu.off?0:(toneLevel(mode)?6000:-6000));
        }
    }
    // 11ピンバス：XOUT（音の出力と兼用）、BUSY（EOL のビット6）、データ（EOH）を周辺機器へ渡し、
    // ACK（EIL のビット7）とディスクのデータ線（EIH の上位4ビット）を返す。
    auto bus=busLevels(mode);xout=bus.xout;bool busy=bus.busy;
    disk.tick(cpu.cycles,xout,busy,cpu.imem[IMEM_EOH]&15);
    printer.tick(cpu.cycles,xout,busy,(cpu.imem[IMEM_EOH]&4)!=0);
    tape.setMotor(printer.connected&&printer.remoteMotor,cpu.cycles,toneLevel(mode));
    cassetteInput();
    // ACK is wired-OR: any device holding it high pulls the line high.
    cpu.imem[IMEM_EIL]=(cpu.imem[IMEM_EIL]&0x7f)|(disk.ack||printer.ack?0x80:0);
    cpu.imem[IMEM_EIH]=(cpu.imem[IMEM_EIH]&15)|(disk.lines<<4);
    serial.tick(n,!cpu.halt&&!cpu.off,cpu.imem);
}
// 内部メモリの KI を読む命令の前に、キーボードを読み直す。
void Csc62015::Chk_imemAdr_Read(BYTE d,BYTE len) {
    for(int i=0;i<len;i++) {
        if(BYTE(d+i)==IMEM_KI)pPC->scanKeys();
        pPC->serial.read(BYTE(d+i));
    }
}
void Csc62015::Chk_imemAdr(BYTE d,BYTE len,UINT32 data) {
    for(int i=0;i<len;i++)pPC->serial.write(BYTE(d+i),BYTE(data>>(8*i)),imem);
}
// HALT・電源 OFF 中は時間だけを進める。そうでなければ PC の命令を1つ実行する。
// A prefix only marks itself (tools/prepare_core.py); the instruction after it
// runs on the next step, and after that the addressing goes back to normal.
// プリフィクスは印を付けるだけ。次の step で次の命令を実行し、そのあとアドレッシングを通常に戻す。
void Csc62015::step() {
    if(halt || off) {AddState(3);return;}
    bool after=prefixed;prefixed=false;
    auto pc=reg.x.p;reg.r.pc++;OpExec(pPC->Get_PC(pc));
    if(after && !prefixed){pre_1=1;pre_2=1;}
}
// Halted or off, the machine only counts time; it did so 3 cycles a step, the
// whole of a step's work for each - most of what idling at a prompt cost. Time
// now goes on to the next timer expiry (which is what wakes it), at most 64
// cycles at a time so the devices still follow XOUT a tone at a time.
// HALT・電源 OFF の間は、次のタイマーの時刻まで（一度に 64 サイクルまで）まとめて進める。
static constexpr uint64_t IdleChunk=64;
void Machine::step(uint64_t budget) {
    if((cpu.halt || cpu.off) && !cpu.prefixed) {
        uint64_t n=std::min({budget,MainTimerCycles-cpu.ticks,SubTimerCycles-cpu.ticks2,IdleChunk});
        cpu.AddState(BYTE(std::max<uint64_t>(n,1)));
    } else cpu.step();
    // Keys change only between runs (e500_key) and the strobe only by an
    // instruction, so one scan after the step sees both; reading KI scans too.
    // キーが変わるのは実行の合間、行の選択が変わるのは命令だけなので、走査は命令のあとの1回でよい。
    scanKeys();
    // An instruction and its prefix are one: no interrupt between them.
    // プリフィクスと次の命令は1つの命令：その間には割り込まない。
    if(cpu.prefixed)return;
    // 割り込み要因（ISR）があれば HALT を解き、IMR で許可されていれば割り込む：
    // PC・F・IMR をスタックに積み、IMR の全体許可を落として割り込みベクタへ飛ぶ。
    if(cpu.off) return;
    if(cpu.imem[IMEM_ISR]) cpu.halt=false;
    BYTE mask=cpu.imem[IMEM_IMR];
    if((mask&0x80) && (mask&cpu.imem[IMEM_ISR]&0x7f)) {
        cpu.reg.x.s=(cpu.reg.x.s-5)&0xfffff;
        Set_20(cpu.reg.x.s+2,cpu.reg.x.p);Set_8(cpu.reg.x.s+1,cpu.reg.x.f);Set_8(cpu.reg.x.s,mask);
        cpu.imem[IMEM_IMR]&=0x7f;cpu.reg.x.p=Get_20(VECT_IR);
        // The RETI at its end counts the level down, as after IR.
        cpu.CallSubLevel++;
    }
}
// The debugger's step. The frames are what a call stack view shows: a CALL,
// CALLF or interrupt (CallSubLevel rose) adds one at the S that holds its return
// address, and once S is above that the return address has been taken off, by a
// RET, RETF or RETI or by a stack set anew (an error unwinding to the prompt).
// デバッガの1命令。CALL・CALLF・割り込み（CallSubLevel が増えた）で記録を積み、
// S がその戻り先より上になったら（RET・RETF・RETI、エラーでスタックを戻したとき）外す。
void Machine::debugStep(uint64_t budget) {
    UINT32 site=cpu.reg.x.p&0xfffff;int level=cpu.CallSubLevel;
    BYTE op=(cpu.halt||cpu.off)?0xff:mem[mirror(site)];
    step(budget);
    if(cpu.prefixed)step(budget);
    UINT32 sp=cpu.reg.x.s&0xfffff;
    while(!frames.empty() && frames.back().sp<sp)frames.pop_back();
    if(cpu.CallSubLevel>level) {
        // A call that never returns (a jump back to the prompt) leaves its frame
        // until S passes it; a runaway one is capped, oldest first.
        if(frames.size()>=4096)frames.erase(frames.begin());
        frames.push_back({site,cpu.reg.x.p&0xfffff,sp,UINT32(op==0x04||op==0x05?0:1)});
    }
}
// LCD のドット：左右の端（0-63・176-239）はコントローラ2、中央はコントローラ1。
// 右半分は桁の向きが逆で、表示開始行（start）だけ上下にずらして読む。
void Machine::pixels(BYTE *out) {
    for(int y=0;y<32;y++) for(int x=0;x<240;x++) {
        int controller=(x<64 || x>=176)?1:0;
        int column=x<64?x:x<120?x-64:x<176?175-x:239-x;
        auto &l=lcd[controller];int row=(y+l.start+(x>=120?32:0))&63;
        out[y*240+x]=l.on && ((l.ram[(row/8)*64+column]>>(row%8))&1);
    }
}

// ---- DLL の公開 API（デスクトップ側の Core.cs から P/Invoke で呼ぶ） ----
#define API extern "C" __declspec(dllexport)
// 本体を作る。rom：ROM イメージ、size：256KB（PC-E500）か 384KB（PC-E500S・E650・U6000）。失敗したら null。
API Machine* e500_create(const BYTE *rom,int size) {
    constexpr int MainRomSize=256*1024;
    constexpr int ExtendedRomSize=128*1024;
    if(!rom || (size!=MainRomSize && size!=MainRomSize+ExtendedRomSize))return nullptr;
    auto m=new(std::nothrow) Machine();if(!m)return nullptr;
    if(size==MainRomSize) {
        std::copy_n(rom,MainRomSize,m->mem.begin()+0xc0000);
    } else {
        // Packed PC-E500S / PC-E650 / PC-U6000 image, in ascending memory-map order:
        //   file 00000-1FFFF -> CPU 20000-3FFFF (CE3)
        //   file 20000-5FFFF -> CPU C0000-FFFFF (CE2)
        std::copy_n(rom,ExtendedRomSize,m->mem.begin()+0x20000);
        std::copy_n(rom+ExtendedRomSize,MainRomSize,m->mem.begin()+0xc0000);
        m->romKB=384;
        // The model code (FFFF1h, the last image byte but 14) tells them apart: the
        // E500S (3) carries one 62256 like the E500, the E650 (5) and U6000 (4) two.
        // 機種コード（FFFF1h）で分ける。E500S（3）は E500 と同じ 62256 が 1 個、E650（5）・U6000（4）は 2 個。
        m->standardKB=rom[size-15]==3?32:64;
        m->internalKB=m->standardKB;
    }
    // FNV-1a of the image: a snapshot says which ROM it was taken with.
    uint32_t sum=2166136261u;for(int i=0;i<size;i++)sum=(sum^rom[i])*16777619u;
    m->romSum=sum;
    m->cpu.Reset();return m;
}
// RAM の容量として正しいか（optional なら 0 = なしも可）。
bool validCapacity(int kb,bool optional=false) {return (optional && (kb==0||kb==2||kb==4))||kb==8||kb==16||kb==32||kb==64||kb==128||kb==256;}
// Internal RAM sits below C0000. The stock E500 carries one 62256 (32KB) and the
// E650/U6000 two of them (64KB, B0000-BFFFF); the well-known 628128 replacement
// swaps each 32KB SRAM for a 128KB part, so an E650/U6000 reaches 128KB or 256KB.
bool validInternal(int kb,int standardKB) {return standardKB==64?(kb==64||kb==128||kb==256):validCapacity(kb);}
// 内蔵 RAM（ram KB）と RAM カード（card KB、0 でなし）を指定して本体を作る。
API Machine* e500_create_config(const BYTE *rom,int size,int ram,int card) {
    if(!validCapacity(ram)||!validCapacity(card,true))return nullptr;
    auto m=e500_create(rom,size);if(!m)return nullptr;
    if(!validInternal(ram,m->standardKB)){delete m;return nullptr;}
    m->internalKB=ram;m->cardKB=card;return m;
}
// 内蔵 RAM（card=0）か RAM カード（card=1）の容量（KB）。
API int e500_capacity(Machine *m,int card) {return m?(card?m->cardKB:m->internalKB):0;}
// リセットボタン：CPU・LCD・キー・音声・バスを初期化し、CE-140F の開いたファイルと転送を閉じる。
// clearInternal なら内蔵 RAM も消す。
API void e500_reset(Machine *m,int clearInternal) {
    if(!m)return;
    m->tape.resetTimeline(m->cpu.cycles,0);
    if(clearInternal)std::fill(m->mem.begin()+0xc0000-m->internalKB*1024,m->mem.begin()+0xc0000,0xff);
    m->cpu=Csc62015(m);m->lcd[0]={};m->lcd[1]={};m->keys.fill(0);
    m->audio.clear();m->samplePhase=m->tonePhase=0;m->lastSoundMode=-1;m->disk.reset();m->printer.resetBus();m->serial.reset();m->cpu.Reset();
}
// 内蔵 RAM（card=0）か RAM カード（card=1）の全体を読む（write=0）・書く（write=1）。size は容量ぴったり。
API int e500_memory(Machine *m,int card,BYTE *data,int size,int write) {
    if(!m || !data)return 0;
    int length=(card?m->cardKB:m->internalKB)*1024;
    if(size!=length || !length)return 0;
    auto start=m->mem.data()+(card?0x40000:0xc0000-length);
    if(write)memcpy(start,data,length);else memcpy(data,start,length);return 1;
}
// たまった音声のサンプルを最大 size 個取り出す。戻り値は取り出した数。
API int e500_audio(Machine *m,int16_t *data,int size) {
    if(!m||!data||size<0)return 0;
    int n=std::min(size,int(m->audio.size()));
    for(int i=0;i<n;i++){data[i]=m->audio.front();m->audio.pop_front();}return n;
}
// CE-140F にフォルダを接続する（folder が null なら取り外す）。
API int e500_disk_mount(Machine *m,const wchar_t *folder,int readOnly) {
    if(!m)return 0;return m->disk.mount(folder,readOnly!=0);
}
// CE-140F の状態ビット（Ce140f::status）とトレースの取り出し。
API int e500_disk_status(Machine *m) {return m?m->disk.status():0;}
API int e500_disk_trace(Machine *m,char *out,int length) {return m?m->disk.trace(out,length):0;}
// CE-126P の接続・取り外し、状態ビット（Ce126p::status）、印字された行の取り出し。
API void e500_printer_connect(Machine *m,int on) {
    if(m) {
        m->printer.connected=on!=0;m->printer.resetBus();
        if(!on)m->printer.remoteMotor=false;
        m->tape.setMotor(m->printer.connected&&m->printer.remoteMotor,m->cpu.cycles,m->toneLevel(m->soundMode()));
        bool high=(m->cpu.imem[IMEM_SCR]&0x40)&&m->tape.inputHigh();
        m->cpu.imem[IMEM_SSR]=(m->cpu.imem[IMEM_SSR]&~2)|(high?2:0);
    }
}
API int e500_printer_status(Machine *m) {return m?m->printer.status():0;}
API int e500_printer_read(Machine *m,BYTE *out,int length) {return m?m->printer.read(out,length):0;}
// UART host endpoint. Injection is paced by configured frame length/baud rate.
API void e500_serial_connect(Machine *m,int on,int cts,int cd,int loopback) {
    if(m)m->serial.connect(on!=0,cts!=0,cd!=0,loopback!=0);
}
API int e500_serial_inject(Machine *m,const BYTE *data,int length,int status) {
    return m?m->serial.inject(data,length,BYTE(status)):0;
}
API int e500_serial_read(Machine *m,BYTE *out,int length) {
    return m?m->serial.receive(out,length):0;
}
// Physical cassette host endpoint: durations are CPU cycles and levels0/1.
// No tape bytes, headers or checksums are decoded on the emulator side.
static void tapeInput(Machine *m) {
    if(!m)return;
    m->tape.advance(m->cpu.cycles);
    bool high=(m->cpu.imem[IMEM_SCR]&0x40)&&m->tape.inputHigh();
    m->cpu.imem[IMEM_SSR]=(m->cpu.imem[IMEM_SSR]&~2)|(high?2:0);
}
API int e500_tape_connect(Machine *m,int on) {
    if(!m)return 0;
    bool okay=m->tape.connect(on!=0,m->cpu.cycles);tapeInput(m);return okay?1:0;
}
API int e500_tape_remote(Machine *m,int on) {
    if(!m||(on!=0&&on!=1))return 0;
    bool okay=m->tape.setRemote(on!=0,m->cpu.cycles);tapeInput(m);return okay?1:0;
}
static int tapePulses(Machine *m,const uint32_t *durations,const BYTE *levels,int count,bool append) {
    if(!m||count<0||count>1024*1024||(count&&(!durations||!levels)))return 0;
    try {
        std::vector<CassettePulse> pulses;pulses.reserve(size_t(count));
        for(int i=0;i<count;i++) {
            if(!durations[i]||levels[i]>1)return 0;
            pulses.push_back({durations[i],levels[i]!=0});
        }
        bool okay=append?m->tape.appendInput(pulses):m->tape.load(pulses);
        tapeInput(m);return okay?1:0;
    } catch(const std::bad_alloc&) {return 0;}
}
API int e500_tape_load(Machine *m,const uint32_t *durations,const BYTE *levels,int count) {
    return tapePulses(m,durations,levels,count,false);
}
API int e500_tape_append(Machine *m,const uint32_t *durations,const BYTE *levels,int count) {
    return tapePulses(m,durations,levels,count,true);
}
API int e500_tape_play(Machine *m) {
    if(!m)return 0;
    bool okay=m->tape.play(m->cpu.cycles);tapeInput(m);return okay?1:0;
}
API int e500_tape_record(Machine *m) {
    if(!m)return 0;
    tapeInput(m);
    return m->tape.startRecording(m->cpu.cycles,m->toneLevel(m->soundMode()))?1:0;
}
API int e500_tape_stop(Machine *m) {
    if(!m)return 0;
    bool okay=m->tape.stopRecording(m->cpu.cycles);
    okay=m->tape.stopPlayback(m->cpu.cycles)&&okay;tapeInput(m);return okay?1:0;
}
API int e500_tape_status(Machine *m) {
    if(!m)return 0;
    return (m->tape.connected()?1:0)|(m->tape.playing()?2:0)|(m->tape.recording()?4:0)|
           (m->tape.pendingInput()?8:0)|(m->tape.overflow()?16:0)|(m->tape.captured()?32:0)|
           (m->tape.remote()?64:0)|(m->tape.motor()?128:0);
}
API int e500_tape_read(Machine *m,uint32_t *durations,BYTE *levels,int count) {
    if(!m||!durations||!levels||count<0)return 0;
    int copied=0;CassettePulse pulse{};
    while(copied<count&&m->tape.read(&pulse,1)) {
        durations[copied]=pulse.cycles;levels[copied]=pulse.high?1:0;copied++;
    }
    return copied;
}
// CE-126P の1行の桁数（紙の幅を決める）。
API int e500_printer_columns() {return Ce126p::Columns;}
// 本体を破棄する。
API void e500_destroy(Machine *m) {delete m;}
// cycles サイクル分（最大 10 秒分）実行する。戻り値：0 正常、1 CPU のエラーで停止、-1 引数の誤り。
API int e500_run(Machine *m,int cycles) {
    if(!m || cycles<0 || cycles>10240000)return -1;
    auto target=m->cpu.cycles+cycles;
    m->scanKeys();
    while(m->cpu.cycles<target && !m->cpu.fault)m->step(target-m->cpu.cycles);
    return m->cpu.fault?1:0;
}
// キーを押す（down=1）・離す（down=0）。row は行（0〜11）、bit はビット（0〜7）。
API void e500_key(Machine *m,int row,int bit,int down) {
    if(!m || row<0 || row>=12 || bit<0 || bit>=8)return;
    if(down)m->keys[row]|=1<<bit;else m->keys[row]&=~(1<<bit);
    m->scanKeys();
}
// すべてのキーを離す。
API void e500_release_keys(Machine *m) {if(m){m->keys.fill(0);m->scanKeys();}}
// LCD の 240×32 ドットを取り出す。
API void e500_pixels(Machine *m,BYTE *out) {if(m && out)m->pixels(out);}
// CPU のレジスタの値（id は REGNAME）。
API UINT32 e500_reg(Machine *m,int id) {return m?m->cpu.get_reg(REGNAME(id)):0;}
// CPU の状態：1 電源 OFF、2 HALT 中、4 エラーで停止（本体がなければ 4）。
API int e500_status(Machine *m) {return m?(m->cpu.off?1:0)|(m->cpu.halt?2:0)|(m->cpu.fault?4:0):4;}
// LCD の表示シンボル（BUSY・RUN・DEG など）の点灯ビット。コントローラ1 の第63桁に並んでいる。
API int e500_symbols(Machine *m,int index) {
    if(!m || index<0 || index>7 || !m->lcd[0].on)return 0;
    // The legends are in controller 1, so its own start line shifts their page.
    return m->lcd[0].ram[((index+m->lcd[0].start/8)&7)*64+63];
}
// デバッガのウォッチ：アドレスを指定し、読まれた回数を数える。
API void e500_watch(Machine *m,int address) {if(m){m->watchAddr=address;m->watchHits=0;}}
API int e500_watch_hits(Machine *m) {return m?(int)m->watchHits:0;}
// A look at the bus that changes nothing: the LCD answers its status, a data
// port shows the byte at the column without moving it, a write port reads 0,
// and the debugger's watch does not count the look as a read.
// バスの1バイトを、何も変えずに読む（LCD は状態を返し、データの読み出しは桁を進めず、書き込み用のポートは 0。ウォッチにも数えない）。
API int e500_peek(Machine *m,int address) {
    if(!m)return 0;
    UINT32 a=m->mirror(UINT32(address)&0xfffff);
    if(a<0x10000 && (a&0x6000)==0x2000) {
        int port=a&15;auto &l=m->lcd[port==5||port==7?1:0];
        if(port==5||port==9)return l.on?0:0x20;
        if(port==7||port==11)return l.ram[l.page*64+((l.column+63)&63)];
        return 0;
    }
    return m->mem[a];
}
// Debugger reads. Get_8 answers the LCD controller inside 2000-3FFF, which
// advances its column counter, so a viewer has to read the bus behind it.
// デバッガの読み出し：LCD の I/O に触れずに、メモリの中身をそのまま length バイト読む。
API int e500_dump(Machine *m,int address,BYTE *out,int length) {
    if(!m || !out || address<0 || length<=0 || address>0xfffff || length>0x100000-address)return 0;
    for(int i=0;i<length;i++)out[i]=m->mem[m->mirror(address+i)];
    return 1;
}
// Debugger writes. Every byte has to land in RAM (internal or card, mirrors
// included) or nothing is written, so a binary that runs past the end of the
// card cannot half-load.
// デバッガの書き込み：範囲がすべて RAM のときだけ書く（一部でも RAM 以外なら何も書かずに 0）。
API int e500_write(Machine *m,int address,const BYTE *data,int length) {
    if(!m || !data || address<0 || length<=0 || address>0xfffff || length>0x100000-address)return 0;
    // Each byte must land on its own: past the end of the RAM it would wrap round
    // the mirror and overwrite the start of the same write.
    // 1バイトずつ別の場所に入ること（RAM の大きさを超えると、ミラーで折り返して自分の先頭を上書きする）。
    UINT32 previous=0;
    for(int i=0;i<length;i++) {
        UINT32 physical=m->mirror(address+i);
        if(!m->isRam(physical))return 0;
        if(i && physical!=previous+1) {
            // A joined logical range may cross CE1 -> CE0 once. This is
            // distinct RAM, not a card/internal mirror wrapping onto itself.
            bool joinedBoundary=(m->cpu.imem[IMEM_AMC]&0x80) &&
                UINT32(address+i)==m->amcBase() && previous>=0x40000 &&
                previous<0x80000 && physical>=0x80000 && physical<0xc0000;
            if(!joinedBoundary)return 0;
        }
        previous=physical;
    }
    for(int i=0;i<length;i++)m->mem[m->mirror(address+i)]=data[i];
    return 1;
}
// CPU の内部メモリ（256バイト）を取り出す。
API int e500_imem(Machine *m,BYTE *out) {
    if(!m || !out)return 0;
    std::copy_n(m->cpu.imem,256,out);return 1;
}
// 起動からの CPU のサイクル数。
API uint64_t e500_cycles(Machine *m) {return m?m->cpu.cycles:0;}

// ---- デバッガ（VS Code のデバッグサーバー e500dbg が使う） ----
// レジスタを書く（id は REGNAME。P・S などは 20 ビット、BA・I は 16 ビット、A・IL・F は 8 ビット）。
API void e500_set_reg(Machine *m,int id,UINT32 value) {
    if(!m || id<0 || id>REG_F)return;
    switch(REGNAME(id)) {
    case REG_A:case REG_IL:case REG_F:value&=0xff;break;
    case REG_BA:case REG_I:value&=0xffff;break;
    case REG_PC:return;
    default:value&=0xfffff;break;
    }
    m->cpu.set_reg(REGNAME(id),value);
}
// ブレークポイントを list の count 個にする（0 個で全部外す）。
API void e500_debug_breakpoints(Machine *m,const uint32_t *list,int count) {
    if(!m)return;
    if(!list || count<=0){m->breakBits.clear();m->breakBits.shrink_to_fit();return;}
    m->breakBits.assign(0x100000/8,0);
    for(int i=0;i<count;i++){UINT32 a=list[i]&0xfffff;m->breakBits[a>>3]|=BYTE(1<<(a&7));}
}
// Runs like e500_run, but looks for a breakpoint before every instruction
// (flags bit 0: not before the first, to go on from one) and keeps the frames.
// depth >= 0 stops once fewer frames than that are left: a step out, or the
// rest of a step over a call. 0 ran the cycles, 1 undefined instruction,
// 2 at a breakpoint (not yet run), 3 returned to the depth, 4 an instruction read
// a watched byte (it has run; e500_debug_read_hit says which), -1 bad arguments.
// e500_run と同じに実行し、命令の前ごとにブレークポイントを見る（flags の bit0：最初の命令では見ない）。
// depth が 0 以上なら、呼び出しの記録がそれより少なくなったら止まる（ステップアウト、CALL のステップオーバー）。
// 戻り値 0：サイクルを実行した、1：未定義命令、2：ブレークポイント（その命令はまだ）、3：depth に戻った、
// 4：命令が読み出しウォッチの番地を読んだ（その命令は実行済み。番地は e500_debug_read_hit）、-1：引数が違う。
API int e500_debug_run(Machine *m,int cycles,int flags,int depth) {
    if(!m || cycles<0 || cycles>10240000)return -1;
    auto target=m->cpu.cycles+cycles;
    m->scanKeys();
    bool first=true;
    m->readHit=0xffffffff;
    while(m->cpu.cycles<target) {
        if(m->cpu.fault)return 1;
        bool running=!(m->cpu.halt||m->cpu.off) || m->cpu.prefixed;
        if(running && !(first && (flags&1)) && m->isBreak(m->cpu.reg.x.p))return 2;
        first=false;
        m->debugStep(target-m->cpu.cycles);
        if(m->readHit!=0xffffffff) {
            // A read from elsewhere than the filter's place goes by unseen.
            if(m->readFilter==0xffffffff || (m->cpu.reg.x.p&0xfffff)==m->readFilter)return m->cpu.fault?1:4;
            m->readHit=0xffffffff;
        }
        if(depth>=0 && int(m->frames.size())<depth)return m->cpu.fault?1:3;
    }
    return m->cpu.fault?1:0;
}
// 1命令だけ実行する（HALT 中なら時間を少し進める）。戻り値は 0、未定義命令なら 1。
API int e500_debug_step(Machine *m) {
    if(!m)return -1;
    if(m->cpu.fault)return 1;
    m->scanKeys();m->debugStep();
    return m->cpu.fault?1:0;
}
// 呼び出しの記録を out に（1つ 4 語：呼んだ番地・飛び先・S・割り込みなら 1）古い順に max 個まで書き、全部の数を返す。
API int e500_debug_frames(Machine *m,uint32_t *out,int max) {
    if(!m)return 0;
    int n=int(m->frames.size());
    for(int i=0;out && i<n && i<max;i++) {
        auto &f=m->frames[i];out[i*4]=f.site;out[i*4+1]=f.target;out[i*4+2]=f.sp;out[i*4+3]=f.interrupt;
    }
    return n;
}
// Calls entry as CALLF (far) or CALL would from returnAddress: the return
// address goes on the S stack (3 bytes or the low 2), the CPU leaves HALT,
// and the call is the newest frame. The debug server stops at returnAddress.
// entry を呼ぶ：戻り先 returnAddress を S に積み（far なら 3 バイト、そうでなければ下位 2 バイト）、HALT を解いて飛ぶ。
API int e500_debug_call(Machine *m,int entry,int returnAddress,int far) {
    if(!m || m->cpu.off || m->cpu.fault)return 0;
    auto &r=m->cpu.reg.x;
    if(far){r.s=(r.s-3)&0xfffff;m->Set_20(r.s,UINT32(returnAddress)&0xfffff);}
    else{r.s=(r.s-2)&0xfffff;m->Set_16(r.s,WORD(returnAddress));}
    r.p=UINT32(entry)&0xfffff;m->cpu.halt=false;
    m->frames.push_back({UINT32(returnAddress)&0xfffff,r.p,r.s,0});
    return 1;
}
// 呼び出しの記録を消す。
API void e500_debug_clear_frames(Machine *m) {if(m)m->frames.clear();}
// 読み出しウォッチを list の count 個の番地にする（0 個で全部外す）。番地はミラーを含めた実際の格納先で覚える。
// after が 0 以上なら、読んだ命令のあとの P がその番地のときだけ止まる（BASIC の行の始まりだけを捕まえる）。
API void e500_debug_watch_reads(Machine *m,const uint32_t *list,int count,int after) {
    if(!m)return;
    m->readHit=0xffffffff;
    m->readFilter=after<0?0xffffffff:UINT32(after)&0xfffff;
    if(!list || count<=0){m->readBits.clear();m->readBits.shrink_to_fit();return;}
    m->readBits.assign(0x100000/8,0);
    for(int i=0;i<count;i++){UINT32 a=m->mirror(list[i]&0xfffff);m->readBits[a>>3]|=BYTE(1<<(a&7));}
}
// 最後の e500_debug_run を止めた読み出しの番地（実際の格納先。なければ -1）。
API int e500_debug_read_hit(Machine *m) {return m && m->readHit!=0xffffffff?int(m->readHit):-1;}
// Debugger writes to the internal RAM: 00h-EEh, the plain bytes and BP, PX, PY. The
// AMC (EFh) and the I/O from F0h act when written, so they are refused.
// デバッガの内部メモリの書き込み：00h〜EEh（普通の所と BP・PX・PY）だけ。AMC（EFh）と F0h からの I/O は、書くと働くので断る。
// 範囲の外を含めば何も書かずに 0。
API int e500_debug_write_imem(Machine *m,int at,const BYTE *data,int length) {
    if(!m || !data || at<0 || length<=0 || length>IMEM_AMC || at>IMEM_AMC-length)return 0;
    std::copy_n(data,length,m->cpu.imem+at);return 1;
}
// HALT の状態を戻す（e500_debug_call の前に HALT していた本体を、戻ったあとで元のとおり待たせる）。
API void e500_debug_halt(Machine *m,int on) {if(m && !m->cpu.off)m->cpu.halt=on!=0;}

// ---- 状態保存（スナップショット） ----
// 固定長のバイナリ。形式は先頭8バイトの magic（E500ST01・02・04）で区別する。
// Fixed-layout snapshot, local little-endian Windows x64 format v1.
struct LegacySnapshot {
    char magic[8];
    SCREG reg;
    BYTE imem[256],ram[32768];
    Lcd lcd[2];
    BYTE pre1,pre2,halt,off;
    uint64_t cycles,ticks,ticks2;
};

// Format v2: the machine without the printer, which v3 appends.
// 形式2：内蔵 RAM とカードの容量、CPU、512KB のメモリ（40000-BFFFF）、LCD、タイマー。
struct SnapshotV2 {
    char magic[8];
    uint32_t internalKB,cardKB;
    SCREG reg;
    BYTE imem[256],memory[0x80000];
    Lcd lcd[2];
    BYTE pre1,pre2,halt,off;
    uint64_t cycles,ticks,ticks2;
};
// v4 adds the printer and the CE-140F's view of a device code going by (v3 was
// never released). The card capacity stays at offset 12, where the desktop reads it.
// 形式4：形式2 に、プリンタとディスクのバス上の状態を加えたもの。
struct Snapshot:SnapshotV2 {
    Ce126p::State printer;
    Ce140f::CodeState disk;
};
// v5 says which ROM it was taken with, so the native load itself refuses
// another model's or another ROM's machine, and keeps a prefix whose
// instruction has yet to run (a prefix no longer runs it within its own step).
// 形式5：形式4 に、ROM（容量とチェックサム）と、プリフィクスの途中かを加えたもの。
struct SnapshotV5:Snapshot {
    uint32_t romKB,romSum;
    BYTE prefixed,reserved[7];
};
// 今の形式のスナップショットの大きさ（バイト）。
API int e500_state_size() {return sizeof(SnapshotV5);}
// The card capacity a snapshot was taken with, read here so that the desktop
// knows nothing of the layouts: -1 for data in no layout this build reads.
// 状態のデータに記録された RAM カードの容量（KB、なしは 0）。知らない形式なら -1。
// 形式の知識をここだけに置き、デスクトップ側は持たない。
API int e500_state_card(const BYTE *data,int size) {
    if(!data)return -1;
    if(size==int(sizeof(LegacySnapshot)))return memcmp(data,"E500ST01",8)?-1:0;
    const char *magic=size==int(sizeof(SnapshotV5))?"E500ST05":size==int(sizeof(Snapshot))?"E500ST04":size==int(sizeof(SnapshotV2))?"E500ST02":nullptr;
    if(!magic || memcmp(data,magic,8))return -1;
    uint32_t card;memcpy(&card,data+offsetof(SnapshotV2,cardKB),sizeof(card));
    return validCapacity(int(card),true)?int(card):-1;
}
// Whether two current snapshots hold the same machine, apart from the time it
// has run (the cycle count and the timers' progress): a machine left off, or
// halted with nothing changing, need not be written again.
// 2つの状態が、動いた時間（サイクル数とタイマーの経過）のほかは同じか。同じなら書き直さなくてよい。
API int e500_state_same(const BYTE *a,const BYTE *b,int size) {
    if(!a || !b || size!=int(sizeof(SnapshotV5)))return 0;
    auto x=std::make_unique<SnapshotV5>(),y=std::make_unique<SnapshotV5>();
    memcpy(x.get(),a,sizeof(SnapshotV5));memcpy(y.get(),b,sizeof(SnapshotV5));
    x->cycles=y->cycles=x->ticks=y->ticks=x->ticks2=y->ticks2=0;
    return memcmp(x.get(),y.get(),sizeof(SnapshotV5))==0;
}
// The one place that says when a snapshot may be taken or restored; the
// desktop asks these too instead of keeping its own copy of the rule. The
// printer, and the CE-140F's part in its codes, go into the snapshot, so the
// printer never holds one off.
// 状態保存してよいか（CPU のエラー中や CE-140F の転送中・ファイルを開いている間は不可）。
API int e500_can_save(Machine *m) {return m && !m->cpu.fault && m->disk.canSnapshot() && m->serial.quiescent() && !m->tape.busy();}
// Restoring cannot resume a CE-140F transfer or open file.
// 状態を読み込んでよいか（CE-140F の転送中・ファイルを開いている間は不可）。
API int e500_can_load(Machine *m) {return m && m->disk.canSnapshot() && m->serial.quiescent() && !m->tape.busy();}
// 状態を out に保存する。size は e500_state_size と同じであること。保存できない状態なら 0。
API int e500_save(Machine *m,BYTE *out,int size) {
    if(!out || size!=sizeof(SnapshotV5) || !e500_can_save(m))return 0;
    auto holder=std::make_unique<SnapshotV5>();auto &s=*holder;
    memcpy(s.magic,"E500ST05",8);s.romKB=uint32_t(m->romKB);s.romSum=m->romSum;s.prefixed=m->cpu.prefixed;memset(s.reserved,0,sizeof(s.reserved));s.internalKB=m->internalKB;s.cardKB=m->cardKB;s.reg=m->cpu.reg;
    memcpy(s.imem,m->cpu.imem,256);memcpy(s.memory,m->mem.data()+0x40000,0x80000);
    s.lcd[0]=m->lcd[0];s.lcd[1]=m->lcd[1];
    s.pre1=m->cpu.pre_1;s.pre2=m->cpu.pre_2;s.halt=m->cpu.halt;s.off=m->cpu.off;
    s.cycles=m->cpu.cycles;s.ticks=m->cpu.ticks;s.ticks2=m->cpu.ticks2;s.printer=m->printer.save();s.disk=m->disk.saveCode();
    memcpy(out,&s,sizeof(s));return 1;
}
// 状態を読み込む。古い形式（ST01・ST02）は今の形式に直してから読む。
// すべて検査してから書き換えるので、不正なデータなら何も変えずに 0 を返す。
API int e500_load(Machine *m,const BYTE *data,int size) {
    if(!data || !e500_can_load(m))return 0;
    auto holder=std::make_unique<SnapshotV5>();auto &s=*holder;
    // Older formats have no ROM to compare (the desktop's file checks it) and no prefix pending.
    s.romKB=uint32_t(m->romKB);s.romSum=m->romSum;s.prefixed=0;memset(s.reserved,0,sizeof(s.reserved));
    // Older formats come from before what they lack, which was idle when they could be taken.
    s.printer=Ce126p().save();s.disk=Ce140f().saveCode();
    if(size==sizeof(LegacySnapshot)) {
        LegacySnapshot old;memcpy(&old,data,sizeof(old));if(memcmp(old.magic,"E500ST01",8))return 0;
        memcpy(s.magic,"E500ST05",8);s.internalKB=32;s.cardKB=0;s.reg=old.reg;memcpy(s.imem,old.imem,256);
        memset(s.memory,0xff,sizeof(s.memory));memcpy(s.memory+0x78000,old.ram,32768);
        s.lcd[0]=old.lcd[0];s.lcd[1]=old.lcd[1];s.pre1=old.pre1;s.pre2=old.pre2;s.halt=old.halt;s.off=old.off;
        s.cycles=old.cycles;s.ticks=old.ticks;s.ticks2=old.ticks2;
    } else if(size==sizeof(SnapshotV2)) {
        memcpy(static_cast<SnapshotV2*>(&s),data,sizeof(SnapshotV2));
        if(memcmp(s.magic,"E500ST02",8))return 0;
        memcpy(s.magic,"E500ST05",8);
    } else if(size==sizeof(Snapshot)) {
        memcpy(static_cast<Snapshot*>(&s),data,sizeof(Snapshot));
        if(memcmp(s.magic,"E500ST04",8))return 0;
        memcpy(s.magic,"E500ST05",8);
    } else if(size==sizeof(s))memcpy(&s,data,sizeof(s));else return 0;
    // Between steps the addressing is normal unless a prefix's instruction is
    // still to run; any other value would send every later access astray.
    // 状態は、プリフィクスの途中でなければアドレッシングは通常（1）。ほかの値ならあとの読み書きがずれる。
    bool prefixOk=s.prefixed?(s.prefixed==1 && s.pre1<=3 && s.pre2<=3 && !s.halt):(s.pre1==1 && s.pre2==1);
    if(memcmp(s.magic,"E500ST05",8) || s.romKB!=uint32_t(m->romKB) || s.romSum!=m->romSum || !prefixOk ||
       std::any_of(std::begin(s.reserved),std::end(s.reserved),[](BYTE b){return b!=0;}) ||
       !validInternal(s.internalKB,m->standardKB) || !validCapacity(s.cardKB,true) ||
       s.halt>1 || s.off>1 || s.reg.x.p>0xfffff ||
       s.ticks>=OldMainTimerCycles || s.ticks2>=OldSubTimerCycles || s.cycles>UINT64_MAX-10240000)return 0;
    for(auto &l:s.lcd)if(l.page>7 || l.column>63 || l.start>63 || l.on>1)return 0;
    // Checked before anything changes, so a bad one leaves the machine as it was;
    // the restores below then cannot fail.
    // The two devices see the same lines, so their levels agree (an older
    // snapshot has none, all zero).
    // 2つの機器は同じ線を見ているので、線の状態は一致していなければならない。
    if(!Ce126p::valid(s.printer,s.cycles) || !Ce140f::validCode(s.disk,s.cycles) ||
       s.printer.oldXout!=s.disk.oldXout || s.printer.oldBusy!=s.disk.oldBusy)return 0;
    m->tape.resetTimeline(m->cpu.cycles,s.cycles);
    m->internalKB=s.internalKB;m->cardKB=s.cardKB;m->cpu.reg=s.reg;
    memcpy(m->cpu.imem,s.imem,256);memcpy(m->mem.data()+0x40000,s.memory,0x80000);
    m->lcd[0]=s.lcd[0];m->lcd[1]=s.lcd[1];m->cpu.pre_1=s.pre1;m->cpu.pre_2=s.pre2;m->cpu.prefixed=s.prefixed!=0;
    m->cpu.halt=s.halt;m->cpu.off=s.off;m->cpu.fault=false;
    m->cpu.cycles=s.cycles;m->cpu.ticks=s.ticks%MainTimerCycles;m->cpu.ticks2=s.ticks2%SubTimerCycles;m->keys.fill(0);
    m->audio.clear();m->samplePhase=0;m->serial.reset();
    // An older snapshot has no levels for the lines: the tone starts from its
    // beginning and they are taken from the loaded ports, so XOUT high in it is
    // not seen to rise on the first step. A current one has them, and the tone
    // goes on from the saved level.
    // 古い形式には線の状態がないので、音は最初から始め、線の状態は読み込んだポートから決める。
    // 今の形式では、保存した XOUT の値から音を続ける（どちらも、偽の立ち上がりを作らないため）。
    if(size!=int(sizeof(Snapshot)) && size!=int(sizeof(SnapshotV5))) {
        m->lastSoundMode=m->soundMode();m->tonePhase=0;
        auto bus=m->busLevels(m->lastSoundMode);
        s.printer.oldXout=s.disk.oldXout=bus.xout;s.printer.oldBusy=s.disk.oldBusy=bus.busy;
    } else m->restartTone(s.printer.oldXout!=0);
    m->disk.restoreCode(s.disk,s.cycles);m->printer.restore(s.printer,s.cycles);m->scanKeys();return 1;
}
