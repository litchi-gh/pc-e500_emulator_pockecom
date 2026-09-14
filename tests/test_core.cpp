// SPDX-License-Identifier: GPL-2.0-or-later
#include "../src/native/machine.h"
#include <memory>
#include <iostream>
#include <vector>
#include <stdexcept>
#include <fstream>
#include <chrono>
extern "C" int e500_state_size();
extern "C" int e500_save(Machine*,BYTE*,int);
extern "C" int e500_load(Machine*,const BYTE*,int);
extern "C" int e500_memory(Machine*,int,BYTE*,int,int);
extern "C" int e500_audio(Machine*,int16_t*,int);
extern "C" void e500_reset(Machine*,int);
extern "C" Machine* e500_create(const BYTE*,int);
extern "C" Machine* e500_create_config(const BYTE*,int,int,int);
extern "C" void e500_destroy(Machine*);
extern "C" int e500_write(Machine*,int,const BYTE*,int);
extern "C" int e500_dump(Machine*,int,BYTE*,int);

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
        m->Set_8(0xb8042,0);c.reg.r.a=99;m->lcd[0].on=0;
        check(e500_load(m.get(),state.data(),int(state.size()))==1,"load snapshot");
        check(m->Get_8(0xb8042)==0x37 && c.reg.r.a==0 && m->lcd[0].on,"snapshot restores CPU/RAM/LCD");
        state[0]^=0xff;check(!e500_load(m.get(),state.data(),int(state.size())),"reject invalid snapshot");
        check(m->Get_8(0xb8042)==0x37,"failed load preserves machine");
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
            auto bad=filename(0x10,"..\\EVIL");check(disk.command(packet(bad))==std::vector<BYTE>{255},"CE rejects path traversal");
            disk.mount(path.c_str(),true);check(disk.command(packet(filename(0x0a,"TEST")))==std::vector<BYTE>{255} && fs::exists(path/"TEST.BAS"),"write protection enforces KILL");
            // Only the specifically created file and empty test directory are removed.
            fs::remove(path/"TEST.BAS");fs::remove(path);
        }
        std::cout<<"PASS: "<<checks<<" CPU, bus, keyboard, LCD, timer and snapshot checks\n";return 0;
    }catch(const std::exception &e){std::cerr<<"FAIL: "<<e.what()<<"\n";return 1;}
}
