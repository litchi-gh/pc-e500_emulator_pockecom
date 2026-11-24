// Independent signal tests; no manufacturer's ROM or recorded data is loaded.
#include "../src/native/cassette.h"
#include <cassert>
#include <iostream>

int main() {
    const std::vector<CassettePulse> wave={{13,true},{9,false},{31,true},{27,false},{5,true}};
    for(uint64_t stride: {1,2,7,19,100}) {
        CassetteSignals tape;
        assert(tape.load(wave)&&!tape.play(0));
        assert(tape.connect(true,0)&&tape.play(0));
        uint64_t cursor=0;
        for(uint64_t now=0;now<=100;now+=stride) {
            assert(tape.advance(now));
            uint64_t end=0;bool expected=false;
            for(auto pulse:wave) {
                end+=pulse.cycles;
                if(now<end) {expected=pulse.high;break;}
            }
            assert(tape.inputHigh()==expected);cursor=now;
        }
        assert(tape.advance(101)&&!tape.playing()&&!tape.pendingInput());
        assert(!tape.advance(cursor)); // time cannot be rewound silently
    }
    CassetteSignals capture;
    assert(capture.connect(true,0)&&capture.startRecording(0,true));
    uint64_t time=0;
    for(size_t n=0;n<wave.size();++n) {
        time+=wave[n].cycles;
        bool next=n+1<wave.size()?wave[n+1].high:false;
        assert(capture.observeOutput(time,next));
    }
    assert(capture.stopRecording(time)&&capture.captured()==wave.size());
    CassettePulse got[8]{};
    assert(capture.read(got,8)==wave.size());
    for(size_t n=0;n<wave.size();++n)
        assert(got[n].cycles==wave[n].cycles&&got[n].high==wave[n].high);
    assert(capture.captured()==0&&!capture.busy());

    CassetteSignals invalid;
    assert(invalid.load(wave)&&!invalid.load({{0,true}}));
    assert(invalid.pendingInput()==wave.size());
    assert(invalid.connect(true,0)&&invalid.play(0));
    assert(!invalid.startRecording(0,false)&&!invalid.load(wave));
    assert(invalid.connect(false,1)&&!invalid.inputHigh()&&!invalid.playing());
    assert(invalid.connect(true,1)&&invalid.play(1));
    assert(invalid.advance(7)&&invalid.stopPlayback(7));
    assert(invalid.advance(500)&&invalid.play(500));
    assert(invalid.advance(505)&&invalid.inputHigh());
    assert(invalid.advance(506)&&!invalid.inputHigh());

    CassetteSignals streamed(3);
    assert(streamed.load({{13,true}})&&streamed.connect(true,0)&&streamed.play(0));
    assert(streamed.advance(5)&&streamed.appendInput({{9,false},{31,true}}));
    assert(!streamed.appendInput({{1,false}})&&!streamed.appendInput({{0,false}}));
    assert(streamed.advance(12)&&streamed.inputHigh());
    assert(streamed.advance(13)&&!streamed.inputHigh());
    assert(streamed.advance(22)&&streamed.inputHigh());
    assert(streamed.advance(53)&&!streamed.playing()&&!streamed.pendingInput());

    CassetteSignals limited(2);
    assert(limited.connect(true,0)&&limited.startRecording(0,false));
    assert(limited.observeOutput(3,true)&&limited.observeOutput(7,false));
    assert(!limited.observeOutput(12,true)&&limited.overflow()&&!limited.recording());
    assert(limited.read(got,8)==2&&got[0].cycles==3&&got[1].cycles==4);
    assert(!limited.load(wave));

    CassetteSignals silence;
    uint64_t longPause=uint64_t(UINT32_MAX)+5;
    assert(silence.connect(true,0)&&silence.startRecording(0,false));
    assert(silence.stopRecording(longPause)&&silence.read(got,8)==2);
    assert(got[0].cycles==UINT32_MAX&&got[1].cycles==5&&!got[0].high&&!got[1].high);
    CassetteSignals remote;
    assert(remote.connect(true,0)&&remote.setRemote(true,0)&&remote.remote());
    assert(remote.load({{13,true},{9,false}})&&remote.play(0));
    assert(!remote.inputHigh()&&remote.busy()&&!remote.moving());
    assert(!remote.setRemote(false,0)&&remote.remote());
    assert(remote.advance(1000)&&remote.pendingInput()==2);
    assert(remote.setMotor(true,1000,false)&&remote.inputHigh());
    assert(remote.advance(1005)&&remote.setMotor(false,1005,false));
    assert(remote.advance(2000)&&!remote.inputHigh()&&remote.pendingInput()==2);
    assert(remote.appendInput({{31,true}})&&remote.setMotor(true,2000,false));
    assert(remote.advance(2007)&&remote.inputHigh());
    assert(remote.advance(2008)&&!remote.inputHigh());
    assert(remote.advance(2017)&&remote.inputHigh());
    assert(remote.advance(2048)&&!remote.busy());
    assert(!remote.setMotor(false,2047,false)&&remote.motor());

    CassetteSignals remoteCapture;
    assert(remoteCapture.connect(true,0)&&remoteCapture.setRemote(true,0));
    assert(remoteCapture.startRecording(0,false));
    assert(remoteCapture.observeOutput(1000,true)&&remoteCapture.captured()==0);
    assert(remoteCapture.setMotor(true,1000,true));
    assert(remoteCapture.observeOutput(1013,false));
    assert(remoteCapture.setMotor(false,1022,false));
    assert(remoteCapture.observeOutput(5000,true));
    assert(remoteCapture.setMotor(true,6000,true));
    assert(remoteCapture.stopRecording(6031)&&remoteCapture.read(got,8)==3);
    assert(got[0].cycles==13&&got[0].high&&got[1].cycles==9&&!got[1].high&&
           got[2].cycles==31&&got[2].high);
    assert(remoteCapture.startRecording(6031,true));
    assert(remoteCapture.setMotor(false,6036,true));
    assert(remoteCapture.observeOutput(6046,false)&&remoteCapture.observeOutput(6056,true));
    assert(remoteCapture.setMotor(true,6066,true)&&remoteCapture.stopRecording(6071));
    assert(remoteCapture.read(got,8)==1&&got[0].cycles==10&&got[0].high);
    remoteCapture.resetTimeline(6071,0);
    assert(remoteCapture.remote()&&remoteCapture.motor()&&!remoteCapture.busy());
    std::cout<<"PASS cassette signals: physical cycle levels, irregular stepping, exact capture/playback, atomic load rejection, offline/mutual exclusion, bounded overflow and long silence\n";
    std::cout<<"PASS cassette remote: armed wait, partial-pulse resume/streaming, paused-edge suppression, exact moving-time capture and external motor/reset state\n";
}
