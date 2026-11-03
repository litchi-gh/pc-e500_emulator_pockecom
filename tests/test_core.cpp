// SPDX-License-Identifier: GPL-2.0-or-later
// ネイティブのコア（CPU・バス・キーボード・LCD・タイマー・状態保存・CE-140F・CE-126P）の
// 単体テスト。ROM を使わず、命令やバスの信号を直接与えて確かめる。tools/test-native.cmd で
// ビルドして実行する。失敗すると最初に失敗した確認の名前を出して終了コード 1 を返す。
#include "../src/native/machine.h"
#include <memory>
#include <iostream>
#include <vector>
#include <stdexcept>
#include <fstream>
#include <chrono>
#include <cstddef>
extern "C" int e500_state_size();
extern "C" int e500_can_save(Machine*);
extern "C" int e500_save(Machine*,BYTE*,int);
extern "C" int e500_load(Machine*,const BYTE*,int);
extern "C" int e500_state_card(const BYTE*,int);
extern "C" int e500_memory(Machine*,int,BYTE*,int,int);
extern "C" int e500_audio(Machine*,int16_t*,int);
extern "C" void e500_reset(Machine*,int);
extern "C" Machine* e500_create(const BYTE*,int);
extern "C" Machine* e500_create_config(const BYTE*,int,int,int);
extern "C" void e500_destroy(Machine*);
extern "C" int e500_write(Machine*,int,const BYTE*,int);
extern "C" int e500_dump(Machine*,int,BYTE*,int);

