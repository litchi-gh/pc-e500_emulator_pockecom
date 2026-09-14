// SPDX-License-Identifier: GPL-2.0-or-later
// Protocol reference: ../../../../Sharp_ce140f_emul/{main.cpp,commands.cpp,protocol.md}
#pragma once
#include <cstdint>
#include <filesystem>
#include <vector>
#include <array>
#include <string>

class Ce140f {
public:
    bool ack=false;
    uint8_t lines=0;
    uint8_t portL=0,portH=0;
    bool mount(const wchar_t *path,bool protect);
    void tick(uint64_t now,bool xout,bool busy,uint8_t data);
    int status() const {return mounted?(1|(readOnly?2:0)|(phase!=Idle?4:0)|(error?8:0)|(!canSnapshot()?16:0)):0;}
    int trace(char *out,int length);
    bool canSnapshot() const;
    void resetBus();
    std::vector<uint8_t> command(const std::vector<uint8_t>& packet);
private:
    enum Phase {Idle,Device,Selected,SelectedAck,Receive,Transmit,TransmitAck,TransmitGap} phase=Idle;
    bool mounted=false,readOnly=false,error=false,oldBusy=false,oldXout=false;
    uint64_t deadline=0,lastByte=0,lastActivity=0;
    int bits=0,nibble=0,device=0,txNibble=0,continuation=0;
    uint8_t low=0;
    std::vector<uint8_t> rx,tx;
    std::filesystem::path root;
    std::string log;
    struct File {std::string name;std::vector<uint8_t> bytes;size_t position=0;int mode=0;};
    File transfer;
    std::array<File,6> files;
    int printFile=0,expectedSize=0,listIndex=-1;
    std::vector<std::string> listing;
    std::filesystem::path safePath(const std::string& name) const;
    std::string fileName(const std::vector<uint8_t>& data,size_t offset) const;
    std::vector<uint8_t> read(const std::string& name) const;
    void write(const std::string& name,const std::vector<uint8_t>& bytes);
    bool protectedFile(const std::string& name) const;
    void requireWrite(const std::string& name) const;
    void note(const std::string& text);
};
