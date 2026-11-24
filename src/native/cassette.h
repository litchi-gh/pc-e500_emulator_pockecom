// SPDX-License-Identifier: GPL-2.0-or-later
// Cycle-timed cassette CI playback and CO capture. This transports physical
// levels, not decoded bytes; tape framing/checksums belong to the ROM driver.
#pragma once
#include <cstddef>
#include <cstdint>
#include <deque>
#include <limits>
#include <vector>

struct CassettePulse {
    uint32_t cycles;
    bool high;
};

class CassetteSignals {
    std::deque<CassettePulse> input_,output_;
    uint64_t clock_=0,remaining_=0,recorded_=0;
    bool connected_=false,playing_=false,recording_=false;
    bool inputHigh_=false,outputHigh_=false,overflow_=false;
    bool remote_=false,motor_=false;
    size_t limit_;

    bool append(uint64_t duration,bool high) {
        constexpr auto largest=std::numeric_limits<uint32_t>::max();
        uint64_t count=duration/largest+(duration%largest!=0);
        if(count>limit_-output_.size()) {
            overflow_=true;recording_=false;return false;
        }
        while(duration) {
            auto part=uint32_t(duration>largest?largest:duration);
            output_.push_back({part,high});duration-=part;
        }
        return true;
    }
public:
    explicit CassetteSignals(size_t pulseLimit=1024*1024):limit_(pulseLimit) {}
    bool connected() const {return connected_;}
    bool playing() const {return playing_;}
    bool recording() const {return recording_;}
    bool remote() const {return remote_;}
    bool motor() const {return motor_;}
    bool moving() const {return connected_&&(!remote_||motor_);}
    bool inputHigh() const {return moving()&&playing_&&inputHigh_;}
    bool overflow() const {return overflow_;}
    size_t pendingInput() const {return input_.size();}
    size_t captured() const {return output_.size();}
    bool busy() const {return playing_||recording_||!input_.empty();}

    bool advance(uint64_t now) {
        if(now<clock_)return false;
        uint64_t elapsed=now-clock_;clock_=now;
        if(!moving())return true;
        if(recording_)recorded_+=elapsed;
        if(!playing_)return true;
        while(elapsed>=remaining_) {
            elapsed-=remaining_;input_.pop_front();
            if(input_.empty()) {
                playing_=false;inputHigh_=false;remaining_=0;return true;
            }
            remaining_=input_.front().cycles;inputHigh_=input_.front().high;
        }
        remaining_-=elapsed;
        return true;
    }
    // The user's remote-cable preference and the external motor are not CPU
    // snapshot state. A stopped motor freezes tape position, never CPU time.
    bool setRemote(bool enabled,uint64_t now) {
        if(playing_||recording_||now<clock_)return false;
        advance(now);remote_=enabled;return true;
    }
    bool setMotor(bool running,uint64_t now,bool level) {
        if(!advance(now))return false;
        motor_=running;
        return observeOutput(now,level);
    }
    bool connect(bool enabled,uint64_t now) {
        if(!advance(now))return false;
        if(!enabled) {
            stopRecording(now);playing_=false;inputHigh_=false;
        }
        connected_=enabled;return true;
    }
    bool load(const std::vector<CassettePulse>& pulses) {
        if(playing_||recording_||pulses.size()>limit_)return false;
        for(auto pulse:pulses)if(!pulse.cycles)return false;
        input_.assign(pulses.begin(),pulses.end());remaining_=0;
        return true;
    }
    bool appendInput(const std::vector<CassettePulse>& pulses) {
        if(recording_||pulses.size()>limit_-input_.size())return false;
        for(auto pulse:pulses)if(!pulse.cycles)return false;
        input_.insert(input_.end(),pulses.begin(),pulses.end());return true;
    }
    bool play(uint64_t now) {
        if(!advance(now)||!connected_||playing_||recording_||input_.empty())return false;
        playing_=true;
        if(!remaining_)remaining_=input_.front().cycles;
        inputHigh_=input_.front().high;
        return true;
    }
    bool stopPlayback(uint64_t now) {
        if(!advance(now))return false;
        playing_=false;inputHigh_=false;return true;
    }
    bool startRecording(uint64_t now,bool level) {
        if(!advance(now)||!connected_||playing_||recording_)return false;
        recording_=true;overflow_=false;recorded_=0;outputHigh_=level;
        return true;
    }
    bool observeOutput(uint64_t now,bool level) {
        if(!advance(now))return false;
        if(recording_&&moving()&&level!=outputHigh_) {
            if(!append(recorded_,outputHigh_))return false;
            recorded_=0;outputHigh_=level;
        }
        return true;
    }
    bool stopRecording(uint64_t now) {
        if(!advance(now))return false;
        bool okay=!recording_||append(recorded_,outputHigh_);
        recording_=false;recorded_=0;return okay;
    }
    size_t read(CassettePulse* destination,size_t count) {
        if(!destination)return 0;
        size_t copied=0;
        while(copied<count&&!output_.empty()) {
            destination[copied++]=output_.front();output_.pop_front();
        }
        return copied;
    }
    void clearInput() {input_.clear();playing_=false;inputHigh_=false;remaining_=0;}
    void clearOutput() {output_.clear();overflow_=false;}
    // CPU reset/state load must not rewind already captured external output.
    void resetTimeline(uint64_t oldTime,uint64_t newTime) {
        stopRecording(oldTime);clearInput();clock_=newTime;recorded_=0;
    }
};
