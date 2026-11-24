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
extern "C" int e500_state_same(const BYTE*,const BYTE*,int);
extern "C" int e500_memory(Machine*,int,BYTE*,int,int);
extern "C" int e500_audio(Machine*,int16_t*,int);
extern "C" void e500_reset(Machine*,int);
extern "C" Machine* e500_create(const BYTE*,int);
extern "C" Machine* e500_create_config(const BYTE*,int,int,int);
extern "C" void e500_destroy(Machine*);
extern "C" int e500_write(Machine*,int,const BYTE*,int);
extern "C" int e500_dump(Machine*,int,BYTE*,int);
extern "C" int e500_peek(Machine*,int);
extern "C" int e500_run(Machine*,int);
extern "C" int e500_can_load(Machine*);
extern "C" int e500_tape_connect(Machine*,int);
extern "C" int e500_tape_remote(Machine*,int);
extern "C" void e500_printer_connect(Machine*,int);
extern "C" int e500_tape_load(Machine*,const uint32_t*,const BYTE*,int);
extern "C" int e500_tape_append(Machine*,const uint32_t*,const BYTE*,int);
extern "C" int e500_tape_play(Machine*);
extern "C" int e500_tape_record(Machine*);
extern "C" int e500_tape_stop(Machine*);
extern "C" int e500_tape_status(Machine*);
extern "C" int e500_tape_read(Machine*,uint32_t*,BYTE*,int);

