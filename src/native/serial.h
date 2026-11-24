// SPDX-License-Identifier: GPL-2.0-or-later
// SC62015 UART register-level endpoint. Framed bytes, not an electrical waveform.
#pragma once
#include <algorithm>
#include <cstdint>
#include <deque>

struct SerialPort {
    static constexpr size_t MaxQueue=65536;
    struct Frame {uint8_t byte,status;};
    bool connected=false,cts=false,cd=false,loopback=false,rxRead=false;
    bool holding=false,shifting=false;
    uint8_t holdByte=0,shiftByte=0;
    uint32_t txCycles=0,rxCycles=0;
    std::deque<Frame> input;
    std::deque<uint8_t> output;
    void reset() {holding=shifting=rxRead=false;txCycles=rxCycles=0;input.clear();}
    void connect(bool on,bool cs,bool carrier,bool loop) {
        reset();connected=on;cts=on&&cs;cd=on&&carrier;loopback=loop;
    }
    bool quiescent() const {return !holding&&!shifting&&!rxRead&&input.empty();}
    uint32_t frameCycles(uint8_t control) const {
        unsigned code=(control>>4)&7;
        if(!code)return 0;
        unsigned baud=300u<<(code-1);
        unsigned bits=1+((control&2)?7:8)+((control&8)?0:1)+((control&1)?2:1);
        return (768000u*bits+baud-1)/baud;
    }
    void write(uint8_t port,uint8_t byte,uint8_t *imem) {
        if(port==0xf7 && !(byte&0x70)) {
            holding=shifting=rxRead=false;txCycles=rxCycles=0;
            imem[0xf8]=(imem[0xf8]&0xc0)|0x18;
        }
        if(port==0xfa && (imem[0xf7]&0x70)) {
            holdByte=byte;holding=true;imem[0xf8]&=uint8_t(~8);
        }
    }
    void read(uint8_t port) {if(port==0xf9)rxRead=true;}
    int inject(const uint8_t *data,int length,uint8_t status=0) {
        if(!connected||!data||length<0)return 0;
        int n=std::min(length,int(MaxQueue-input.size()));
        for(int i=0;i<n;i++)input.push_back({data[i],uint8_t(status&7)});
        return n;
    }
    int receive(uint8_t *data,int length) {
        if(!data||length<0)return 0;
        int n=std::min(length,int(output.size()));
        for(int i=0;i<n;i++){data[i]=output.front();output.pop_front();}
        return n;
    }
    void tick(unsigned cycles,bool running,uint8_t *imem) {
        imem[0xf5]=(imem[0xf5]&uint8_t(~6))|(cts?4:0)|(cd?2:0);
        if(rxRead){imem[0xf8]&=uint8_t(~0x20);rxRead=false;}
        uint32_t duration=frameCycles(imem[0xf7]);
        if(!duration||!running)return;
        if(shifting) {
            if(cycles>=txCycles) {
                shifting=false;txCycles=0;
                if(connected && output.size()<MaxQueue)output.push_back(shiftByte);
                if(connected && loopback && input.size()<MaxQueue)input.push_back({shiftByte,0});
                imem[0xf8]|=0x10;
            } else txCycles-=cycles;
        }
        if(!shifting && holding) {
            shifting=true;holding=false;shiftByte=holdByte&((imem[0xf7]&2)?0x7f:0xff);
            txCycles=duration;imem[0xf8]=(imem[0xf8]|8)&uint8_t(~0x10);imem[0xfc]|=0x10;
        }
        if(!connected||input.empty()){rxCycles=0;return;}
        if(!rxCycles)rxCycles=duration;
        if(cycles<rxCycles){rxCycles-=cycles;return;}
        Frame f=input.front();input.pop_front();rxCycles=0;
        // A completed second frame records overrun while retaining the unread byte.
        uint8_t overrun=(imem[0xf8]&0x20)?2:0;
        if(!overrun)imem[0xf9]=f.byte&((imem[0xf7]&2)?0x7f:0xff);
        imem[0xf8]=(imem[0xf8]&0xd8)|0x20|f.status|overrun;
        imem[0xfc]|=0x20;
    }
};