// 確認の数と、確認の本体（条件が偽なら message の例外で止める）。
int checks=0;
void check(bool condition,const char* message) {++checks;if(!condition)throw std::runtime_error(message);}
int main() {
    try {
        auto m=std::make_unique<Machine>();auto &c=m->cpu;
        m->mem[0xffffd]=0x00;m->mem[0xffffe]=0x01;m->mem[0xfffff]=0x0c;
        c.Reset();check(c.reg.x.p==0xc0100,"reset vector");
        m->mem[0xc0100]=0x08;m->mem[0xc0101]=0xff; // LD A,FF
        c.step();check(c.reg.r.a==255 && c.reg.x.p==0xc0102,"immediate load");
        m->mem[0xc0102]=0x40;m->mem[0xc0103]=1; // ADD A,1
        c.step();check(c.reg.r.a==0 && c.reg.r.c && c.reg.r.z,"8-bit carry/zero");
        m->Set_8(0xc0000,0x12);check(m->Get_8(0xc0000)==0xff,"ROM write protection");
        m->Set_8(0x80042,0x37);check(m->Get_8(0xb8042)==0x37 && m->Get_8(0xa0042)==0x37,"32KB RAM mirrors");
        m->Set_8(0x40000,0x12);check(m->Get_8(0x40000)==0xff,"absent RAM card");
        c.pre_1=0;c.imem[255]=0x34;c.imem[0]=0x12;c.imem[1]=5;
        check(c.Get_i16(255,0)==0x1234 && c.Get_i20(255,0)==0x51234,"internal RAM read wraps at FF");
        c.Set_i20(255,0xabcde,0);check(c.imem[255]==0xde && c.imem[0]==0xbc && c.imem[1]==0xa,"internal RAM write wraps");
        c.pre_1=1;c.imem[0xec]=0xf0;check(c.Conv_imemAdr(0x30,0)==0x20,"BP address wraps");
        c.pre_1=2;c.imem[0xed]=0x12;check(c.Conv_imemAdr(3,0)==0x15,"PX prefix");
        m->keys[5]=0x40;c.imem[IMEM_KOL]=0x20;c.imem[IMEM_KOH]=0;m->scanKeys();
        check(c.imem[IMEM_KI]==0x40 && (c.imem[IMEM_ISR]&4),"keyboard selected row/IRQ");
        c.imem[IMEM_KOL]=0;m->scanKeys();check(c.imem[IMEM_KI]==0,"unselected keyboard row");
        Lcd l;l.command(0x3f);l.command(0xb8);l.command(0x7f);l.command(0x1a5);
        check(l.on && l.ram[63]==0xa5 && l.column==0,"LCD column wrap");
        l.command(0x40);check(l.command(0x300)==0xa5,"LCD pipelined read");
        // The sub timer is 64 Hz: 16000 cycles, not the 2 ms PockEmul uses.
        c.imem[IMEM_ISR]=0;for(int i=0;i<62;i++)c.AddState(255);
        check(!(c.imem[IMEM_ISR]&1),"fast timer waits a full 1/64 s");
        c.AddState(255);check(c.imem[IMEM_ISR]&1,"fast timer");
        for(int i=0;i<2100;i++)c.AddState(255);check(c.imem[IMEM_ISR]&2,"slow timer");
        m->lcd[0]=l;c.pre_1=1;c.pre_2=1;
        std::vector<BYTE> state(e500_state_size());
        check(e500_save(m.get(),state.data(),int(state.size()))==1,"save snapshot");
        // The card capacity is read natively, by layout: the current one and the
        // released v2 (offset 12, after the 8-byte magic and the internal size),
        // and -1 for anything else.
        // 状態のデータからカードの容量を読む（今の形式と形式2。知らない形式は -1）。
        check(e500_state_card(state.data(),int(state.size()))==m->cardKB,"card capacity read from a snapshot");
        {
            std::vector<BYTE> v2(state.size()-sizeof(Ce126p::State)-sizeof(Ce140f::CodeState));
            std::copy_n(state.begin(),v2.size(),v2.begin());memcpy(v2.data(),"E500ST02",8);v2[12]=16;
            check(e500_state_card(v2.data(),int(v2.size()))==16,"card capacity read from a v2 snapshot");
            memcpy(v2.data(),"E500ST99",8);check(e500_state_card(v2.data(),int(v2.size()))==-1,"unknown snapshot layout refused");
            auto future=state;memcpy(future.data(),"E500ST05",8);check(e500_state_card(future.data(),int(future.size()))==-1,"unknown magic refused");
        }
        m->Set_8(0xb8042,0);c.reg.r.a=99;m->lcd[0].on=0;
        check(e500_load(m.get(),state.data(),int(state.size()))==1,"load snapshot");
        check(m->Get_8(0xb8042)==0x37 && c.reg.r.a==0 && m->lcd[0].on,"snapshot restores CPU/RAM/LCD");
        {
            // The printer and the disk see the same lines, and reserved bytes are zero.
            // プリンタとディスクの線の状態が食い違う状態や、予約のバイトが 0 でない状態は断る。
            size_t diskAt=state.size()-sizeof(Ce140f::CodeState),printerAt=diskAt-sizeof(Ce126p::State);
            auto odd=state;odd[printerAt+offsetof(Ce126p::State,oldXout)]^=1;
            check(!e500_load(m.get(),odd.data(),int(odd.size())),"reject printer and disk disagreeing on XOUT");
            odd=state;odd[diskAt+offsetof(Ce140f::CodeState,oldBusy)]^=1;
            check(!e500_load(m.get(),odd.data(),int(odd.size())),"reject printer and disk disagreeing on BUSY");
            odd=state;odd[printerAt+offsetof(Ce126p::State,reserved)+5]=1;
            check(!e500_load(m.get(),odd.data(),int(odd.size())),"reject a printer state with reserved bytes set");
            odd=state;odd[diskAt+offsetof(Ce140f::CodeState,reserved)+1]=1;
            check(!e500_load(m.get(),odd.data(),int(odd.size())),"reject a disk state with reserved bytes set");
            odd=state;odd[printerAt+offsetof(Ce126p::State,oldXout)]^=1;odd[diskAt+offsetof(Ce140f::CodeState,oldXout)]^=1;
            check(e500_load(m.get(),odd.data(),int(odd.size()))==1,"load printer and disk agreeing on XOUT");
            check(e500_load(m.get(),state.data(),int(state.size()))==1,"load snapshot again");
        }
        state[0]^=0xff;check(!e500_load(m.get(),state.data(),int(state.size())),"reject invalid snapshot");
        check(m->Get_8(0xb8042)==0x37,"failed load preserves machine");
        {
            // The tone's phase is not saved: loaded in the low half of a mode-2
            // tone, XOUT goes on low, and neither device sees it rise.
            // 音のモード2の Low の半周期で保存して読み込んでも、XOUT は Low から続き、機器は立ち上がりを見ない。
            BYTE scr=c.imem[IMEM_SCR];c.imem[IMEM_SCR]=0x20;
            for(int i=0;i<300;i++)m->peripherals(1);
            check(!m->xout && !m->printer.ack,"mode-2 tone in its low half");
            std::vector<BYTE> tone(e500_state_size());
            check(e500_save(m.get(),tone.data(),int(tone.size()))==1 && e500_load(m.get(),tone.data(),int(tone.size()))==1,"snapshot in a tone's low half");
            m->peripherals(1);check(!m->xout && !m->printer.ack,"tone goes on low after a load, with no code begun");
            c.imem[IMEM_SCR]=scr;m->peripherals(1);
        }
        check(!e500_load(m.get(),state.data(),10),"reject truncated snapshot");
        {
            std::vector<BYTE> mainRom(256*1024,0x5a),familyRom(384*1024,0xff);
            mainRom[mainRom.size()-3]=0x34;mainRom[mainRom.size()-2]=0x12;mainRom[mainRom.size()-1]=0x0c;
            std::fill(familyRom.begin(),familyRom.begin()+128*1024,0xa5);
            std::copy(mainRom.begin(),mainRom.end(),familyRom.begin()+128*1024);
            check(e500_create(mainRom.data(),int(mainRom.size())-1)==nullptr,"reject unsupported ROM size");
            std::unique_ptr<Machine,void(*)(Machine*)> e500(e500_create(mainRom.data(),int(mainRom.size())),e500_destroy);
            check(e500 && e500->romKB==256 && e500->Get_8(0x20000)==0xff && e500->Get_8(0xc0000)==0x5a,"PC-E500 ROM map");
            std::unique_ptr<Machine,void(*)(Machine*)> e650(e500_create(familyRom.data(),int(familyRom.size())),e500_destroy);
            check(e650 && e650->romKB==384 && e650->internalKB==64 && e650->Get_8(0x20000)==0xa5 && e650->Get_8(0x3ffff)==0xa5 && e650->Get_8(0xc0000)==0x5a,"PC-E650/U6000 split ROM map");
            check(e650->cpu.reg.x.p==0xc1234,"PC-E650/U6000 reset vector comes from CE2 ROM");
            e650->Set_8(0x20000,0);e650->Set_8(0xc0000,0);
            check(e650->Get_8(0x20000)==0xa5 && e650->Get_8(0xc0000)==0x5a,"both internal ROM regions are write protected");
            std::unique_ptr<Machine,void(*)(Machine*)> configured(e500_create_config(familyRom.data(),int(familyRom.size()),256,128),e500_destroy);
            check(configured && configured->internalKB==256 && configured->cardKB==128,"PC-E650/U6000 accepts the 628128 expansion to 256KB");
            for(int kb:{64,128,256}) {
                std::unique_ptr<Machine,void(*)(Machine*)> sized(e500_create_config(familyRom.data(),int(familyRom.size()),kb,0),e500_destroy);
                check(sized && sized->internalKB==kb,"PC-E650/U6000 internal RAM capacity");
            }
            for(int kb:{8,16,32})
                check(e500_create_config(familyRom.data(),int(familyRom.size()),kb,0)==nullptr,"PC-E650/U6000 rejects capacities below its two 62256 sockets");
            configured->Set_8(0x80000,0x81);
            check(configured->Get_8(0x80000)==0x81 && configured->Get_8(0xbffff)!=0x81,"256KB internal RAM reaches 80000 without mirroring");
            // Debugger writes land in RAM only, through the mirrors, and all or nothing.
            BYTE patch[4]={1,2,3,4},back[4]={};
            check(e500_write(configured.get(),0xbfffe,patch,2) && configured->Get_8(0xbfffe)==1 && configured->Get_8(0xbffff)==2,"debugger write into internal RAM");
            check(e500_write(configured.get(),0x7fffe,patch,2) && configured->Get_8(0x5fffe)==1 && configured->Get_8(0x40000+128*1024-1)==2,"debugger write through the card mirror");
            check(!e500_write(configured.get(),0xbffff,patch,2) && configured->Get_8(0xc0000)==0x5a && configured->Get_8(0xbffff)==2,"debugger write that reaches ROM is refused whole");
            check(!e500_write(configured.get(),0x10000,patch,1) && !e500_write(configured.get(),0xfffff,patch,2),"debugger write refuses unmapped space and the end of memory");
            check(e500_dump(configured.get(),0xbfffe,back,2) && back[0]==1 && back[1]==2,"debugger dump reads the written bytes");
        }
        for(int kb:{8,16,32,64,128,256}) {
            m->internalKB=m->cardKB=kb;m->mem.fill(0xff);
            int start=0xc0000-kb*1024;
            m->Set_8(start,0xa5);m->Set_8(0xbffff,0x5a);
            check(m->Get_8(0x80000)==0xa5 && m->Get_8(0xbffff)==0x5a,"RAM capacity and mirroring");
            m->Set_8(0x40000,0x73);m->Set_8(0x40000+kb*1024-1,0x37);
            check(m->Get_8(0x7ffff)==0x37,"card capacity/mirroring");
            std::vector<BYTE> bank(kb*1024);
            check(e500_memory(m.get(),1,bank.data(),int(bank.size()),0)==1 && bank[0]==0x73 && bank.back()==0x37,"card image export");
            bank[0]=0xc5;e500_memory(m.get(),1,bank.data(),int(bank.size()),1);
            check(m->Get_8(0x40000)==0xc5,"card image import");
            c.off=true;e500_save(m.get(),state.data(),int(state.size()));
            m->Set_8(0x40000,0);m->internalKB=m->cardKB=32;
            check(e500_load(m.get(),state.data(),int(state.size()))==1 && m->internalKB==kb && m->cardKB==kb && m->Get_8(0x40000)==0xc5 && c.off,"OFF snapshot retains capacities and card");
            e500_reset(m.get(),1);check(m->Get_8(0x40000)==0xc5 && m->Get_8(0xbffff)==0xff,"internal reset preserves card");
        }
        c.off=false;c.imem[IMEM_SCR]=0x20;m->audio.clear();
        for(int i=0;i<100;i++)c.AddState(128);
        int16_t pcm[1000];int count=e500_audio(m.get(),pcm,1000),edges=0;
        for(int i=1;i<count;i++)edges+=pcm[i]!=pcm[i-1];
        check(count==600 && edges>=48 && edges<=51,"2kHz XOUT PCM generation");
        // Uneven instruction lengths must not drag the 4 kHz BEEP edges off the
        // sample grid: at 48 kHz every half period is exactly six samples wide.
        // The sample grid starts at whatever offset the previous tone left behind,
        // and this one lands 4 kHz edges right on it.
        c.imem[IMEM_SCR]=0x30;m->audio.clear();m->samplePhase=96024;m->lastSoundMode=-1;
        for(int i=0;i<3000;i++)c.AddState(7);
        count=e500_audio(m.get(),pcm,1000);
        int run=1,uneven=0;
        for(int i=1;i<count;i++){if(pcm[i]==pcm[i-1])run++;else{if(run!=6)uneven++;run=1;}}
        check(count==984 && uneven<=1,"4kHz BEEP half periods stay six samples wide");
        c.imem[IMEM_SCR]=0;m->audio.clear();for(int i=0;i<100;i++)c.AddState(128);
        count=e500_audio(m.get(),pcm,1000);check(std::all_of(pcm,pcm+count,[](int16_t v){return v==-6000;}),"XOUT low after BEEP");
        {
            namespace fs=std::filesystem;
            auto path=fs::temp_directory_path()/("pce500-native-"+std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
            Ce140f disk;check(disk.mount(path.c_str(),false),"CE140F mount");
            auto packet=[](std::vector<BYTE> b){BYTE sum=0;for(auto v:b)sum+=v;b.push_back(sum);return b;};
            auto filename=[](BYTE cmd,std::string stem,std::string ext="BAS") {
                std::vector<BYTE> b{cmd,'X',':'};stem.resize(8,' ');stem+='.';stem+=ext;stem.resize(12,' ');b.insert(b.end(),stem.begin(),stem.end());return b;
            };
            check(disk.command({5,0})==std::vector<BYTE>{255},"CE rejects checksum mismatch");
            check(disk.command(packet(filename(0x10,"TEST")))==std::vector<BYTE>{0},"SAVE filename");
            check(!disk.canSnapshot(),"SAVE context prevents inconsistent snapshots");
            check(disk.command(packet({0x11,0,3,0,0}))==std::vector<BYTE>{0},"SAVE size");
            check(disk.command(packet({0x41,0x42,0x43}))==std::vector<BYTE>{0},"SAVE commit");
            check(fs::file_size(path/"TEST.BAS")==3,"SAVE creates disk file");
            check(disk.command(packet(filename(0x0e,"TEST")))==std::vector<BYTE>({0,32,3,0,0,35}),"LOAD size response");
            check(disk.command(packet({0x17,1}))==std::vector<BYTE>({0,65,65}),"LOAD first byte");
            check(disk.command(packet({0x0f,1}))==std::vector<BYTE>({0,66,67,133,0}),"LOAD stream checksums");
            {
                // FILES "X:" comes as ????????.???, padded to the 8.3 fields: a ?
                // stands for a space too, so short names are listed (they were not).
                // FILES の ? は空白にも合う：短い名前も一覧に出る。* はその欄の残りすべて。
                std::ofstream(path/"A.BAS")<<"1";std::ofstream(path/"LONGNAME.TXT")<<"1";
                check(disk.command(packet(filename(0x05,"????????","???")))==std::vector<BYTE>({0,3,3}),"FILES ????????.??? lists short names");
                check(disk.command(packet({0x06}))==std::vector<BYTE>({0,'X',':','A',' ',' ',' ',' ',' ',' ',' ','.','B','A','S',' ',BYTE(0+'X'+':'+'A'+7*' '+'.'+'B'+'A'+'S'+' ')}),"FILES first name, padded");
                check(disk.command(packet(filename(0x05,"*","BAS")))==std::vector<BYTE>({0,2,2}),"FILES *.BAS");
                check(disk.command(packet(filename(0x05,"T???","???")))==std::vector<BYTE>({0,1,1}),"FILES T???.??? matches TEST");
                check(disk.command(packet(filename(0x05,"T??","???")))==std::vector<BYTE>({0,0,0}),"FILES T??.??? does not match TEST");
                fs::remove(path/"A.BAS");fs::remove(path/"LONGNAME.TXT");
            }
            auto bad=filename(0x10,"..\\EVIL");check(disk.command(packet(bad))==std::vector<BYTE>{255},"CE rejects path traversal");
            {
                // A folder that cannot be opened (here a file) leaves the drive on the
                // one it had, with its open file still open.
                // 開けないフォルダを指定しても、それまでのフォルダとファイルのまま。
                auto open=filename(0x03,"TEST");open.push_back(1);open.push_back(2);
                check(disk.command(packet(open))==std::vector<BYTE>{0},"CE140F file open before a bad mount");
                check(!disk.mount((path/"TEST.BAS").c_str(),false),"CE140F refuses a file as its folder");
                check((disk.status()&1) && !disk.canSnapshot() && disk.command(packet({0x1b,2}))!=std::vector<BYTE>{255},"CE140F keeps its folder and open file after a refused mount");
                check(disk.command(packet({0x04,0xff}))==std::vector<BYTE>{0} && disk.canSnapshot(),"CE140F closes the file");
            }
            disk.mount(path.c_str(),true);check(disk.command(packet(filename(0x0a,"TEST")))==std::vector<BYTE>{255} && fs::exists(path/"TEST.BAS"),"write protection enforces KILL");
            {
                // The machine's RESET closes the drive's files and transfer: BASIC
                // has no file number left to close them with.
                // 本体の RESET で、CE-140F の開いたファイルと転送も閉じる（接続はそのまま）。
                auto r=std::make_unique<Machine>();check(r->disk.mount(path.c_str(),false),"CE140F mounted in a machine");
                auto open=filename(0x03,"TEST");open.push_back(1);open.push_back(2);
                check(r->disk.command(packet(open))==std::vector<BYTE>{0} && r->disk.command(packet(filename(0x10,"NEW")))==std::vector<BYTE>{0} &&
                      !e500_can_save(r.get()),"CE140F file open and SAVE under way");
                e500_reset(r.get(),0);
                check(e500_can_save(r.get()) && (r->disk.status()&1),"RESET closes the CE140F's files and transfer, mounted still");
                r->disk.mount(nullptr,false);
            }
            // Only the specifically created file and empty test directory are removed.
            fs::remove(path/"TEST.BAS");fs::remove(path);
        }
        {
            // The ROM's side of the CE-126P bus: a device code with XOUT high,
            // then data bytes with XOUT low, each bit LSB first on a BUSY strobe.
            // A CE-140F with a folder shares the bus, as it does in the machine.
            namespace fs=std::filesystem;
            auto path=fs::temp_directory_path()/("pce500-native-bus-"+std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
            fs::create_directory(path);
            Ce126p p;Ce140f disk;check(disk.mount(path.c_str(),false),"CE140F mount beside the printer");
            bool xout=false;uint64_t now=0;bool apart=false;
            auto tick=[&](bool busy,bool bit) {
                now+=100;p.tick(now,xout,busy,bit);disk.tick(now,xout,busy,bit?4:0);
                // The printer answers every code; the disk only codes whose first
                // bit is 1, as 41h's. It never holds the wired-OR ACK up where the
                // printer has let it down.
                apart|=xout && disk.ack && !p.ack;
            };
            auto strobe=[&](bool bit){tick(false,bit);tick(true,bit);tick(false,bit);};
            auto device=[&](BYTE code) {
                xout=true;tick(false,false);
                for(int i=0;i<8;i++){check(p.ack,"CE126P ready for a device bit");strobe(code>>i&1);}
                xout=false;tick(false,false);
            };
            auto data=[&](BYTE b){for(int i=0;i<8;i++)strobe(b>>i&1);};
            auto printed=[&]{BYTE b[64];int n=p.read(b,sizeof(b));return std::string(b,b+n);};
            auto trace=[&]{char b[4096];int n=disk.trace(b,sizeof(b));return std::string(b,n);};
            std::string before=trace();
            device(0x0f);device(0x20);data(0x0f);
            check(p.status()==1 && !p.ack && printed().empty(),"CE126P presence check ends the selection");
            device(0x20);for(BYTE b:{BYTE('H'),BYTE(0xf0),BYTE(0x30),BYTE(' ')})data(b);data(0x0d);
            check(p.status()==3,"CE126P stays selected until 0Eh");
            data(0x0e);check(p.status()==1 && !p.ack,"CE126P 0Eh returns to idle");
            check(printed()=="H0O\n","CE126P line decoded back to the PC-E500 set");
            check(!apart && !disk.ack && (disk.status()&4)==0 && disk.canSnapshot(),"CE140F stays out of the printer's codes");
            check(trace()==before,"CE140F trace untouched by the printer's codes");
            // BUSY strobes after the line (another device, or the CE-140F's
            // nibbles) must neither raise ACK nor print.
            bool raised=false;for(int i=0;i<8;i++){tick(true,true);raised|=p.ack;tick(false,true);}
            data(0x0d);check(!raised && printed().empty() && p.status()==1,"CE126P ignores the bus while idle");
            device(0x41);check(p.status()==1 && !p.ack,"CE126P not selected by the CE-140F code");
            check(trace().find("Device select 41")!=std::string::npos,"CE140F selected by its own code");
            now+=10000;tick(false,false);check((disk.status()&4)!=0,"CE140F receiving after its acknowledge");
            xout=true;tick(false,false);check((disk.status()&4)!=0,"CE140F busy while a tone cuts into its transfer");
            xout=false;tick(false,false);
            // A tone in the middle of a transfer leaves it going.
            for(int i=0;i<4;i++){xout=!xout;tick(false,false);}
            check((disk.status()&4)!=0 && !disk.ack,"CE140F transfer survives a tone");
            // The ROM turning to the printer mid-transfer ends the transfer, so
            // the printer's data strobes are not taken for CE-140F nibbles.
            device(0x20);check((disk.status()&4)==0 && trace().find("Transfer dropped for device 20")!=std::string::npos,"CE140F drops a transfer for another device");
            bool diskAck=false;for(BYTE b:{BYTE('Z'),BYTE(0x0d),BYTE(0x0e)})for(int i=0;i<8;i++){strobe(b>>i&1);diskAck|=disk.ack;}
            now+=60000;tick(false,false);
            check(!diskAck && !disk.ack && (disk.status()&4)==0 && printed()=="Z\n","CE140F ignores the printer's data after dropping its transfer");
            device(0x10);check(p.status()==1,"CE126P not selected by code 10h");
            // A tone on XOUT is a code cut short, and changes nothing. Each high
            // half looks like the start of a code, which must not hold off saves.
            bool saves=true;
            for(int i=0;i<20;i++){xout=!xout;tick(false,false);saves&=disk.canSnapshot();}
            check(saves,"BEEP leaves snapshots allowed");
            xout=false;tick(false,false);check(p.status()==1 && !p.ack && !disk.ack && disk.canSnapshot(),"BEEP selects neither device");
            // A snapshot mid-line carries the printer's place: restored over a
            // printer that has moved on, the line goes on where it was.
            device(0x20);data('A');strobe(0);strobe(1);
            auto saved=p.save();p.resetBus();
            Ce126p::State bad=saved;bad.phase=9;
            check(!p.restore(bad,now) && p.status()==1,"CE126P refuses an impossible state");
            // Fields each in range but not together, as tick never leaves them.
            Ce126p::State loose=saved;loose.bits=8;loose.oldBusy=0;
            check(!p.restore(loose,now),"CE126P refuses a line's eighth bit with BUSY already low");
            loose=saved;loose.phase=0;check(!p.restore(loose,now),"CE126P refuses idle with bits taken");
            loose=saved;loose.value=0xff;check(!p.restore(loose,now),"CE126P refuses bits beyond those taken");
            loose=saved;loose.line[0]=0x0a;check(!p.restore(loose,now),"CE126P refuses a control byte in the line");
            loose=saved;loose.line[loose.length]='Z';check(!p.restore(loose,now),"CE126P refuses bytes past the line's length");
            loose=saved;loose.phase=1;check(!p.restore(loose,now),"CE126P refuses a code with XOUT low");
            check(p.restore(saved,now) && p.status()==3,"CE126P restores mid-line");
            for(int i=2;i<8;i++)strobe('B'>>i&1);data(0x0d);data(0x0e);
            check(printed()=="AB\n" && p.status()==1,"CE126P line resumes from a snapshot mid-byte");
            // A line the ROM abandons (BREAK) times out instead of holding the bus.
            device(0x20);data('A');check(p.status()==3,"CE126P selected mid-line");
            now+=2048000;tick(false,false);check(p.status()==1,"CE126P abandoned line times out");
            device(0x20);data(0x0d);data(0x0e);check(printed()=="\n","CE126P drops the abandoned half line");
            // Unread output keeps the newest lines, like the window's roll.
            for(int i=0;i<3000;i++) {
                std::string number=std::to_string(i);number.insert(0,24-number.size(),'X');
                device(0x20);for(char c:number)data(c=='0'?0xf0:BYTE(c));data(0x0d);data(0x0e);
            }
            std::vector<BYTE> all(70000);int n=p.read(all.data(),int(all.size()));std::string kept(all.begin(),all.begin()+n);
            int lines=int(std::count(kept.begin(),kept.end(),'\n'));
            check(n<=64*1024 && lines<3000 && kept.substr(kept.size()-5)=="2999\n" && std::stoi(kept.substr(kept.find_first_not_of('X')))==3000-lines,"CE126P full buffer drops the oldest lines");
            auto count=[&](const std::string& what){size_t n=0;auto t=trace();for(size_t at=t.find(what);at!=std::string::npos;at=t.find(what,at+1))n++;return n;};
            // XOUT held high by a tone mode or the serial line, with no strobe for
            // longer than either device's timeout, is no code: each lets it go
            // quietly, with no error and its ACK down on the wired-OR line, and
            // takes the next code as ever.
            bool wasApart=apart;
            xout=true;tick(false,false);check(p.ack && p.status()==1,"CE126P answers XOUT, and is not busy");
            now+=1100000;tick(false,false);check(!p.ack && p.status()==1,"CE126P lets XOUT held high go");
            tick(false,true);check(disk.ack,"CE140F answers a 1 on DOUT under XOUT");
            now+=5000000;tick(false,true);
            check(!disk.ack && trace().find("Bus timeout")==std::string::npos && (disk.status()&8)==0 && disk.canSnapshot(),"CE140F lets XOUT held high go quietly");
            apart=wasApart;
            xout=false;tick(false,false);device(0x20);data('H');data(0x0d);data(0x0e);
            check(printed()=="H\n","CE126P takes the next code after XOUT held high");
            // Plugged in, or a drive mounted, under XOUT already high: that is no
            // rise, and so no code to answer.
            {
                Ce126p late;late.connected=false;late.tick(now,true,false,true);
                late.connected=true;late.resetBus();late.tick(now+100,true,false,true);
                check(!late.ack && late.status()==1,"CE126P plugged in under XOUT high answers nothing");
                Ce140f drive;drive.tick(now,true,false,4);check(drive.mount(path.c_str(),false),"CE140F mounted under XOUT high");
                drive.tick(now+100,true,false,4);check(!drive.ack,"CE140F mounted under XOUT high answers nothing");
                drive.mount(nullptr,false);
            }
            // A code going by is no transfer of the CE-140F's: it allows a snapshot
            // mid-code and carries its place, so even its own 41h, cut by a
            // restore, is accepted when the rest of the bits come.
            // Mounted again mid-code (a settings change, or the desktop mounting the
            // drive after a restore), the drive keeps the bits it has taken.
            size_t remounted=count("Device select 41");
            xout=true;tick(false,false);for(int i=0;i<3;i++)strobe(0x41>>i&1);
            check(disk.mount(path.c_str(),false),"CE140F mounted again mid-code");
            for(int i=3;i<8;i++)strobe(0x41>>i&1);xout=false;tick(false,false);
            check(count("Device select 41")==remounted+1,"CE140F accepts its code after being mounted again");
            now+=10000;tick(false,false);device(0x20);data(0x0e);
            check((disk.status()&4)==0 && p.status()==1,"CE140F transfer dropped for the printer, which goes idle");
            // XOUT can fall after the eighth bit's strobe rises and before it falls;
            // a snapshot taken then has to load again.
            xout=true;tick(false,false);for(int i=0;i<7;i++)strobe(0x20>>i&1);
            tick(true,false);xout=false;tick(true,false);
            auto late=disk.saveCode();
            check(disk.canSnapshot() && Ce140f::validCode(late,now),"CE140F state with XOUT down before the last strobe ends loads again");
            tick(false,false);data(0x0e);
            check(p.status()==1 && (disk.status()&4)==0,"both idle after a code ending with XOUT down early");
            size_t selects=count("Device select 41");
            xout=true;tick(false,false);for(int i=0;i<3;i++)strobe(0x41>>i&1);
            check(disk.canSnapshot() && (p.status()&2)!=0,"CE140F allows a snapshot mid-code");
            auto code=disk.saveCode();disk.resetBus();
            Ce140f::CodeState wrong=code;wrong.bits=9;check(!disk.restoreCode(wrong,now),"CE140F refuses an impossible code state");
            wrong=code;wrong.device=0xff;check(!disk.restoreCode(wrong,now),"CE140F refuses bits beyond those taken");
            check(disk.restoreCode(code,now),"CE140F restores mid-code");
            for(int i=3;i<8;i++)strobe(0x41>>i&1);xout=false;tick(false,false);
            check(count("Device select 41")==selects+1,"CE140F accepts its code finished after a restore");
            // Tones across a transfer: ACK comes back where the transfer's own
            // handshake has it. Packet 1Dh (free space) and its checksum, low
            // nibble first.
            now+=10000;tick(false,false);
            auto nib=[&](bool busy,BYTE v){now+=100;p.tick(now,xout,busy,false);disk.tick(now,xout,busy,v);};
            nib(true,0xd);check(disk.ack,"CE140F takes a nibble");
            xout=true;nib(true,0xd);xout=false;nib(true,0xd);
            check(disk.ack && (disk.status()&4)!=0,"CE140F ACK stays up after a tone while BUSY holds the nibble");
            nib(false,0xd);check(!disk.ack,"CE140F ACK down after the nibble");
            // A strobe while XOUT is high is a bit of a code, handshaken in step
            // with the printer and never taken for a nibble; the tone over, the
            // transfer goes on.
            xout=true;nib(false,1);nib(true,1);
            check(!disk.ack && !p.ack,"CE140F handshakes a strobe under XOUT as a code bit, with the printer");
            nib(false,1);xout=false;nib(false,1);
            check(!disk.ack && (disk.status()&4)!=0,"CE140F transfer goes on after a tone with a strobe in it");
            nib(true,1);check(disk.ack,"CE140F takes the next nibble");nib(false,1);
            // A tone of 50 ms or more between the bytes is no end of the packet:
            // the gap counts from the tone's end.
            // バイトの間の 50ms 以上の音は、パケットの終わりではない（切れ目は音の終わりから数える）。
            xout=true;nib(false,0);now+=60000;nib(false,0);xout=false;nib(false,0);nib(false,0);
            check((disk.status()&4)!=0 && trace().find("ERROR")==std::string::npos,"CE140F keeps a packet across a long tone");
            for(BYTE v:{BYTE(0xd),BYTE(1)}){nib(true,v);check(disk.ack,"CE140F takes the next nibble");nib(false,v);}
            now+=60000;nib(false,0);
            check((disk.status()&8)==0 && trace().find("ERROR")==std::string::npos,"CE140F nibbles across tones arrive intact");
            // Read the answer back as the ROM would, a tone now and then across a
            // nibble on offer, until the device lets go.
            bool held=true;int offered=0;
            for(int i=0;i<10000 && (disk.status()&4)!=0;i++) {
                nib(false,0);if(!disk.ack)continue;
                if(offered++%2==0){xout=true;nib(false,0);xout=false;nib(false,0);held&=disk.ack;}
                nib(true,0);nib(false,0);
            }
            check(held,"CE140F keeps its answer's ACK across a tone");
            check((disk.status()&4)==0 && disk.canSnapshot(),"CE140F answers the packet and goes idle");
            // A BREAK while the answer goes out leaves the disk sending. The next
            // LPRINT's code ends that, and the line prints with the ACK all the
            // printer's.
            device(0x41);now+=10000;tick(false,false);
            for(BYTE v:{BYTE(0xd),BYTE(1),BYTE(0xd),BYTE(1)}){nib(true,v);nib(false,v);}
            now+=60000;nib(false,0);for(int i=0;i<50 && !disk.ack;i++)nib(false,0);
            check(disk.ack && (disk.status()&4)!=0,"CE140F sending its answer");
            size_t dropped=count("Transfer dropped");apart=false;
            device(0x20);
            bool sending=false;for(BYTE b:{BYTE('Y'),BYTE(0x0d),BYTE(0x0e)})for(int i=0;i<8;i++){strobe(b>>i&1);sending|=disk.ack;}
            check(!apart && count("Transfer dropped")==dropped+1 && !sending && (disk.status()&4)==0 && printed()=="Y\n","CE140F drops its answer for the printer's code");
            // A SAVE broken off between its packets (BREAK, then LPRINT) is over
            // too, and no longer holds off snapshots; a file OPENed stays open.
            {
                auto packet=[](std::vector<BYTE> b){BYTE sum=0;for(auto v:b)sum+=v;b.push_back(sum);return b;};
                std::vector<BYTE> name{0x10,'X',':'};for(char ch:std::string("DROP    .BAS"))name.push_back(BYTE(ch));
                check(disk.command(packet(name))==std::vector<BYTE>{0} && !disk.canSnapshot(),"CE140F SAVE under way");
                size_t before=count("Transfer dropped");
                device(0x20);data(0x0e);
                check(count("Transfer dropped")==before+1 && disk.canSnapshot() && p.status()==1,"CE140F SAVE between packets dropped for the printer's code");
            }
            // A tone in the middle of a line leaves the line going, as a tone
            // leaves a CE-140F transfer; a full code of another device ends it.
            device(0x20);data('A');
            for(int i=0;i<4;i++){xout=!xout;tick(false,false);}
            check(p.status()==3 && !p.ack,"CE126P line survives a tone");
            data('B');data(0x0d);data(0x0e);
            check(printed()=="AB\n","CE126P prints the whole line across a tone");
            // After a RESET the cycle count starts over; a snapshot taken then, with
            // the printer idle, has to load again.
            now=1000000000;device(0x20);data('C');data(0x0d);data(0x0e);printed();
            p.resetBus();now=5000000;
            check(Ce126p::valid(p.save(),now),"CE126P idle state after a RESET loads again");
            auto idle=p.save();check(idle.lastActivity==0,"CE126P idle state carries no time");
            // With no printer on the bus the disk answers a code that starts with
            // 1, as 41h does (0Fh), all the way, and stays silent through one that
            // starts with 0 (20h), so the ROM finds no printer and LPRINT fails at once.
            p.connected=false;tick(false,false);
            for(BYTE c:{BYTE(0x0f),BYTE(0x20)}) {
                xout=true;tick(false,false);bool answered=true,silent=true;
                for(int i=0;i<8;i++){
                    // DOUT shows the bit before BUSY rises, which is when the disk looks.
                    now+=100;p.tick(now,xout,false,c>>i&1);disk.tick(now,xout,false,(c>>i&1)?4:0);
                    answered&=disk.ack;silent&=!disk.ack;strobe(c>>i&1);
                }
                xout=false;tick(false,false);
                check(c&1?answered:silent,c&1?"CE140F answers every bit of 0Fh":"CE140F stays silent through 20h");
            }
            check((disk.status()&4)==0 && disk.canSnapshot(),"CE140F idle after the printer's codes");
            xout=true;tick(false,false);check(!p.ack && p.status()==0,"CE126P disconnected stays silent");
            fs::remove(path);
        }
        std::cout<<"PASS: "<<checks<<" CPU, bus, keyboard, LCD, timer, snapshot and printer checks\n";return 0;
    }catch(const std::exception &e){std::cerr<<"FAIL: "<<e.what()<<"\n";return 1;}
}
