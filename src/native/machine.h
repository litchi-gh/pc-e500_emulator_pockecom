// SPDX-License-Identifier: GPL-2.0-or-later
#pragma once
#include "sc62015.h"
#include <array>
#include <algorithm>
#include <deque>
#include "ce140f.h"

struct Lcd {
    std::array<BYTE,512> ram{};
    BYTE page=0,column=0,start=0,on=0;
    BYTE command(int cmd);
};

class Machine {
public:
    std::array<BYTE,0x100000> mem;
    std::array<BYTE,12> keys{};
    Lcd lcd[2]; // controller 1, controller 2
    Csc62015 cpu;
    int internalKB=32,cardKB=0;
    int romKB=256;
    Ce140f disk;
    std::deque<int16_t> audio;
    uint64_t samplePhase=0,tonePhase=0;
    int lastSoundMode=-1;
    bool xout=false;
    UINT32 watchAddr=0xffffffff;
    UINT32 watchHits=0;
    Machine():cpu(this) {mem.fill(0xff);}
    bool isRam(UINT32 a) const {
        return (a>=0x80000 && a<0xc0000) || (cardKB && a>=0x40000 && a<0x80000);
    }
    UINT32 mirror(UINT32 a) {
        a&=0xfffff;
        if(a>=0x80000 && a<0xc0000)return (a&(internalKB*1024-1))|(0xc0000-internalKB*1024);
        if(cardKB && a>=0x40000 && a<0x80000)return (a&(cardKB*1024-1))|0x40000;
        return a;
    }
    bool toneLevel(int mode) const {
        switch(mode) {
        case 1:case 5:return true;
        case 2:return ((tonePhase/256)&1)==0;
        case 3:return ((tonePhase/128)&1)==0;
        case 6:case 7:return (cpu.imem[IMEM_SSR]&2)!=0;
        default:return false;
        }
    }
    BYTE display(int cmd,BYTE d);
    BYTE Get_8(UINT32 a);
    BYTE Get_PC(UINT32 a) {return Get_8(a);}
    WORD Get_16(UINT32 a) {return Get_8(a)|(Get_8(a+1)<<8);}
    UINT32 Get_24(UINT32 a) {return Get_16(a)|(Get_8(a+2)<<16);}
    UINT32 Get_20(UINT32 a) {return Get_24(a)&0xfffff;}
    void Set_8(UINT32 a,BYTE d);
    void Set_16(UINT32 a,WORD d) {Set_8(a,d);Set_8(a+1,d>>8);}
    void Set_24(UINT32 a,UINT32 d) {Set_16(a,d);Set_8(a+2,d>>16);}
    void Set_20(UINT32 a,UINT32 d) {Set_16(a,d);Set_8(a+2,(d>>16)&15);}
    void scanKeys();
    void step();
    void pixels(BYTE *out);
    void peripherals(BYTE cycles);
};
