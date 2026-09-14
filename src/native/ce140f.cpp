// SPDX-License-Identifier: GPL-2.0-or-later
// Native adaptation of the protocol documented by ffxx68/Sharp_ce140f_emul.
#include "ce140f.h"
#include <algorithm>
#include <fstream>
#include <numeric>
#include <cstring>
#include <stdexcept>
#define NOMINMAX
#include <windows.h>
namespace fs=std::filesystem;
using Bytes=std::vector<uint8_t>;
static constexpr size_t MaxFile=1024*1024;
static bool reparse(const fs::path &p) {
    auto a=GetFileAttributesW(p.c_str());if(a==INVALID_FILE_ATTRIBUTES || !(a&FILE_ATTRIBUTE_REPARSE_POINT))return false;
    // OneDrive placeholders have reparse metadata but do not redirect the path.
    // Reject name-surrogate tags (symlinks/junctions), not ordinary cloud files.
    HANDLE h=CreateFileW(p.c_str(),0,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT|FILE_FLAG_BACKUP_SEMANTICS,nullptr);
    if(h==INVALID_HANDLE_VALUE)return true;
    FILE_ATTRIBUTE_TAG_INFO info{};bool ok=GetFileInformationByHandleEx(h,FileAttributeTagInfo,&info,sizeof(info));CloseHandle(h);
    return !ok || IsReparseTagNameSurrogate(info.ReparseTag);
}
static Bytes number(uint32_t n,int count) {Bytes b{0};uint8_t sum=0;while(count--){b.push_back(n&255);sum+=n&255;n>>=8;}b.push_back(sum);return b;}
static Bytes checked(Bytes b) {uint8_t sum=std::accumulate(b.begin(),b.end(),0);b.push_back(sum);return b;}
static bool wildcard(const char *p,const char *s) {
    if(!*p)return !*s;
    if(*p=='*')return wildcard(p+1,s)||(*s && wildcard(p,s+1));
    return *s && (*p=='?'||*p==*s) && wildcard(p+1,s+1);
}
void Ce140f::note(const std::string& s) {log+=s+"\n";if(log.size()>16384)log.erase(0,log.size()-12000);}
int Ce140f::trace(char *out,int length) {if(!out||length<1)return 0;int n=std::min(length-1,int(log.size()));memcpy(out,log.data(),n);out[n]=0;return n;}
void Ce140f::resetBus() {phase=Idle;ack=false;lines=0;rx.clear();tx.clear();continuation=0;oldBusy=oldXout=false;nibble=0;}
bool Ce140f::canSnapshot() const {return phase==Idle && continuation==0 && transfer.mode==0 && std::all_of(files.begin(),files.end(),[](const File &f){return f.mode==0;});}
bool Ce140f::mount(const wchar_t *path,bool protect) {
    resetBus();transfer={};files={};error=false;mounted=false;
    if(!path||!*path)return true;
    try {
        fs::path p=fs::absolute(path);
        fs::create_directories(p);
        if(!fs::is_directory(p)||reparse(p))throw std::runtime_error("Invalid disk directory");
        root=fs::canonical(p);readOnly=protect;mounted=true;note("Mounted CE-140F drive X:");return true;
    }catch(const std::exception &e){note(e.what());error=true;return false;}
}
fs::path Ce140f::safePath(const std::string& n) const {
    auto dot=n.find('.');
    if(n.empty()||n.size()>12||dot==0||dot>8||dot==std::string::npos||n.size()-dot-1>3||n.find('.',dot+1)!=std::string::npos)
        throw std::runtime_error("Invalid 8.3 filename");
    for(unsigned char c:n)if(!((c>='A'&&c<='Z')||(c>='0'&&c<='9')||c=='_'||c=='-'||c=='.'))throw std::runtime_error("Unsafe filename");
    std::string stem=n.substr(0,dot);
    if(stem=="CON"||stem=="PRN"||stem=="AUX"||stem=="NUL"||(stem.size()==4&&(stem.substr(0,3)=="COM"||stem.substr(0,3)=="LPT")))throw std::runtime_error("Reserved filename");
    auto p=root/fs::path(n);
    if(reparse(p)||reparse(root)||fs::canonical(root)!=root)throw std::runtime_error("Reparse points are not disk files");
    return p;
}
std::string Ce140f::fileName(const Bytes& b,size_t at) const {
    if(at+12>b.size())throw std::runtime_error("Truncated filename");
    std::string s;for(size_t i=at;i<at+12;i++)if(b[i] && b[i]!=' ')s+=char(toupper(b[i]));return s;
}
Bytes Ce140f::read(const std::string& name) const {
    auto p=safePath(name);if(!fs::is_regular_file(p)||fs::file_size(p)>MaxFile)throw std::runtime_error("File missing or too large");
    std::ifstream f(p,std::ios::binary);Bytes b((std::istreambuf_iterator<char>(f)),{});if(!f.eof() && f.fail())throw std::runtime_error("Disk read failed");return b;
}
bool Ce140f::protectedFile(const std::string& name) const {auto a=GetFileAttributesW(safePath(name).c_str());return a!=INVALID_FILE_ATTRIBUTES && (a&FILE_ATTRIBUTE_READONLY);}
void Ce140f::requireWrite(const std::string& name) const {if(readOnly||protectedFile(name))throw std::runtime_error("Write protected");}
void Ce140f::write(const std::string& name,const Bytes& b) {
    requireWrite(name);if(b.size()>MaxFile)throw std::runtime_error("File too large");
    auto p=safePath(name),tmp=p;tmp+=L".ce140f-tmp";
    if(reparse(tmp))throw std::runtime_error("Unsafe temporary file");
    {std::ofstream f(tmp,std::ios::binary|std::ios::trunc);f.write(reinterpret_cast<const char*>(b.data()),b.size());f.flush();if(!f)throw std::runtime_error("Disk write failed");}
    if(!MoveFileExW(tmp.c_str(),p.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH))throw std::runtime_error("Disk commit failed");
}
Bytes Ce140f::command(const Bytes& packet) {
    if(!mounted)return {0xff};
    if(packet.size()<2 || uint8_t(std::accumulate(packet.begin(),packet.end()-1,0))!=packet.back()) {error=true;note("Checksum error");continuation=0;return {0xff};}
    Bytes b(packet.begin(),packet.end()-1);
    int cmd=continuation?continuation:b[0];continuation=0;
    char label[80];snprintf(label,sizeof(label),"CMD %02X (%zu bytes)",cmd,b.size());note(label);
    std::string hex;for(size_t i=0;i<std::min(size_t(48),b.size());i++){char v[4];snprintf(v,sizeof(v),"%02X ",b[i]);hex+=v;}note(hex);
    auto need=[&](size_t n){if(b.size()<n)throw std::runtime_error("Truncated command");};
    auto openFile=[&]() -> File& {need(2);int n=int(b[1])-2;if(n<0||n>=6||!files[n].mode)throw std::runtime_error("File not open");return files[n];};
    try {
        error=false;
        switch(cmd) {
        case 0x03: {
            need(17);int n=int(b[16])-2,mode=b[15];if(n< -1||n>=6||mode<1||mode>3)throw std::runtime_error("Invalid OPEN");
            auto name=fileName(b,3);safePath(name);File f;f.name=name;f.mode=mode;
            if(mode==1||mode==3)f.bytes=read(name);
            if(mode!=1){requireWrite(name);if(mode==2)write(name,{});f.position=f.bytes.size();}
            if(n==-1)transfer=std::move(f);else files[n]=std::move(f);return {0};
        }
        case 0x04: need(2);if(b[1]==255){files={};transfer={};}else if(b[1]==1)transfer={};else {int n=b[1]-2;if(n<0||n>=6)throw std::runtime_error("Invalid CLOSE");files[n]={};}return {0};
        case 0x05: {
            listing.clear();listIndex=-1;std::string pattern=b.size()>=15?fileName(b,3):"*.*";if(pattern.empty()||pattern==".")pattern="*.*";
            for(auto &entry:fs::directory_iterator(root))if(entry.is_regular_file()&&!reparse(entry.path())) {
                auto name=entry.path().filename().string();std::transform(name.begin(),name.end(),name.begin(),[](unsigned char c){return char(toupper(c));});
                try {safePath(name);if(wildcard(pattern.c_str(),name.c_str()) && listing.size()<255)listing.push_back(name);}catch(...){}
            }
            std::sort(listing.begin(),listing.end());return checked({0,uint8_t(listing.size())});
        }
        case 0x06:case 0x07: {
            listIndex+=cmd==6?1:-1;if(listIndex<0||listIndex>=int(listing.size()))return {0xff};
            auto name=listing[listIndex];auto dot=name.find('.');std::string s="X:"+name.substr(0,dot);s.resize(10,' ');s+="."+name.substr(dot+1);s.resize(15,' ');
            Bytes out{0};out.insert(out.end(),s.begin(),s.end());return checked(out);
        }
        case 0x08: if(readOnly)throw std::runtime_error("Write protected");return {0};
        case 0x09: {
            if(readOnly)throw std::runtime_error("Write protected");
            // Folder-backed virtual disk: format only disk-visible 8.3 files, never recurse.
            std::vector<std::string> names;for(auto &e:fs::directory_iterator(root))if(e.is_regular_file()&&!reparse(e.path())) {
                try {auto n=e.path().filename().string();safePath(n);names.push_back(n);}catch(...){}
            }
            for(auto &n:names)requireWrite(n);
            for(auto &n:names)fs::remove(safePath(n));files={};transfer={};return {0};
        }
        case 0x0a: {auto n=fileName(b,3);requireWrite(n);if(!fs::remove(safePath(n)))throw std::runtime_error("File not found");return {0};}
        case 0x0b:case 0x0d: {
            auto src=fileName(b,3);auto dest=fileName(b,17);requireWrite(dest);
            if(cmd==0x0b){requireWrite(src);if(fs::exists(safePath(dest)))throw std::runtime_error("Destination exists");fs::rename(safePath(src),safePath(dest));}
            else write(dest,read(src));return {0};
        }
        case 0x0c: {
            need(17);if(readOnly)throw std::runtime_error("Write protected");auto p=safePath(fileName(b,3));auto a=GetFileAttributesW(p.c_str());if(a==INVALID_FILE_ATTRIBUTES)throw std::runtime_error("File not found");
            if(!SetFileAttributesW(p.c_str(),(b[16]&1)?a|FILE_ATTRIBUTE_READONLY:a&~FILE_ATTRIBUTE_READONLY))throw std::runtime_error("Attribute write failed");return {0};
        }
        case 0x0e: {transfer={};transfer.name=fileName(b,3);transfer.bytes=read(transfer.name);transfer.mode=1;auto out=number(uint32_t(transfer.bytes.size()),3);out.insert(out.begin()+1,0x20);out.back()+=0x20;return out;}
        case 0x17: {if(transfer.position>=transfer.bytes.size())return {0xff};return checked({0,transfer.bytes[transfer.position++]});}
        case 0x0f: {
            Bytes out{0};uint8_t sum=0;size_t count=0;
            while(transfer.position<transfer.bytes.size()) {auto v=transfer.bytes[transfer.position++];out.push_back(v);sum+=v;if(++count%256==0){out.push_back(sum);sum=0;}}
            out.push_back(sum);out.push_back(0);transfer.mode=0;return out;
        }
        case 0x12: {
            Bytes out{0};bool ended=false;
            while(transfer.position<transfer.bytes.size()) {auto v=transfer.bytes[transfer.position++];if(v==10)continue;out.push_back(v);if(v==13){ended=true;break;}}
            if(!ended){out.push_back(0x1a);transfer.mode=0;}out=checked(out);out.push_back(0);return out;
        }
        case 0x10: {transfer={};transfer.name=fileName(b,3);safePath(transfer.name);requireWrite(transfer.name);transfer.mode=2;return {0};}
        case 0x11: {need(5);expectedSize=b[2]|(b[3]<<8)|(b[4]<<16);if(expectedSize<1||expectedSize>MaxFile)throw std::runtime_error("Invalid SAVE size");transfer.bytes.clear();continuation=0xff;return {0};}
        case 0x16: if(transfer.name.empty())throw std::runtime_error("ASCII file not open");continuation=0xfe;return {0};
        case 0xff: {
            transfer.bytes.insert(transfer.bytes.end(),b.begin(),b.end());
            if(transfer.bytes.size()>size_t(expectedSize))throw std::runtime_error("SAVE overflow");
            if(transfer.bytes.size()==size_t(expectedSize)){write(transfer.name,transfer.bytes);note("Saved "+transfer.name);transfer.mode=0;}
            else continuation=0xff;return {0};
        }
        case 0xfe: {
            if(b[0]==0x1a){write(transfer.name,transfer.bytes);note("Saved ASCII "+transfer.name);transfer.mode=0;}
            else {transfer.bytes.insert(transfer.bytes.end(),b.begin(),b.end());if(transfer.bytes.size()>MaxFile)throw std::runtime_error("File too large");write(transfer.name,transfer.bytes);}
            return {0};
        }
        case 0x15: {auto &f=openFile();if(f.mode==1)throw std::runtime_error("File is input only");printFile=b[1]-2;continuation=0xfd;return {0};}
        case 0xfd: {
            auto &f=files[printFile];if(f.mode<2)throw std::runtime_error("File not writable");
            if(!b.empty()&&b.back()==0)b.pop_back();f.bytes.insert(f.bytes.end(),b.begin(),b.end());
            write(f.name,f.bytes);f.position=f.bytes.size();return {0};
        }
        case 0x13:case 0x14:case 0x20: {
            auto &f=openFile();if(f.mode!=1)throw std::runtime_error("File not readable");Bytes out{0};
            while(f.position<f.bytes.size()){auto v=f.bytes[f.position++];out.push_back(v);if(cmd!=0x20&&v==10)break;}
            out.push_back(0);out=checked(out);out.push_back(0);return out;
        }
        case 0x1a: {auto &f=openFile();return {uint8_t(f.position>=f.bytes.size())};}
        case 0x1b: {auto &f=openFile();return number(uint32_t(f.bytes.size()),3);}
        case 0x1c: {auto &f=openFile();return number(uint32_t((f.position>>8)+1),2);}
        case 0x1d: return number(uint32_t(std::min(uintmax_t(0xffffff),fs::space(root).available)),3);
        default:throw std::runtime_error("Unsupported command");
        }
    } catch(const std::exception& e) {error=true;continuation=0;note(std::string("ERROR: ")+e.what());return {0xff};}
}
void Ce140f::tick(uint64_t now,bool x,bool busy,uint8_t data) {
    if(!mounted)return;
    bool rise=busy&&!oldBusy,fall=!busy&&oldBusy;
    if(x&&(data&4)&&(phase==Idle||phase==Receive)) {
        resetBus();phase=Device;ack=true;device=bits=0;lastActivity=now;note("Device select");
    }
    if(rise||fall)lastActivity=now;
    switch(phase) {
    case Idle:break;
    case Device:
        if(rise && bits<8){device|=((data>>2)&1)<<bits++;ack=false;}
        if(fall){if(bits==8){char s[32];snprintf(s,sizeof(s),"Device bits %02X",device);note(s);phase=Selected;ack=false;}else ack=true;}
        break;
    case Selected:
        if(!x&&!busy){if(device==0x41){ack=true;phase=SelectedAck;deadline=now+9216;note("Device 41 acknowledged");}else resetBus();}break;
    case SelectedAck:if(now>=deadline){ack=false;phase=Receive;rx.clear();nibble=0;lastByte=now;}break;
    case Receive:
        if(rise){ack=true;if(nibble==0){low=data;nibble=1;}else{rx.push_back(low|(data<<4));nibble=0;lastByte=now;}}
        if(fall)ack=false;
        if(!busy&&!nibble&&!rx.empty()&&now-lastByte>=51200){tx=command(rx);rx.clear();txNibble=0;ack=false;phase=TransmitGap;deadline=now+1024;}
        if(rx.size()>MaxFile){error=true;resetBus();}break;
    case TransmitGap:
        if(!busy&&now>=deadline){
            if(txNibble>=int(tx.size()*2)){ack=false;lines=0;phase=continuation?Receive:Idle;rx.clear();nibble=0;lastByte=now;}
            else {lines=(tx[txNibble/2]>>((txNibble&1)*4))&15;ack=true;phase=Transmit;}
        }break;
    case Transmit:if(rise){ack=false;phase=TransmitAck;}break;
    case TransmitAck:if(!busy){
        ++txNibble;
        if(txNibble>=int(tx.size()*2)){ack=false;lines=0;phase=continuation?Receive:Idle;rx.clear();nibble=0;lastByte=now;}
        else {phase=TransmitGap;deadline=now+512;}
    }break;
    }
    if(phase!=Idle && now-lastActivity>1024000*5){note("Bus timeout");resetBus();error=true;}
    oldBusy=busy;oldXout=x;
}
