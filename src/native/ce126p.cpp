// SPDX-License-Identifier: GPL-2.0-or-later
// CE-126P プリンタの実装（仕様は ce126p.h を参照）。
#include "ce126p.h"
#include <algorithm>

// The PC-E500 ROM selects the printer with device code 0Fh (presence check)
// or 20h (a line follows). Both come one bit at a time on DOUT, LSB first.
// プリンタを選ぶデバイスコード：0Fh（有無の確認）と 20h（行が続く）。
static bool printerCode(uint8_t code) {return code==0x0f || code==0x20;}
// 読み出されていない紙バッファの上限（バイト）。
static constexpr size_t MaxPaper=64*1024;
// A line is a few milliseconds of bits; a second without one means the ROM gave
// up (BREAK or a timeout), and the half-sent line is dropped.
// バスが動かないまま選択を終えるまでの時間（1秒）。
static constexpr uint64_t Timeout=CpuHz;

// The time goes too: a RESET starts the cycle count over, and an old one would
// make a later snapshot look as if it came from the future. The lines' last
// levels stay: they are the bus's, not the printer's.
// 時刻も戻す（RESET でサイクル数が 0 に戻るため、古い時刻が残ると保存した状態が不正に見える）。
// 線の前回の状態（oldBusy・oldXout）は残す。
void Ce126p::resetBus() {phase=code.resume=Idle;ack=false;code.start();bits=0;value=0;line.clear();lastActivity=0;}

// The inverse of the ROM's conversion at EA9C9h, so lines come out in the
// PC-E500 character set and print with the PC-E500 font.
// ROM（EA9C9h）は、プリンタの文字に合わせて一部の文字コードを入れ替えて送る。
// '0' と 'O'、記号（♠♥♦♣・円年月日）を元の PC-E500 の文字コードに戻す。
uint8_t Ce126p::toE500(uint8_t c) {
    if(c==0x30)return 'O';
    if(c==0xf0)return '0';
    if(c>=0xf5 && c<=0xf8)return c-0x0d;
    if(c==0xf4)return 0xf1;
    if(c>=0xf1 && c<=0xf3)return c+1;
    return c;
}

void Ce126p::byte(uint8_t b) {
    if(b==0x0d) {
        // 0Dh：行の終わり。行末の空白を除き、文字コードを戻して紙に送る。
        while(!line.empty() && line.back()==' ')line.pop_back();
        for(auto c:line)paper.push_back(toE500(c));
        paper.push_back('\n');
        // Unread, the oldest lines go first, as on the window's roll.
        // 紙バッファがあふれたら、古い行から1行ずつ捨てる。
        while(paper.size()>MaxPaper)paper.erase(paper.begin(),std::find(paper.begin(),paper.end(),'\n')+1);
        line.clear();
    } else if(b==0x0e || b==0x0f) {
        // 0Eh closes a line and 0Fh is the presence check: both print nothing
        // and end the selection, so later BUSY strobes are not taken as data.
        // 0Eh（行の後始末）と 0Fh（有無の確認）：何も印字せず、選択を終える。
        phase=Idle;ack=false;
    } else if(b>=0x20 && line.size()<Columns) line.push_back(b);  // 文字：24桁まで行にためる
}

void Ce126p::tick(uint64_t now,bool xout,bool busy,bool dout) {
    if(!connected){ack=false;oldBusy=busy;oldXout=xout;return;}
    // BUSY の立ち上がり・立ち下がりを検出し、バスが動いた時刻を記録する。
    bool rise=busy&&!oldBusy,fall=!busy&&oldBusy;
    if(rise||fall||xout!=oldXout)lastActivity=now;
    // Like SharpManager's CE-126P, answer every bit of any device code: which
    // device is meant is only known after the eighth. The CE-140F's 41h is then
    // handshaken by both, and without a disk the ROM still fails at the same
    // point, waiting for the disk's own acknowledge after the code. At the rise
    // of XOUT a tone (BEEP, cassette) cannot be told from a code either, so ACK
    // follows a tone's high halves, as it would from any device answering XOUT.
    // A line in progress is kept aside, as the CE-140F keeps a transfer: a tone
    // goes back to it, and only a full code ends it.
    // XOUT の立ち上がりで、どの機器宛てでもデバイスコードの受信を始める。
    // 行の途中なら、戻り先（resume）に残しておく。
    if(xout&&!oldXout) {code.begin(phase,Device);ack=true;}
    switch(phase) {
    case Idle:break;
    case Device:
        // ACK high: ready for a bit. The ROM raises BUSY, we take DOUT and drop
        // ACK, it drops BUSY, we raise ACK for the next bit.
        // デバイスコード：ACK High で待ち、BUSY の立ち上がりで DOUT を1ビット取って
        // ACK を下げ、BUSY の立ち下がりで次のビットのために ACK を上げる。
        if(rise){code.rise(dout);ack=false;}
        if(fall)ack=!code.complete();
        if(!xout) {
            if(code.tone(phase,xout)) {
                // A BEEP or a cassette tone also moves XOUT; only a full code selects.
                // 8ビットに満たなければ音だったとみなし、元の段階に戻る。行の途中なら、
                // ACK はバイトの手順どおり（BUSY が High の間は High）。
                ack=phase==Data && busy;
            } else {
                // XOUT が下がったらコードの終わり。0Fh/20h なら選択され、新しい行を始める。
                // ほかの機器のコードなら、行の途中でも選択を終える。
                bool selected=printerCode(code.value);
                if(selected)line.clear();
                phase=selected?Data:Idle;ack=false;bits=0;value=0;
            }
            code.resume=Idle;
        }
        break;
    case Data:
        // The other way round: ACK goes high to take a bit and low to finish it.
        // 印字データ：デバイスコードとは逆に、BUSY の立ち上がりで ACK を上げてビットを取り、
        // 立ち下がりで ACK を下げる。8ビットそろったら1バイトとして処理する。
        if(rise && bits<8){value|=uint8_t(dout)<<bits++;ack=true;}
        if(fall){ack=false;if(bits==8){byte(value);bits=0;value=0;}}
        break;
    }
    // A second with the bus still ends the selection, and so too a code from
    // idle still waiting for its first bit (see BusCode::unanswered): XOUT
    // held high then is no code, and ACK must not stay up on the line.
    // 1秒バスが動かなければ選択を終える。待機から始まって最初のビットを待つコードも同じ
    // （XOUT が High に保たれているだけなので、ACK を上げたままにしない）。
    if(phase!=Idle && now-lastActivity>Timeout)resetBus();
    oldBusy=busy;oldXout=xout;
}

