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
static constexpr uint64_t MainTimerCycles=512000,SubTimerCycles=16000;
// 命令にかかったサイクル数を加え、2つのタイマーの割り込み要因を立て、周辺機器を動かす。
void Csc62015::AddState(BYTE n) {
    cycles+=n;ticks+=n;ticks2+=n;
    if(ticks>=MainTimerCycles) {imem[IMEM_ISR]|=2;ticks%=MainTimerCycles;}
    if(ticks2>=SubTimerCycles) {imem[IMEM_ISR]|=1;ticks2%=SubTimerCycles;}
    pPC->peripherals(n);
}
void Machine::peripherals(BYTE n) {
    // 音のモードは SCR のビット4〜6。モードが変わったら波形の位相を最初からにする。
    int mode=soundMode();
    if(mode!=lastSoundMode){tonePhase=0;lastSoundMode=mode;}
    // 48 kHz mono, sampled from the actual XOUT line (also handles software BEEP).
    // Each sample takes the level of the cycle it falls on. Holding one level for a
    // whole instruction moves every edge by up to an instruction, and a 4 kHz BEEP
    // half period is exactly six samples, so edges that land on the sample grid flip
    // side to side and detune the occasional beep into 5/7-sample half periods.
    for(BYTE i=0;i<n;i++) {
        tonePhase++;samplePhase+=48000;
        if(samplePhase>=1024000) {
            samplePhase-=1024000;
            if(audio.size()<48000)audio.push_back(cpu.off?0:(toneLevel(mode)?6000:-6000));
        }
    }
    // 11ピンバス：XOUT（音の出力と兼用）、BUSY（EOL のビット6）、データ（EOH）を周辺機器へ渡し、
    // ACK（EIL のビット7）とディスクのデータ線（EIH の上位4ビット）を返す。
    auto bus=busLevels(mode);xout=bus.xout;bool busy=bus.busy;
    disk.portL=cpu.imem[IMEM_EOL];disk.portH=cpu.imem[IMEM_EOH];
    disk.tick(cpu.cycles,xout,busy,cpu.imem[IMEM_EOH]&15);
    printer.tick(cpu.cycles,xout,busy,(cpu.imem[IMEM_EOH]&4)!=0);
    // ACK is wired-OR: any device holding it high pulls the line high.
    cpu.imem[IMEM_EIL]=(cpu.imem[IMEM_EIL]&0x7f)|(disk.ack||printer.ack?0x80:0);
    cpu.imem[IMEM_EIH]=(cpu.imem[IMEM_EIH]&15)|(disk.lines<<4);
}
// 内部メモリの KI を読む命令の前に、キーボードを読み直す。
void Csc62015::Chk_imemAdr_Read(BYTE d,BYTE len) {
    for(int i=0;i<len;i++) if(BYTE(d+i)==IMEM_KI) pPC->scanKeys();
}
// HALT・電源 OFF 中は時間だけを進める。そうでなければ PC の命令を1つ実行する。
void Csc62015::step() {
    if(halt || off) {AddState(3);return;}
    auto pc=reg.x.p;reg.r.pc++;OpExec(pPC->Get_PC(pc));
}
void Machine::step() {
    scanKeys();cpu.step();scanKeys();
    // 割り込み要因（ISR）があれば HALT を解き、IMR で許可されていれば割り込む：
    // PC・F・IMR をスタックに積み、IMR の全体許可を落として割り込みベクタへ飛ぶ。
    if(cpu.off) return;
    if(cpu.imem[IMEM_ISR]) cpu.halt=false;
    BYTE mask=cpu.imem[IMEM_IMR];
    if((mask&0x80) && (mask&cpu.imem[IMEM_ISR]&0x7f)) {
        cpu.reg.x.s=(cpu.reg.x.s-5)&0xfffff;
        Set_20(cpu.reg.x.s+2,cpu.reg.x.p);Set_8(cpu.reg.x.s+1,cpu.reg.x.f);Set_8(cpu.reg.x.s,mask);
        cpu.imem[IMEM_IMR]&=0x7f;cpu.reg.x.p=Get_20(VECT_IR);
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
// 本体を作る。rom：ROM イメージ、size：256KB（PC-E500）か 384KB（PC-E650・U6000）。失敗したら null。
API Machine* e500_create(const BYTE *rom,int size) {
    constexpr int MainRomSize=256*1024;
    constexpr int ExtendedRomSize=128*1024;
    if(!rom || (size!=MainRomSize && size!=MainRomSize+ExtendedRomSize))return nullptr;
    auto m=new(std::nothrow) Machine();if(!m)return nullptr;
    if(size==MainRomSize) {
        std::copy_n(rom,MainRomSize,m->mem.begin()+0xc0000);
    } else {
        // Packed PC-E650 / PC-U6000 image, in ascending memory-map order:
        //   file 00000-1FFFF -> CPU 20000-3FFFF (CE3)
        //   file 20000-5FFFF -> CPU C0000-FFFFF (CE2)
        std::copy_n(rom,ExtendedRomSize,m->mem.begin()+0x20000);
        std::copy_n(rom+ExtendedRomSize,MainRomSize,m->mem.begin()+0xc0000);
        m->romKB=384;
        m->internalKB=64;
    }
    m->cpu.Reset();return m;
}
// RAM の容量として正しいか（optional なら 0 = なしも可）。
bool validCapacity(int kb,bool optional=false) {return (optional && kb==0)||kb==8||kb==16||kb==32||kb==64||kb==128||kb==256;}
// Internal RAM sits below C0000. The stock E500 carries one 62256 (32KB) and the
// E650/U6000 two of them (64KB, B0000-BFFFF); the well-known 628128 replacement
// swaps each 32KB SRAM for a 128KB part, so an E650/U6000 reaches 128KB or 256KB.
bool validInternal(int kb,int romKB) {return romKB==384?(kb==64||kb==128||kb==256):validCapacity(kb);}
// 内蔵 RAM（ram KB）と RAM カード（card KB、0 でなし）を指定して本体を作る。
API Machine* e500_create_config(const BYTE *rom,int size,int ram,int card) {
    if(!validCapacity(ram)||!validCapacity(card,true))return nullptr;
    auto m=e500_create(rom,size);if(!m)return nullptr;
    if(!validInternal(ram,m->romKB)){delete m;return nullptr;}
    m->internalKB=ram;m->cardKB=card;return m;
}
// 内蔵 RAM（card=0）か RAM カード（card=1）の容量（KB）。
API int e500_capacity(Machine *m,int card) {return m?(card?m->cardKB:m->internalKB):0;}
// リセットボタン：CPU・LCD・キー・音声・バスを初期化し、CE-140F の開いたファイルと転送を閉じる。
// clearInternal なら内蔵 RAM も消す。
API void e500_reset(Machine *m,int clearInternal) {
    if(!m)return;
    if(clearInternal)std::fill(m->mem.begin()+0xc0000-m->internalKB*1024,m->mem.begin()+0xc0000,0xff);
    m->cpu=Csc62015(m);m->lcd[0]={};m->lcd[1]={};m->keys.fill(0);
    m->audio.clear();m->samplePhase=m->tonePhase=0;m->lastSoundMode=-1;m->disk.reset();m->printer.resetBus();m->cpu.Reset();
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
API void e500_printer_connect(Machine *m,int on) {if(m){m->printer.connected=on!=0;m->printer.resetBus();}}
API int e500_printer_status(Machine *m) {return m?m->printer.status():0;}
API int e500_printer_read(Machine *m,BYTE *out,int length) {return m?m->printer.read(out,length):0;}
// CE-126P の1行の桁数（紙の幅を決める）。
API int e500_printer_columns() {return Ce126p::Columns;}
// 本体を破棄する。
API void e500_destroy(Machine *m) {delete m;}
// cycles サイクル分（最大 10 秒分）実行する。戻り値：0 正常、1 CPU のエラーで停止、-1 引数の誤り。
API int e500_run(Machine *m,int cycles) {
    if(!m || cycles<0 || cycles>10240000)return -1;
    auto target=m->cpu.cycles+cycles;
    while(m->cpu.cycles<target && !m->cpu.fault)m->step();
    return m->cpu.fault?1:0;
}
// キーを押す（down=1）・離す（down=0）。row は行（0〜11）、bit はビット（0〜7）。
API void e500_key(Machine *m,int row,int bit,int down) {
    if(!m || row<0 || row>=12 || bit<0 || bit>=8)return;
    if(down)m->keys[row]|=1<<bit;else m->keys[row]&=~(1<<bit);
}
// すべてのキーを離す。
API void e500_release_keys(Machine *m) {if(m)m->keys.fill(0);}
// LCD の 240×32 ドットを取り出す。
API void e500_pixels(Machine *m,BYTE *out) {if(m && out)m->pixels(out);}
// CPU のレジスタの値（id は REGNAME）。
API UINT32 e500_reg(Machine *m,int id) {return m?m->cpu.get_reg(REGNAME(id)):0;}
// CPU の状態：1 電源 OFF、2 HALT 中、4 エラーで停止（本体がなければ 4）。
API int e500_status(Machine *m) {return m?(m->cpu.off?1:0)|(m->cpu.halt?2:0)|(m->cpu.fault?4:0):4;}
// LCD の表示シンボル（BUSY・RUN・DEG など）の点灯ビット。コントローラ1 の第63桁に並んでいる。
API int e500_symbols(Machine *m,int index) {
    if(!m || index<0 || index>7 || !m->lcd[0].on)return 0;
    return m->lcd[0].ram[((index+m->lcd[1].start/8)&7)*64+63];
}
// デバッガのウォッチ：アドレスを指定し、読まれた回数を数える。
API void e500_watch(Machine *m,int address) {if(m){m->watchAddr=address;m->watchHits=0;}}
API int e500_watch_hits(Machine *m) {return m?(int)m->watchHits:0;}
// バス経由で1バイト読む（LCD の I/O も反応する）。
API int e500_peek(Machine *m,int address) {return m?m->Get_8(address):0;}
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
    for(int i=0;i<length;i++)if(!m->isRam(m->mirror(address+i)))return 0;
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
// 32KB の内蔵 RAM（B8000-BFFFF）の読み出しと書き込み（書き込むと CPU をリセット）。古い形式との互換用。
API void e500_ram(Machine *m,BYTE *out) {if(m && out)std::copy_n(m->mem.begin()+0xb8000,32768,out);}
API int e500_load_ram(Machine *m,const BYTE *data,int size) {
    if(!m || !data || size!=32768)return 0;
    std::copy_n(data,size,m->mem.begin()+0xb8000);m->cpu.Reset();return 1;
}

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
// 今の形式のスナップショットの大きさ（バイト）。
API int e500_state_size() {return sizeof(Snapshot);}
// The card capacity a snapshot was taken with, read here so that the desktop
// knows nothing of the layouts: -1 for data in no layout this build reads.
// 状態のデータに記録された RAM カードの容量（KB、なしは 0）。知らない形式なら -1。
// 形式の知識をここだけに置き、デスクトップ側は持たない。
API int e500_state_card(const BYTE *data,int size) {
    if(!data)return -1;
    if(size==int(sizeof(LegacySnapshot)))return memcmp(data,"E500ST01",8)?-1:0;
    const char *magic=size==int(sizeof(Snapshot))?"E500ST04":size==int(sizeof(SnapshotV2))?"E500ST02":nullptr;
    if(!magic || memcmp(data,magic,8))return -1;
    uint32_t card;memcpy(&card,data+offsetof(SnapshotV2,cardKB),sizeof(card));
    return validCapacity(int(card),true)?int(card):-1;
}
// The one place that says when a snapshot may be taken or restored; the
// desktop asks these too instead of keeping its own copy of the rule. The
// printer, and the CE-140F's part in its codes, go into the snapshot, so the
// printer never holds one off.
// 状態保存してよいか（CPU のエラー中や CE-140F の転送中・ファイルを開いている間は不可）。
API int e500_can_save(Machine *m) {return m && !m->cpu.fault && m->disk.canSnapshot();}
// Restoring cannot resume a CE-140F transfer or open file.
// 状態を読み込んでよいか（CE-140F の転送中・ファイルを開いている間は不可）。
API int e500_can_load(Machine *m) {return m && m->disk.canSnapshot();}
// 状態を out に保存する。size は e500_state_size と同じであること。保存できない状態なら 0。
API int e500_save(Machine *m,BYTE *out,int size) {
    if(!out || size!=sizeof(Snapshot) || !e500_can_save(m))return 0;
    auto holder=std::make_unique<Snapshot>();auto &s=*holder;
    memcpy(s.magic,"E500ST04",8);s.internalKB=m->internalKB;s.cardKB=m->cardKB;s.reg=m->cpu.reg;
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
    auto holder=std::make_unique<Snapshot>();auto &s=*holder;
    // Older formats come from before what they lack, which was idle when they could be taken.
    s.printer=Ce126p().save();s.disk=Ce140f().saveCode();
    if(size==sizeof(LegacySnapshot)) {
        LegacySnapshot old;memcpy(&old,data,sizeof(old));if(memcmp(old.magic,"E500ST01",8))return 0;
        memcpy(s.magic,"E500ST04",8);s.internalKB=32;s.cardKB=0;s.reg=old.reg;memcpy(s.imem,old.imem,256);
        memset(s.memory,0xff,sizeof(s.memory));memcpy(s.memory+0x78000,old.ram,32768);
        s.lcd[0]=old.lcd[0];s.lcd[1]=old.lcd[1];s.pre1=old.pre1;s.pre2=old.pre2;s.halt=old.halt;s.off=old.off;
        s.cycles=old.cycles;s.ticks=old.ticks;s.ticks2=old.ticks2;
    } else if(size==sizeof(SnapshotV2)) {
        memcpy(static_cast<SnapshotV2*>(&s),data,sizeof(SnapshotV2));
        if(memcmp(s.magic,"E500ST02",8))return 0;
        memcpy(s.magic,"E500ST04",8);
    } else if(size==sizeof(s))memcpy(&s,data,sizeof(s));else return 0;
    if(memcmp(s.magic,"E500ST04",8) || !validInternal(s.internalKB,m->romKB) || !validCapacity(s.cardKB,true) ||
       s.pre1>3 || s.pre2>3 || s.halt>1 || s.off>1 || s.reg.x.p>0xfffff ||
       s.ticks>=MainTimerCycles || s.ticks2>=SubTimerCycles || s.cycles>UINT64_MAX-10240000)return 0;
    for(auto &l:s.lcd)if(l.page>7 || l.column>63 || l.start>63 || l.on>1)return 0;
    // Checked before anything changes, so a bad one leaves the machine as it was;
    // the restores below then cannot fail.
    // The two devices see the same lines, so their levels agree (an older
    // snapshot has none, all zero).
    // 2つの機器は同じ線を見ているので、線の状態は一致していなければならない。
    if(!Ce126p::valid(s.printer,s.cycles) || !Ce140f::validCode(s.disk,s.cycles) ||
       s.printer.oldXout!=s.disk.oldXout || s.printer.oldBusy!=s.disk.oldBusy)return 0;
    m->internalKB=s.internalKB;m->cardKB=s.cardKB;m->cpu.reg=s.reg;
    memcpy(m->cpu.imem,s.imem,256);memcpy(m->mem.data()+0x40000,s.memory,0x80000);
    m->lcd[0]=s.lcd[0];m->lcd[1]=s.lcd[1];m->cpu.pre_1=s.pre1;m->cpu.pre_2=s.pre2;
    m->cpu.halt=s.halt;m->cpu.off=s.off;m->cpu.fault=false;
    m->cpu.cycles=s.cycles;m->cpu.ticks=s.ticks;m->cpu.ticks2=s.ticks2;m->keys.fill(0);
    m->audio.clear();m->samplePhase=0;
    // An older snapshot has no levels for the lines: the tone starts from its
    // beginning and they are taken from the loaded ports, so XOUT high in it is
    // not seen to rise on the first step. A current one has them, and the tone
    // goes on from the saved level.
    // 古い形式には線の状態がないので、音は最初から始め、線の状態は読み込んだポートから決める。
    // 今の形式では、保存した XOUT の値から音を続ける（どちらも、偽の立ち上がりを作らないため）。
    if(size!=int(sizeof(Snapshot))) {
        m->lastSoundMode=m->soundMode();m->tonePhase=0;
        auto bus=m->busLevels(m->lastSoundMode);
        s.printer.oldXout=s.disk.oldXout=bus.xout;s.printer.oldBusy=s.disk.oldBusy=bus.busy;
    } else m->restartTone(s.printer.oldXout!=0);
    m->disk.restoreCode(s.disk,s.cycles);m->printer.restore(s.printer,s.cycles);return 1;
}