// 確認の数と、確認の本体（条件が偽なら message の例外で止める）。
int checks=0;
void check(bool condition,const char* message) {++checks;if(!condition)throw std::runtime_error(message);}
void testAmc() {
    auto joined=std::make_unique<Machine>();auto &r=joined->cpu;
    joined->internalKB=32;joined->cardKB=64;r.Reset();
    std::fill(joined->mem.begin()+0xb8000,joined->mem.begin()+0xc0000,0xa5);
    for(UINT32 n=0;n<65536;n++)joined->mem[0x40000+n]=BYTE(n^(n>>8));
    r.imem[IMEM_AMC]=0x8f;
    // The official32KB CE0/64KB CE1 example: virtual A8000..B7FFF
    // immediately precedes populated CE0 B8000..BFFFF.
    check(joined->amcBase()==0xb8000 && joined->mirror(0xa8000)==0x40000 &&
          joined->mirror(0xb7fff)==0x4ffff && joined->mirror(0xb8000)==0xb8000,
          "AMC official contiguous CE1/CE0 mapping");
    joined->Set_8(0xa8123,0x37);
    check(joined->Get_8(0x40123)==0x37 && joined->mem[0xb8123]==0xa5,
          "AMC CPU writes reach physical card without internal-RAM aliasing");
    // CE1 still mirrors below its populated window: the CPU has no CE1
    // capacity register, so mapping cannot depend on the card image length.
    check(joined->mirror(0x98000)==0x40000 && joined->mirror(0xa7fff)==0x4ffff,
          "AMC lower CE0 aliases preserve external card mirroring");
    struct Geometry {BYTE am;UINT32 base;};
    const Geometry geometry[]={{0,0xbf800},{1,0xbf000},{3,0xbe000},{7,0xbc000},
                               {15,0xb8000},{31,0xb0000},{63,0xa0000}};
    for(const auto &g:geometry)for(int card:{2,4,8,16,32,64,128,256}) {
        joined->cardKB=card;r.imem[IMEM_AMC]=BYTE(0x80|g.am);
        UINT32 bytes=UINT32(card)*1024;
        check(joined->amcBase()==g.base,"AMC documented2KB..128KB control codes");
        check(joined->mirror(g.base-1)==0x40000+bytes-1,"AMC last card byte adjoins CE0");
        check(joined->Get_8(g.base)==0xa5,"AMC CE0 remains physical internal RAM");
        if(g.base-bytes>=0x80000)
            check(joined->mirror(g.base-bytes)==0x40000,"AMC first card byte for a window inside CE0 decode");
        check(joined->mirror(0x40000)==0x40000,"AMC preserves direct CE1 access");
        r.imem[IMEM_AMC]|=0x40;
        check(joined->amcBase()==g.base && joined->mirror(g.base-1)==0x40000+bytes-1,
              "AMC reserved bit6 does not change the address translation");
        r.imem[IMEM_AMC]&=0x7f;
        check(joined->mirror(0xa8000)==0xb8000,"AMC disabled preserves ordinary internal mirrors");
    }
    joined->cardKB=64;r.imem[IMEM_AMC]=0x8f;
    BYTE patch[]={1,2,3,4},got[4]{};
    check(e500_write(joined.get(),0xb7ffe,patch,4)==1 &&
          joined->mem[0x4fffe]==1 && joined->mem[0x4ffff]==2 &&
          joined->mem[0xb8000]==3 && joined->mem[0xb8001]==4,
          "AMC debugger write atomically crosses distinct card/internal banks");
    check(e500_dump(joined.get(),0xb7ffe,got,4)==1 && std::equal(got,got+4,patch),
          "AMC debugger dump follows the contiguous logical view");
    std::vector<BYTE> tooLong(98305,0x73);
    check(!e500_write(joined.get(),0xa8000,tooLong.data(),int(tooLong.size())) &&
          joined->mem[0x4fffe]==1 && joined->mem[0xb8000]==3,
          "AMC debugger rejects a range reaching ROM before any bank is changed");
    std::vector<BYTE> wrapping(65537,0x71);
    BYTE guard=joined->mem[0x48000];
    check(!e500_write(joined.get(),0x80000,wrapping.data(),int(wrapping.size())) &&
          joined->mem[0x48000]==guard,"AMC debugger still rejects wrapping card aliases atomically");
    BYTE ownCode[]={0x08,42,0xde}; // MV A,42 / HALT; no manufacturer ROM.
    check(e500_write(joined.get(),0xb7fff,ownCode,3)==1,"AMC own instruction spans the bank boundary");
    r.reg.x.p=0xb7fff;r.halt=false;r.step();r.step();
    check(r.reg.r.a==42 && r.halt && r.reg.x.p==0xb8002 && r.imem[IMEM_AMC]==0x8f,
          "AMC instruction/operand fetch crosses CE1/CE0 and HALT retains control");
    std::vector<BYTE> state(e500_state_size());
    check(e500_save(joined.get(),state.data(),int(state.size()))==1,"AMC idle snapshot save");
    r.imem[IMEM_AMC]=0;joined->mem[0x40000]=0;
    check(e500_load(joined.get(),state.data(),int(state.size()))==1 &&
          r.imem[IMEM_AMC]==0x8f && joined->mirror(0xa8000)==0x40000,
          "AMC snapshot restores the control and physical memory without a format change");
    r.imem[0xfe]=0xe3;r.Reset();
    check(r.imem[IMEM_AMC]==0x0f && r.imem[0xfe]==0xe3,
          "AMC reset disables AME, retains size bits and leaves LCC bit7 intact");
    auto absent=std::make_unique<Machine>();absent->cpu.imem[IMEM_AMC]=0x8f;
    absent->Set_8(0xa8000,0x42);
    check(absent->Get_8(0xa8000)==0xff && absent->mem[0xb8000]==0xff,
          "AMC absent card does not fall through to an internal-RAM mirror");
}
int main() {
    try {
        testAmc();
        auto m=std::make_unique<Machine>();auto &c=m->cpu;
        m->mem[0xffffd]=0x00;m->mem[0xffffe]=0x01;m->mem[0xfffff]=0x0c;
        c.Reset();check(c.reg.x.p==0xc0100,"reset vector");
        {
            auto recorder=std::make_unique<Machine>();auto &r=recorder->cpu;r.Reset();
            uint32_t durations[]={13,9,31};BYTE levels[]={1,0,1};
            check(e500_tape_connect(recorder.get(),1)==1,"cassette connection");
            check(e500_tape_load(recorder.get(),durations,levels,3)==1,"cassette host pulse input");
            uint32_t invalid[]={0};
            check(!e500_tape_load(recorder.get(),invalid,levels,1)&&recorder->tape.pendingInput()==3,"cassette invalid input leaves loaded waveform intact");
            check(!e500_can_save(recorder.get())&&!e500_can_load(recorder.get()),"pending external cassette input rejects snapshots");
            check(e500_tape_play(recorder.get())==1&&!(r.imem[IMEM_SSR]&2),"CI input disabled without SCR BZ2");
            r.imem[IMEM_SCR]=0x40;r.AddState(12);
            check((r.imem[IMEM_SSR]&2)&&!recorder->toneLevel(4),"enabled CI high with independent low CO mode");
            r.AddState(1);check(!(r.imem[IMEM_SSR]&2),"CI changes at the pulse's exact end cycle");
            r.imem[IMEM_SCR]=0x60;r.AddState(9);
            check((r.imem[IMEM_SSR]&2)&&recorder->toneLevel(6),"CO mode6 mirrors physical CI");
            r.imem[IMEM_SCR]=0;r.AddState(1);
            check(!(r.imem[IMEM_SSR]&2),"disabling input gate clears CI without rewinding playback");
            r.imem[IMEM_SCR]=0x70;r.AddState(1);
            check((r.imem[IMEM_SSR]&2)&&recorder->toneLevel(7),"CO mode7 mirrors the ongoing input pulse");
            r.AddState(29);
            check(!(r.imem[IMEM_SSR]&2)&&!recorder->tape.playing(),"exhausted cassette waveform returns CI low");
            int16_t samples[8]{};
            check(e500_audio(recorder.get(),samples,8)==3&&samples[0]==-6000&&samples[1]==6000&&samples[2]==6000,"cassette CI monitor reaches real48kHz audio at sample cycles");
            std::vector<BYTE> saved(e500_state_size());
            check(e500_save(recorder.get(),saved.data(),int(saved.size()))==1,"idle cassette permits snapshot");
            r.imem[IMEM_SCR]=0;
            check(e500_tape_record(recorder.get())==1&&!e500_can_save(recorder.get()),"cassette recording rejects rollback snapshot");
            r.AddState(10);r.imem[IMEM_SCR]=0x10;r.AddState(1);
            r.AddState(9);r.imem[IMEM_SCR]=0;r.AddState(1);
            check(e500_tape_stop(recorder.get())==1&&(e500_tape_status(recorder.get())&32),"cassette CO output capture closes");
            check(e500_load(recorder.get(),saved.data(),int(saved.size()))==1,"idle state load with external cassette output");
            e500_reset(recorder.get(),0);
            uint32_t got[4]{};BYTE high[4]{};
            check(e500_tape_read(recorder.get(),got,high,4)==2&&got[0]==11&&got[1]==10&&!high[0]&&high[1],"snapshot load and reset retain exact external output history");
            check(e500_tape_connect(nullptr,1)==0&&e500_tape_load(recorder.get(),nullptr,nullptr,-1)==0&&
                  e500_tape_read(recorder.get(),nullptr,nullptr,1)==0,"cassette public API rejects malformed buffers/counts");
        }
        {
            auto oscillator=std::make_unique<Machine>();oscillator->cpu.Reset();
            e500_tape_connect(oscillator.get(),1);oscillator->cpu.imem[IMEM_SCR]=0x20;
            check(e500_tape_record(oscillator.get())==1,"cassette records hardware tone output");
            oscillator->cpu.AddState(255);oscillator->cpu.AddState(255);
            e500_tape_stop(oscillator.get());uint32_t cycles[4]{};BYTE high[4]{};
            check(e500_tape_read(oscillator.get(),cycles,high,4)==3&&
                  cycles[0]==192&&cycles[1]==192&&cycles[2]==126&&high[0]&&!high[1]&&high[2],
                  "2kHz cassette output records exact edges inside long peripheral steps");
        }
        {
            auto stream=std::make_unique<Machine>();stream->cpu.Reset();
            stream->cpu.imem[IMEM_SCR]=0x40;e500_tape_connect(stream.get(),1);
            uint32_t duration=13;BYTE high=1;
            check(e500_tape_load(stream.get(),&duration,&high,1)&&e500_tape_play(stream.get()),"cassette streamed playback begins");
            stream->cpu.AddState(5);uint32_t more[]={9,31};BYTE levels[]={0,1};
            check(e500_tape_append(stream.get(),more,levels,2)==1,"append cassette pulses while the current pulse is partially consumed");
            stream->cpu.AddState(8);check(!(stream->cpu.imem[IMEM_SSR]&2),"append retains the original current-pulse deadline");
            stream->cpu.AddState(9);check(stream->cpu.imem[IMEM_SSR]&2,"appended pulse follows without a playback gap");
            stream->cpu.AddState(31);check(!stream->tape.busy(),"streamed cassette input drains at its exact final cycle");
        }
        {
            auto remote=std::make_unique<Machine>();auto &r=remote->cpu;r.Reset();
            auto pins=[&](bool xout,bool busy,bool data) {
                r.imem[IMEM_SCR]=xout?0x10:0;
                r.imem[IMEM_EOL]=busy?0x40:0;
                r.imem[IMEM_EOH]=data?4:0;
                r.AddState(1);
            };
            auto device=[&](BYTE value,int bits=8) {
                pins(true,false,false);
                for(int n=0;n<bits;n++){pins(true,true,(value>>n)&1);pins(true,false,false);}
                pins(false,false,false);
            };
            check(!e500_tape_remote(nullptr,1)&&!e500_tape_remote(remote.get(),2),"cassette remote API validates machine and boolean");
            check(e500_tape_connect(remote.get(),1)&&e500_tape_remote(remote.get(),1),"cassette remote cable configured");
            uint32_t durations[]={1000,1000};BYTE levels[]={1,0};
            check(e500_tape_load(remote.get(),durations,levels,2)&&e500_tape_play(remote.get()),"remote playback armed before motor start");
            r.imem[IMEM_SCR]=0x40;r.AddState(200);
            check(!(r.imem[IMEM_SSR]&2)&&(e500_tape_status(remote.get())&0xca)==0x4a,"stopped remote motor retains pending input and keeps CI low");
            check(!e500_tape_remote(remote.get(),0)&&!e500_can_save(remote.get()),"armed remote playback rejects configuration and snapshot rollback");
            device(0x10,7);check(!remote->printer.remoteMotor,"partial motor code does not start deck");
            device(0x10);check(remote->printer.remoteMotor&&(e500_tape_status(remote.get())&128),"complete10h starts external cassette motor");
            r.imem[IMEM_SCR]=0x40;r.AddState(5);
            check(r.imem[IMEM_SSR]&2,"remote motor advances actual CI waveform");
            device(0x41);check(remote->printer.remoteMotor,"another device code retains cassette motor");
            device(0x00);check(!remote->printer.remoteMotor&&!(e500_tape_status(remote.get())&128),"complete00h stops cassette motor");
            r.imem[IMEM_SCR]=0x40;r.AddState(200);
            check(!(r.imem[IMEM_SSR]&2)&&remote->tape.pendingInput()==2,"stopped motor freezes a partially consumed input pulse");
            device(0x10);r.imem[IMEM_SCR]=0x40;r.AddState(1);
            check(r.imem[IMEM_SSR]&2,"motor restart resumes retained pulse");
            e500_printer_connect(remote.get(),0);
            check(!remote->printer.remoteMotor&&!(r.imem[IMEM_SSR]&2)&&!(e500_tape_status(remote.get())&128),"unplugged CE126P stops motor and immediately clears CI");
            device(0x10);check(!remote->printer.remoteMotor,"disconnected adapter ignores complete motor code");
            e500_printer_connect(remote.get(),1);device(0x10);
            check(remote->printer.remoteMotor,"reconnected adapter accepts next motor command");
            e500_tape_stop(remote.get());e500_tape_load(remote.get(),nullptr,nullptr,0);
            std::vector<BYTE> saved(e500_state_size());
            check(e500_save(remote.get(),saved.data(),int(saved.size()))==1,"idle remote motor allows CPU snapshot");
            device(0x00);check(e500_load(remote.get(),saved.data(),int(saved.size()))==1,"CPU snapshot reload with external motor stopped");
            check(!remote->printer.remoteMotor&&!(e500_tape_status(remote.get())&128),"CPU snapshot does not rewind physical motor state");
            device(0x10);e500_reset(remote.get(),0);
            check(remote->printer.remoteMotor&&(e500_tape_status(remote.get())&0xc0)==0xc0,"CPU reset preserves powered external motor and cable preference");
        }
        {
            auto uart=std::make_unique<Machine>();auto &u=uart->cpu;
            u.Reset();uart->serial.connect(true,true,true,false);
            u.Set_i8(0xf7,0x6c,0); // 9600, no parity, eight bits, one stop.
            auto frame=uart->serial.frameCycles(0x6c);
            check(frame==800,"UART frame duration derives from UCR and CPU clock");
            u.Set_i8(0xfa,'A',0);
            check(!(u.imem[0xf8]&8),"UART TX holding register becomes occupied");
            uart->serial.tick(1,true,u.imem);
            check((u.imem[0xf8]&0x18)==8 && (u.imem[0xfc]&0x10),"UART holding-to-shifter and TX interrupt");
            auto remaining=uart->serial.txCycles;
            uart->serial.tick(1000,false,u.imem);
            check(uart->serial.txCycles==remaining,"UART stops its clock during HALT/OFF");
            uart->serial.tick(frame,true,u.imem);
            BYTE out=0;check(uart->serial.receive(&out,1)==1 && out=='A' && (u.imem[0xf8]&0x18)==0x18,"UART framed transmit completes");
            BYTE incoming='B';check(uart->serial.inject(&incoming,1)==1,"UART host input accepted");
            uart->serial.tick(frame-1,true,u.imem);
            check(!(u.imem[0xf8]&0x20),"UART does not deliver an incomplete frame");
            uart->serial.tick(1,true,u.imem);
            check((u.imem[0xf8]&0x20) && (u.imem[0xfc]&0x20),"UART RX ready and interrupt");
            check(u.Get_i8(0xf9,0)=='B',"UART RXD read returns received byte");
            uart->serial.tick(1,true,u.imem);
            check(!(u.imem[0xf8]&0x20),"UART RXD read clears RXR after the instruction");
            u.Set_i8(0xf7,0x6e,0);incoming=0xff;
            uart->serial.inject(&incoming,1,1);
            uart->serial.tick(uart->serial.frameCycles(0x6e),true,u.imem);
            check(u.imem[0xf9]==0x7f && (u.imem[0xf8]&1),"UART seven-bit receive and parity status");
            incoming='C';uart->serial.inject(&incoming,1);
            check(!uart->serial.quiescent(),"UART in-flight host input cannot be snapshotted");
            uart->serial.tick(uart->serial.frameCycles(0x6e),true,u.imem);
            check(u.imem[0xf8]&2,"UART unread receive reports overrun");
            u.Set_i8(0xf7,0,0);
            check((u.imem[0xf8]&0x3f)==0x18,"UART disabled control resets its status");
        }
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
        // The sub timer is 64 Hz: 12000 cycles at 768 kHz, not the 2 ms PockEmul uses.
        c.imem[IMEM_ISR]=0;for(int i=0;i<47;i++)c.AddState(255);
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
        // v5 appends the ROM (size and checksum, 8 bytes) and the prefix flag with 7 reserved bytes.
        // 形式5は末尾に ROM（容量とチェックサム、8バイト）と、プリフィクスの途中か（1バイト）、予約7バイトを足す。
        constexpr size_t V5Extra=16;
        {
            std::vector<BYTE> v2(state.size()-V5Extra-sizeof(Ce126p::State)-sizeof(Ce140f::CodeState));
            std::copy_n(state.begin(),v2.size(),v2.begin());memcpy(v2.data(),"E500ST02",8);v2[12]=16;
            check(e500_state_card(v2.data(),int(v2.size()))==16,"card capacity read from a v2 snapshot");
            memcpy(v2.data(),"E500ST99",8);check(e500_state_card(v2.data(),int(v2.size()))==-1,"unknown snapshot layout refused");
            auto future=state;memcpy(future.data(),"E500ST06",8);check(e500_state_card(future.data(),int(future.size()))==-1,"unknown magic refused");
            std::vector<BYTE> v4(state.begin(),state.end()-V5Extra);memcpy(v4.data(),"E500ST04",8);
            check(e500_state_card(v4.data(),int(v4.size()))==m->cardKB,"card capacity read from a v4 snapshot");
        }
        m->Set_8(0xb8042,0);c.reg.r.a=99;m->lcd[0].on=0;
        check(e500_load(m.get(),state.data(),int(state.size()))==1,"load snapshot");
        check(m->Get_8(0xb8042)==0x37 && c.reg.r.a==0 && m->lcd[0].on,"snapshot restores CPU/RAM/LCD");
        {
            // The printer and the disk see the same lines, and reserved bytes are zero.
            // プリンタとディスクの線の状態が食い違う状態や、予約のバイトが 0 でない状態は断る。
            size_t diskAt=state.size()-V5Extra-sizeof(Ce140f::CodeState),printerAt=diskAt-sizeof(Ce126p::State);
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
            // A v5 snapshot names its ROM: another ROM's or another size's is refused natively.
            // 形式5は ROM を記録する：別の ROM や別の容量の状態は、ネイティブ側で断る。
            size_t romAt=state.size()-V5Extra;
            odd=state;odd[romAt+4]^=1;check(!e500_load(m.get(),odd.data(),int(odd.size())),"reject a snapshot of another ROM");
            odd=state;odd[romAt]=128;check(!e500_load(m.get(),odd.data(),int(odd.size())),"reject a snapshot of another ROM size");
            odd=state;odd[romAt+9]=1;check(!e500_load(m.get(),odd.data(),int(odd.size())),"reject a v5 snapshot with reserved bytes set");
            // Two states apart only in the time run are the same; one in the RAM is not.
            // 経過時間だけが違う状態は同じ、RAM が違えば違う（同じなら自動保存は書き直さない）。
            std::vector<BYTE> later(state.size());auto cycles=c.cycles;c.cycles+=12345;c.ticks2=(c.ticks2+7)%(CpuHz/64);
            check(e500_save(m.get(),later.data(),int(later.size()))==1 && e500_state_same(state.data(),later.data(),int(state.size())),"states apart only in time are the same");
            m->Set_8(0xb8042,0x99);check(e500_save(m.get(),later.data(),int(later.size()))==1 && !e500_state_same(state.data(),later.data(),int(state.size())),"states apart in RAM differ");
            m->Set_8(0xb8042,0x37);c.cycles=cycles;check(e500_load(m.get(),state.data(),int(state.size()))==1,"load snapshot after the comparison");
            // The released v4 layout still loads.
            std::vector<BYTE> v4(state.begin(),state.end()-V5Extra);memcpy(v4.data(),"E500ST04",8);
            check(e500_load(m.get(),v4.data(),int(v4.size()))==1,"load a v4 snapshot");
            // Between steps the addressing is normal, unless a prefix's instruction is still to run.
            // 命令の合間はアドレッシングが通常（プリフィクスの途中を除く）。
            BYTE p1=c.pre_1;c.pre_1=0;std::vector<BYTE> pre(state.size());
            check(e500_save(m.get(),pre.data(),int(pre.size()))==1 && !e500_load(m.get(),pre.data(),int(pre.size())),"reject a stray prefix state");
            c.prefixed=true;
            check(e500_save(m.get(),pre.data(),int(pre.size()))==1 && e500_load(m.get(),pre.data(),int(pre.size()))==1 && c.prefixed && c.pre_1==0,"keep a pending prefix");
            c.prefixed=false;c.pre_1=p1;
            check(e500_load(m.get(),state.data(),int(state.size()))==1,"load snapshot once more");
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
            // A write longer than the RAM it lands in would wrap round the mirror
            // over its own start: refused whole.
            // RAM より長い書き込みは、ミラーで折り返して自分の先頭を上書きするので、全体を断る。
            std::vector<BYTE> big(130*1024,0x77);
            check(!e500_write(configured.get(),0x40000,big.data(),int(big.size())) && configured->Get_8(0x40000)!=0x77,"debugger write longer than the card is refused");
            // A peek changes nothing: a write port of the LCD is not written, the
            // data port does not move its column, and the watch is not counted.
            // バスの読み出しは何も変えない（LCD に書かない、桁を進めない、ウォッチに数えない）。
            auto lcdBefore=configured->lcd[0];configured->watchAddr=0xbfffe;configured->watchHits=0;
            e500_peek(configured.get(),0x2002);e500_peek(configured.get(),0x200b);e500_peek(configured.get(),0xbfffe);
            check(configured->lcd[0].ram==lcdBefore.ram && configured->lcd[0].column==lcdBefore.column && configured->watchHits==0,"a peek changes nothing");
            // A run of prefix bytes is no deeper a call for every byte: a page of
            // them (a runaway into '0'-'7') runs as long as asked and comes back.
            // プリフィクスが続いても呼び出しは深くならない（ページいっぱいでも、指定の時間だけ動いて戻る）。
            for(UINT32 a=0x80000;a<0xc0000;a++)configured->mem[a]=0x30;
            configured->cpu.reg.x.p=0xb8000;configured->cpu.halt=configured->cpu.off=false;configured->cpu.imem[IMEM_IMR]=0;
            check(e500_run(configured.get(),200000)==0 && configured->cpu.prefixed && (configured->cpu.reg.x.p&0xf0000)==0xb0000,"a page of prefix bytes runs without a call per byte");
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
        check(count==800 && edges>=65 && edges<=68,"2kHz XOUT PCM generation");
        // Uneven instruction lengths must not drag the 4 kHz BEEP edges off the
        // sample grid: at 48 kHz every half period is exactly six samples wide.
        // At 768 kHz a sample is 16 cycles and a half period 96, so from phase 0
        // every 4 kHz edge lands right on the sample grid.
        c.imem[IMEM_SCR]=0x30;m->audio.clear();m->samplePhase=0;m->lastSoundMode=-1;
        for(int i=0;i<2250;i++)c.AddState(7);
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
            {
                // The drive's fixes of the 2026-11 review.
                // 2026-11 のレビューで直した CE-140F の動き。
                check(disk.mount(path.c_str(),false),"CE140F mounted for the review checks");
                auto opened=[&](std::string stem,std::string ext,BYTE mode,BYTE number){auto b=filename(0x03,stem,ext);b.push_back(mode);b.push_back(number);return disk.command(packet(b));};
                // APPEND to a missing file starts it; PRINT# is kept until CLOSE, which writes it once.
                check(opened("LOG","TXT",3,2)==std::vector<BYTE>{0} && fs::exists(path/"LOG.TXT"),"APPEND to a missing file creates it");
                check(disk.command(packet({0x15,2}))==std::vector<BYTE>{0} && disk.command(packet({'H','I',13,10,0}))==std::vector<BYTE>{0},"PRINT# a line");
                check(fs::file_size(path/"LOG.TXT")==0,"PRINT# is kept until CLOSE");
                check(disk.command(packet({0x04,2}))==std::vector<BYTE>{0} && fs::file_size(path/"LOG.TXT")==4,"CLOSE writes what PRINT# kept");
                // A line ends at a CR with no LF after it.
                std::ofstream(path/"CR.TXT",std::ios::binary)<<"A\rB\r";
                check(opened("CR","TXT",1,3)==std::vector<BYTE>{0},"OPEN a file with CR line ends");
                auto line=disk.command(packet({0x14,3}));
                check(line.size()==6 && line[1]=='A' && line[2]==13,"INPUT# ends a line at a lone CR");
                disk.command(packet({0x04,3}));
                // A text SAVE is written once, at its end.
                check(disk.command(packet(filename(0x10,"ASCII","TXT")))==std::vector<BYTE>{0} && disk.command(packet({0x16,1}))==std::vector<BYTE>{0},"text SAVE begins");
                check(disk.command(packet({'1','0',' ','E','N','D',13,10}))==std::vector<BYTE>{0} && !fs::exists(path/"ASCII.TXT"),"text SAVE keeps its lines until the end");
                check(disk.command(packet({0x16,1}))==std::vector<BYTE>{0} && disk.command(packet({0x1a}))==std::vector<BYTE>{0} && fs::file_size(path/"ASCII.TXT")==8,"text SAVE writes the file at its end");
                // A LOAD body of exactly 256 bytes has its checksum once.
                {std::ofstream f(path/"B256.BIN",std::ios::binary);for(int i=0;i<256;i++)f.put(char(i));}
                check(disk.command(packet(filename(0x0e,"B256","BIN"))).size()>1,"LOAD of 256 bytes begins");
                check(disk.command(packet({0x0f,1})).size()==1+256+1+1,"a 256-byte body sends its checksum once");
                // No extension, and names that only look like devices.
                check(disk.command(packet(filename(0x10,"PROG","")))==std::vector<BYTE>{0} && disk.command(packet({0x11,0,1,0,0}))==std::vector<BYTE>{0} &&
                      disk.command(packet({0x41}))==std::vector<BYTE>{0} && fs::exists(path/"PROG"),"SAVE with no extension");
                check(disk.command(packet(filename(0x10,"COMP")))==std::vector<BYTE>{0} && disk.command(packet({0x11,0,1,0,0}))==std::vector<BYTE>{0} &&
                      disk.command(packet({0x42}))==std::vector<BYTE>{0},"COMP is not a device name");
                check(disk.command(packet(filename(0x10,"COM1")))==std::vector<BYTE>{255},"COM1 is a device name");
                // FILES lists PROG and COMP.BAS though a name outside the code page sits beside them.
                std::ofstream(path/fs::path(L"\u2603.TXT"))<<"x";
                std::ofstream(path/"lower.bas")<<"x";
                auto listed=disk.command(packet(filename(0x05,"????????","???")));
                // LOG.TXT CR.TXT ASCII.TXT B256.BIN PROG COMP.BAS LOWER.BAS TEST.BAS
                check(listed.size()==3 && listed[1]==8,"FILES lists the disk past a name it cannot show");
                // INIT clears what FILES shows, lower-case names included.
                check(disk.command(packet({0x09}))==std::vector<BYTE>{0} && !fs::exists(path/"lower.bas") && !fs::exists(path/"PROG"),"INIT clears lower-case and extensionless names");
                fs::remove(path/fs::path(L"\u2603.TXT"));
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