Ce126p::State Ce126p::save() const {
    State s{};
    s.phase=uint8_t(phase);s.ack=ack;s.oldBusy=oldBusy;s.oldXout=oldXout;
    s.length=uint8_t(line.size());std::copy(line.begin(),line.end(),s.line);
    // Idle, the rest means nothing: the next XOUT rise sets it afresh.
    // 待機中は、コード・バイト・時刻に意味がないので 0 のまま（次の XOUT の立ち上がりで決まる）。
    if(phase==Idle)return s;
    s.bits=uint8_t(bits);s.value=value;s.lastActivity=lastActivity;
    if(phase==Device){s.resume=uint8_t(code.resume);s.codeBits=code.bits;s.codeValue=code.value;}
    return s;
}

bool Ce126p::valid(const State& s,uint64_t now) {
    // 各項目の範囲を確かめる。
    if(s.phase>Data || s.ack>1 || s.oldBusy>1 || s.oldXout>1 || s.length>Columns)return false;
    // Reserved bytes are zero, so that a later format can give them a meaning.
    // 予約のバイトは 0（将来の形式で意味を持たせられるように）。
    if(!std::all_of(std::begin(s.reserved),std::end(s.reserved),[](uint8_t c){return c==0;}))return false;
    if(!DeviceCode::valid(s.codeBits,s.codeValue) || !DeviceCode::valid(s.bits,s.value))return false;
    // The line holds only what byte() keeps - characters from 20h - and nothing past its length.
    // 行には byte() がためる文字（20h 以上）だけがあり、長さより後ろは 0。
    if(!std::all_of(s.line,s.line+s.length,[](uint8_t c){return c>=0x20;}) ||
       !std::all_of(s.line+s.length,s.line+Columns,[](uint8_t c){return c==0;}))return false;
    // And the fields together as tick leaves them: idle has nothing in hand (a
    // half line may wait there, for the next selection to clear); a code runs
    // with XOUT high, from idle or from a line, and a line with XOUT low; a
    // line's eighth bit waits only for BUSY to fall.
    // 項目どうしの整合を確かめる（tick が作りうる組み合わせだけを通す）。
    if(s.phase==Idle)return !s.resume && !s.codeBits && !s.codeValue && !s.bits && !s.value && !s.ack && !s.lastActivity;
    if(s.lastActivity>now)return false;
    if(s.phase==Device)return s.oldXout && (s.resume==Data || (s.resume==Idle && !s.bits && !s.value));
    return !s.resume && !s.codeBits && !s.codeValue && !s.oldXout && (s.bits<8 || s.oldBusy);
}

bool Ce126p::restore(const State& s,uint64_t now) {
    if(!valid(s,now))return false;
    phase=Phase(s.phase);code.resume=Phase(s.resume);code.bits=s.codeBits;code.value=s.codeValue;
    bits=s.bits;value=s.value;ack=s.ack;oldBusy=s.oldBusy;oldXout=s.oldXout;
    line.assign(s.line,s.line+s.length);lastActivity=s.lastActivity;
    // Unplugged, it has no place on the bus to come back to.
    // 未接続なら、バス上の状態は持たない。
    if(!connected)resetBus();
    return true;
}

int Ce126p::read(uint8_t *out,int length) {
    if(!out||length<=0)return 0;
    int n=std::min(length,int(paper.size()));
    std::copy_n(paper.begin(),n,out);paper.erase(paper.begin(),paper.begin()+n);
    return n;
}
