// SPDX-License-Identifier: GPL-2.0-or-later
// Native adaptation of the protocol documented by ffxx68/Sharp_ce140f_emul.
// CE-140F ドライブの実装（仕様は ce140f.h を参照）。
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
// 扱うファイルの上限（1MB）。
static constexpr size_t MaxFile=1024*1024;
// パスがシンボリックリンク・ジャンクションなど、別の場所を指すものか。
static bool reparse(const fs::path &p) {
    auto a=GetFileAttributesW(p.c_str());if(a==INVALID_FILE_ATTRIBUTES || !(a&FILE_ATTRIBUTE_REPARSE_POINT))return false;
    // OneDrive placeholders have reparse metadata but do not redirect the path.
    // Reject name-surrogate tags (symlinks/junctions), not ordinary cloud files.
    HANDLE h=CreateFileW(p.c_str(),0,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT|FILE_FLAG_BACKUP_SEMANTICS,nullptr);
    if(h==INVALID_HANDLE_VALUE)return true;
    FILE_ATTRIBUTE_TAG_INFO info{};bool ok=GetFileInformationByHandleEx(h,FileAttributeTagInfo,&info,sizeof(info));CloseHandle(h);
    return !ok || IsReparseTagNameSurrogate(info.ReparseTag);
}
// 数値を count バイトのリトルエンディアンにし、先頭に 0（成功）、末尾にチェックサムを付けた応答を作る。
static Bytes number(uint32_t n,int count) {Bytes b{0};uint8_t sum=0;while(count--){b.push_back(n&255);sum+=n&255;n>>=8;}b.push_back(sum);return b;}
// 末尾にチェックサム（全バイトの和の下位8ビット）を付ける。
static Bytes checked(Bytes b) {uint8_t sum=std::accumulate(b.begin(),b.end(),0);b.push_back(sum);return b;}
// The ROM sends a FILES pattern as the 8.3 fields padded with spaces - FILES
// "X:" is ????????.??? - so names are matched in that form, where a ? stands
// for a space as well, as on a DOS-style drive. A * fills the rest of its field
// with ?. Matched as written, ? needed a character and short names never showed.
// 8.3 の名前を、空白で埋めた「名前8文字.拡張子3文字」の形にする。* はその欄の残りを ? で埋める。
static std::string padded(const std::string& s) {
    auto dot=s.find('.');std::string name=s.substr(0,dot),ext=dot==std::string::npos?"":s.substr(dot+1);
    auto field=[](std::string f,size_t width){auto star=f.find('*');if(star!=std::string::npos)f=f.substr(0,star)+std::string(width>star?width-star:0,'?');f.resize(width,' ');return f;};
    return field(name,8)+"."+field(ext,3);
}
// FILES のパターン（* と ?）がファイル名に合うか。どちらも空白で埋めた 8.3 の形で比べ、? は空白にも合う。
static bool wildcard(const std::string& pattern,const std::string& name) {
    auto p=padded(pattern),s=padded(name);
    for(size_t i=0;i<p.size();i++)if(p[i]!='?' && p[i]!=s[i])return false;
    return true;
}
// Trimmed at a line's end, so the window never starts with half a line (or half a character).
// 古い分を捨てるときは行の切れ目で（ウィンドウの先頭が行や文字の途中にならないように）。
void Ce140f::note(const std::string& s) {
    log+=s+"\n";
    if(log.size()>16384) {auto cut=log.find('\n',log.size()-12000);log.erase(0,cut==std::string::npos?log.size()-12000:cut+1);}
}
int Ce140f::trace(char *out,int length) {if(!out||length<1)return 0;int n=std::min(length-1,int(log.size()));memcpy(out,log.data(),n);out[n]=0;return n;}
// The lines' last levels stay, as on the printer: they are the bus's.
void Ce140f::resetBus() {phase=code.resume=Idle;ack=resumeAck=false;lines=0;rx.clear();tx.clear();continuation=0;nibble=0;code.start();}
// The machine's RESET leaves no file number in BASIC: a file the drive kept
// open, or a transfer, could never be closed, and would hold off every snapshot.
// 本体の RESET で BASIC のファイル番号はなくなるので、開いたファイルと転送も閉じる
// （残すと閉じる手段がなく、状態保存がずっと断られる）。フォルダの接続はそのまま。
// What PRINT# wrote is kept in memory and written once (CLOSE, a reset, a new
// folder, the end): written on every line, a long file cost the square of its
// size in disk writes, seconds of a stalled machine in a synced folder.
// PRINT# の中身はメモリに持ち、CLOSE・リセット・フォルダの切り替え・終了のときに1回だけ書く。
void Ce140f::flush(File& f) {if(f.mode>=2 && f.dirty){write(f.name,f.bytes);f.dirty=false;}}
void Ce140f::flushAll() {
    for(auto &f:files)try {flush(f);} catch(const std::exception &e) {note(std::string("ERROR: ")+e.what());}
}
Ce140f::~Ce140f() {if(mounted)flushAll();}
void Ce140f::reset() {if(mounted)flushAll();resetBus();transfer={};files={};error=false;}
// Our own code, once accepted, starts a transfer that no snapshot can carry.
bool Ce140f::canSnapshot() const {
    bool bus=phase==Idle || (code.resume==Idle && (phase==Device || (phase==Selected && code.value!=0x41)));
    return bus && continuation==0 && transfer.mode==0 && std::all_of(files.begin(),files.end(),[](const File &f){return f.mode==0;});
}
Ce140f::CodeState Ce140f::saveCode() const {
    CodeState s{};
    // Idle keeps the last code's bits and device about; they mean nothing there.
    // The lines' levels go in all the same, so that XOUT high when it was taken
    // is not seen to rise on loading.
    // 待機中は、前回のコードのビットに意味がないので線の状態だけを入れる（読み込んだとき、
    // High だった XOUT を立ち上がりとみなさないため）。
    s.oldBusy=oldBusy;s.oldXout=oldXout;
    if(!canSnapshot() || phase==Idle)return s;
    s.phase=uint8_t(phase);s.bits=code.bits;s.device=code.value;s.ack=ack;s.lastActivity=lastActivity;
    return s;
}
bool Ce140f::validCode(const CodeState& s,uint64_t now) {
    // Reserved bytes are zero, so that a later format can give them a meaning.
    // 予約のバイトは 0（将来の形式で意味を持たせられるように）。
    bool fits=s.ack<2 && s.oldBusy<2 && s.oldXout<2 && s.lastActivity<=now && DeviceCode::valid(s.bits,s.device) &&
              !s.reserved[0] && !s.reserved[1];
    if(s.phase==Idle)return fits && s.bits==0 && s.device==0 && !s.ack;
    // A code runs with XOUT high; only its eighth bit can see XOUT fall
    // before BUSY does, and wait in Device for that.
    if(s.phase==Device)return fits && (s.oldXout || s.bits==8);
    if(s.phase==Selected)return fits && s.bits==8 && s.device!=0x41;
    return false;
}
bool Ce140f::restoreCode(const CodeState& s,uint64_t now) {
    if(!validCode(s,now))return false;
    resetBus();oldBusy=s.oldBusy;oldXout=s.oldXout;
    if(!mounted)return true;
    phase=Phase(s.phase);code.bits=s.bits;code.value=s.device;ack=s.ack;lastActivity=s.lastActivity;
    return true;
}
// フォルダを検査して（リンクは拒否）ドライブとして接続する。
bool Ce140f::mount(const wchar_t *path,bool protect) {
    // Mounted again (another folder, or the protection changed), the drive keeps
    // its place in a device code going by: the folder has nothing to do with the
    // bus, and losing the bits taken would fail the ROM's selection of 41h.
    // Unmounted before, it is idle, and saveCode gives only the lines' levels.
    // 以前も接続していればコードの受け取りの状態を、そうでなければ（待機なので）線の状態だけを持ち越す。
    // A folder that cannot be opened changes nothing: the drive stays on the
    // folder it had (or unmounted), its files and transfer as they were, so a
    // rejected setting does not cost the drive in use.
    // 開けないフォルダなら何も変えない（それまでのフォルダのまま、ファイルと転送もそのまま）。
    CodeState saved=saveCode();
    fs::path next;
    if(path&&*path) {
        try {
            fs::path p=fs::absolute(path);
            fs::create_directories(p);
            if(!fs::is_directory(p)||reparse(p))throw std::runtime_error("Invalid disk directory");
            next=fs::canonical(p);
        }catch(const std::exception &e){note(e.what());return false;}
    }
    reset();mounted=false;
    if(next.empty())return true;
    root=next;readOnly=protect;mounted=true;note("Mounted CE-140F drive X:");
    restoreCode(saved,saved.lastActivity);return true;
}
// 8.3 形式・使える文字・予約名・リンクを検査し、フォルダの外を指せないようにする。
fs::path Ce140f::safePath(const std::string& n) const {
    // A name with no extension (SAVE "X:PROG") is a name like any other: refused,
    // it was made all the same with its dot dropped by Windows, and then neither
    // FILES nor INIT could see it.
    // 拡張子のない名前も受け付ける（以前は Windows が点を落として作り、FILES にも INIT にも出なかった）。
    auto dot=n.find('.');
    if(n.empty()||dot==0||(dot==std::string::npos?n.size()>8:dot>8||n.size()-dot-1>3||n.size()-dot-1==0||n.find('.',dot+1)!=std::string::npos))
        throw std::runtime_error("Invalid 8.3 filename");
    for(unsigned char c:n)if(!((c>='A'&&c<='Z')||(c>='0'&&c<='9')||c=='_'||c=='-'||c=='.'))throw std::runtime_error("Unsafe filename");
    std::string stem=n.substr(0,dot);
    // Only the device names: COMP or LPTR is an ordinary name.
    if(stem=="CON"||stem=="PRN"||stem=="AUX"||stem=="NUL"||(stem.size()==4&&(stem.substr(0,3)=="COM"||stem.substr(0,3)=="LPT")&&stem[3]>='1'&&stem[3]<='9'))throw std::runtime_error("Reserved filename");
    auto p=root/fs::path(n);
    if(reparse(p)||reparse(root)||fs::canonical(root)!=root)throw std::runtime_error("Reparse points are not disk files");
    return p;
}
std::string Ce140f::fileName(const Bytes& b,size_t at) const {
    if(at+12>b.size())throw std::runtime_error("Truncated filename");
    std::string s;for(size_t i=at;i<at+12;i++)if(b[i] && b[i]!=' ')s+=char(toupper(b[i]));
    // "PROG    .   " is PROG, with no extension.
    if(!s.empty() && s.back()=='.')s.pop_back();
    return s;
}
Bytes Ce140f::read(const std::string& name) const {
    auto p=safePath(name);if(!fs::is_regular_file(p)||fs::file_size(p)>MaxFile)throw std::runtime_error("File missing or too large");
    std::ifstream f(p,std::ios::binary);Bytes b((std::istreambuf_iterator<char>(f)),{});if(!f.eof() && f.fail())throw std::runtime_error("Disk read failed");return b;
}
bool Ce140f::protectedFile(const std::string& name) const {auto a=GetFileAttributesW(safePath(name).c_str());return a!=INVALID_FILE_ATTRIBUTES && (a&FILE_ATTRIBUTE_READONLY);}
void Ce140f::requireWrite(const std::string& name) const {if(readOnly||protectedFile(name))throw std::runtime_error("Write protected");}
// 一時ファイルに書いてから置き換えるので、途中で失敗しても元のファイルは壊れない。
void Ce140f::write(const std::string& name,const Bytes& b) {
    requireWrite(name);if(b.size()>MaxFile)throw std::runtime_error("File too large");
    auto p=safePath(name),tmp=p;tmp+=L".ce140f-tmp";
    if(reparse(tmp))throw std::runtime_error("Unsafe temporary file");
    {std::ofstream f(tmp,std::ios::binary|std::ios::trunc);f.write(reinterpret_cast<const char*>(b.data()),b.size());f.flush();if(!f)throw std::runtime_error("Disk write failed");}
    if(!MoveFileExW(tmp.c_str(),p.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH))throw std::runtime_error("Disk commit failed");
}
Bytes Ce140f::command(const Bytes& packet) {
    if(!mounted)return {0xff};
    // チェックサムを確かめ、コマンド番号を決める（続きのパケットなら前のコマンドの続き）。
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
        case 0x03: {  // OPEN：ファイルを開く（1 読み出し、2 書き込み、3 追記）
            need(17);int n=int(b[16])-2,mode=b[15];if(n< -1||n>=6||mode<1||mode>3)throw std::runtime_error("Invalid OPEN");
            auto name=fileName(b,3);safePath(name);File f;f.name=name;f.mode=mode;
            for(auto &other:files)if(other.mode && other.name==name)flush(other);
            // APPEND to a file not there yet starts it, as on a disk.
            if(mode==1 || (mode==3 && fs::exists(safePath(name))))f.bytes=read(name);
            if(mode!=1){requireWrite(name);if(mode==2 || !fs::exists(safePath(name)))write(name,{});f.position=f.bytes.size();}
            if(n==-1)transfer=std::move(f);else files[n]=std::move(f);return {0};
        }
        case 0x04: need(2);  // CLOSE：ファイルを閉じる（FFh ならすべて）
            if(b[1]==255){for(auto &f:files)flush(f);files={};transfer={};}else if(b[1]==1)transfer={};else {int n=b[1]-2;if(n<0||n>=6)throw std::runtime_error("Invalid CLOSE");flush(files[n]);files[n]={};}return {0};
        case 0x05: {  // FILES：パターンに合うファイルの一覧を作り、件数を返す
            listing.clear();listIndex=-1;std::string pattern=b.size()>=15?fileName(b,3):"*.*";if(pattern.empty()||pattern==".")pattern="*.*";
            flushAll();
            // An entry that cannot be read or named (a name outside the code page)
            // is left out, not the whole listing.
            for(auto &entry:fs::directory_iterator(root)) {
                try {
                    if(!entry.is_regular_file()||reparse(entry.path()))continue;
                    auto name=entry.path().filename().string();std::transform(name.begin(),name.end(),name.begin(),[](unsigned char c){return char(toupper(c));});
                    safePath(name);if(wildcard(pattern,name) && listing.size()<255)listing.push_back(name);
                } catch(...) {}
            }
            std::sort(listing.begin(),listing.end());return checked({0,uint8_t(listing.size())});
        }
        case 0x06:case 0x07: {  // FILES の次・前の1件を返す
            listIndex+=cmd==6?1:-1;if(listIndex<0||listIndex>=int(listing.size()))return {0xff};
            auto name=listing[listIndex];auto dot=name.find('.');std::string s="X:"+name.substr(0,dot);s.resize(10,' ');s+="."+(dot==std::string::npos?"":name.substr(dot+1));s.resize(15,' ');
            Bytes out{0};out.insert(out.end(),s.begin(),s.end());return checked(out);
        }
        // 書き込みできるかの確認
        case 0x08: if(readOnly)throw std::runtime_error("Write protected");return {0};
        case 0x09: {  // INIT（フォーマット）：フォルダ内の 8.3 形式のファイルを消す
            if(readOnly)throw std::runtime_error("Write protected");
            // Folder-backed virtual disk: format only disk-visible 8.3 files, never recurse.
            // Names compared as FILES shows them, in capitals: prog.bas copied from a PC is PROG.BAS on the disk.
            std::vector<std::string> names;for(auto &e:fs::directory_iterator(root)) {
                try {
                    if(!e.is_regular_file()||reparse(e.path()))continue;
                    auto n=e.path().filename().string();std::transform(n.begin(),n.end(),n.begin(),[](unsigned char c){return char(toupper(c));});
                    safePath(n);names.push_back(n);
                } catch(...) {}
            }
            for(auto &n:names)requireWrite(n);
            for(auto &n:names)fs::remove(safePath(n));for(auto &f:files)f.dirty=false;files={};transfer={};return {0};
        }
        // KILL：ファイルを消す
        case 0x0a: {flushAll();auto n=fileName(b,3);requireWrite(n);if(!fs::remove(safePath(n)))throw std::runtime_error("File not found");return {0};}
        case 0x0b:case 0x0d: {  // 0Bh NAME（名前の変更）、0Dh COPY
            flushAll();auto src=fileName(b,3);auto dest=fileName(b,17);requireWrite(dest);
            if(cmd==0x0b){requireWrite(src);if(fs::exists(safePath(dest)))throw std::runtime_error("Destination exists");fs::rename(safePath(src),safePath(dest));}
            else write(dest,read(src));return {0};
        }
        case 0x0c: {  // SET：読み取り専用の属性を付け外しする
            need(17);if(readOnly)throw std::runtime_error("Write protected");auto p=safePath(fileName(b,3));auto a=GetFileAttributesW(p.c_str());if(a==INVALID_FILE_ATTRIBUTES)throw std::runtime_error("File not found");
            if(!SetFileAttributesW(p.c_str(),(b[16]&1)?a|FILE_ATTRIBUTE_READONLY:a&~FILE_ATTRIBUTE_READONLY))throw std::runtime_error("Attribute write failed");return {0};
        }
        // 0Eh LOAD の開始（サイズを返す）、17h 1バイトずつ、0Fh 残りをまとめて（256バイトごとにチェックサム）
        case 0x0e: {flushAll();transfer={};transfer.name=fileName(b,3);transfer.bytes=read(transfer.name);transfer.mode=1;auto out=number(uint32_t(transfer.bytes.size()),3);out.insert(out.begin()+1,0x20);out.back()+=0x20;return out;}
        case 0x17: {if(transfer.position>=transfer.bytes.size())return {0xff};return checked({0,transfer.bytes[transfer.position++]});}
        case 0x0f: {
            Bytes out{0};uint8_t sum=0;size_t count=0;
            while(transfer.position<transfer.bytes.size()) {auto v=transfer.bytes[transfer.position++];out.push_back(v);sum+=v;if(++count%256==0){out.push_back(sum);sum=0;}}
            // A body that ends on a whole block has had its checksum already; sent
            // again, the ROM took it for the status and left a byte unread.
            // ちょうど 256 バイトの倍数で終わるときは、チェックサムはもう送ってある（重ねると ROM が1バイト読み残す）。
            if(count==0 || count%256!=0)out.push_back(sum);
            out.push_back(0);transfer.mode=0;return out;
        }
        // A text file may end with a 1Ah (EOF), put there by a PC or another
        // drive; the file ends there, and whatever follows is not read. Sent as a
        // character as well as the closing 1Ah, it made the ROM report Disk full.
        // A last line with no CR (a file from a PC often has none) gets one:
        // the ROM drops a line that runs into the closing 1Ah.
        case 0x12: {  // テキストファイルの LOAD：1行（CR まで）ずつ返す。ファイルの中の 1Ah で終わり、最後に 1Ah。CR のない最後の行には CR を補う
            Bytes out{0};bool ended=false;
            while(transfer.position<transfer.bytes.size()) {
                auto v=transfer.bytes[transfer.position++];
                if(v==0x1a){transfer.position=transfer.bytes.size();break;}
                if(v==10)continue;out.push_back(v);if(v==13){ended=true;break;}
            }
            if(!ended && out.size()>1){out.push_back(13);ended=true;}
            if(!ended){out.push_back(0x1a);transfer.mode=0;}out=checked(out);out.push_back(0);return out;
        }
        // 10h SAVE の開始、11h サイズの通知（続くパケットがデータ）、16h テキストの SAVE の開始
        case 0x10: {transfer={};transfer.name=fileName(b,3);safePath(transfer.name);requireWrite(transfer.name);transfer.mode=2;return {0};}
        case 0x11: {need(5);expectedSize=b[2]|(b[3]<<8)|(b[4]<<16);if(expectedSize<1||expectedSize>MaxFile)throw std::runtime_error("Invalid SAVE size");transfer.bytes.clear();continuation=0xff;return {0};}
        case 0x16: if(transfer.name.empty())throw std::runtime_error("ASCII file not open");continuation=0xfe;return {0};
        case 0xff: {  // SAVE のデータの続き。予定のサイズに達したら書き込む
            transfer.bytes.insert(transfer.bytes.end(),b.begin(),b.end());
            if(transfer.bytes.size()>size_t(expectedSize))throw std::runtime_error("SAVE overflow");
            if(transfer.bytes.size()==size_t(expectedSize)){write(transfer.name,transfer.bytes);note("Saved "+transfer.name);transfer.mode=0;}
            else continuation=0xff;return {0};
        }
        // The 1Ah that ends the text is not written: a hardware CE-140F emulator
        // (ffxx68's) leaves it out too, and when its LOAD meets one in a file it
        // sends the end twice and the ROM fails, as this one did before.
        case 0xfe: {  // テキストの SAVE のデータの続き。1Ah で終わり（1Ah はファイルに書かない。ハードの CE-140F エミュレータと同じ）
            if(b[0]==0x1a){write(transfer.name,transfer.bytes);note("Saved ASCII "+transfer.name);transfer.mode=0;}
            // Written once, at the end, not again with every line.
            else {transfer.bytes.insert(transfer.bytes.end(),b.begin(),b.end());if(transfer.bytes.size()>MaxFile)throw std::runtime_error("File too large");}
            return {0};
        }
        // PRINT#：続くパケットを開いたファイルへ書く
        case 0x15: {auto &f=openFile();if(f.mode==1)throw std::runtime_error("File is input only");printFile=b[1]-2;continuation=0xfd;return {0};}
        case 0xfd: {
            auto &f=files[printFile];if(f.mode<2)throw std::runtime_error("File not writable");
            if(!b.empty()&&b.back()==0)b.pop_back();f.bytes.insert(f.bytes.end(),b.begin(),b.end());
            if(f.bytes.size()>MaxFile)throw std::runtime_error("File too large");
            f.dirty=true;f.position=f.bytes.size();return {0};
        }
        case 0x13:case 0x14:case 0x20: {  // INPUT#・LINE INPUT#（1行）、20h は残りすべて
            auto &f=openFile();if(f.mode!=1)throw std::runtime_error("File not readable");Bytes out{0};
            // A line ends at LF, or at a CR with no LF after it (a file with CR line ends).
            while(f.position<f.bytes.size()){
                auto v=f.bytes[f.position++];out.push_back(v);
                if(cmd!=0x20 && (v==10 || (v==13 && (f.position>=f.bytes.size() || f.bytes[f.position]!=10))))break;
            }
            out.push_back(0);out=checked(out);out.push_back(0);return out;
        }
        // 1Ah EOF、1Bh LOF（ファイルの大きさ）、1Ch LOC（位置）、1Dh DSKF（空き容量）
        case 0x1a: {auto &f=openFile();return {uint8_t(f.position>=f.bytes.size())};}
        case 0x1b: {auto &f=openFile();return number(uint32_t(f.bytes.size()),3);}
        case 0x1c: {auto &f=openFile();return number(uint32_t((f.position>>8)+1),2);}
        case 0x1d: return number(uint32_t(std::min(uintmax_t(0xffffff),fs::space(root).available)),3);
        default:throw std::runtime_error("Unsupported command");
        }
    } catch(const std::exception& e) {error=true;continuation=0;note(std::string("ERROR: ")+e.what());return {0xff};}
}
void Ce140f::tick(uint64_t now,bool x,bool busy,uint8_t data) {
    if(!mounted){oldBusy=busy;oldXout=x;return;}
    bool rise=busy&&!oldBusy,fall=!busy&&oldBusy;
    // Every device code starts on the rise of XOUT, whoever it is for: the
    // CE-126P's codes and BEEP tones come through here too, in whatever phase a
    // transfer is. Nothing is reset until the code turns out to be 41h, so they
    // leave a transfer in progress as it was. The ROM sends nibbles with XOUT
    // low, so every strobe while it is high is a bit of the code.
    // The drive speaks up only for a code whose first bit is 1, as 41h's is: it
    // raises ACK when DOUT shows a 1 with XOUT high, as ffxx68's emulator of the
    // real drive does (and this one did up to 0.0.3), and then answers all eight
    // bits, since once a code has begun the ROM waits long for every ACK. A code
    // that starts with 0 - the printer's 20h - it lets go by in silence, so with
    // no printer the ROM finds no device and LPRINT fails at once; answered,
    // the ROM went on to wait out the data phase (3 s for LPRINT, 9 s at PF1).
    // XOUT の立ち上がりで、どの段階からでもデバイスコードの受け取りへ移る（戻り先を resume に残す）。
    // ACK を返すのは、1ビット目が 1 のコード（41h と同じ）だけ。XOUT が High で DOUT が 1 に
    // なったら ACK を上げ、そのコードは8ビットすべてに応答する（ROM はコードを始めると ACK を
    // 長く待つので、途中でやめると止まったようになる）。1ビット目が 0 のコード（プリンタの 20h）
    // には応答しない。こうしないと、プリンタがないとき ROM はデータの段階まで進んで長く待つ。
    if(x&&!oldXout&&phase!=Device&&phase!=Selected) {resumeAck=ack;code.begin(phase,Device);ack=false;lastActivity=now;}
    if(rise||fall)lastActivity=now;
    switch(phase) {
    case Idle:break;
    case Device:
        // デバイスコード：BUSY の立ち上がりで1ビット取り、立ち下がりで次を待つ。8ビットで判定する。
        // XOUT falling before the eighth bit was a tone, not a code; a transfer in
        // progress goes on, its ACK back where its own handshake has it. Receiving,
        // that is up while the ROM holds BUSY for a nibble and down once it has
        // dropped it; sending, it is where the tone found it.
        // Back to receiving, the packet's 50 ms gap counts from the tone's end:
        // the ROM sent nothing under it, and the half packet is not complete.
        // 受信へ戻るときは、パケットの切れ目（50ms）を音の終わりから数え直す（音の間は何も来ていないため）。
        if(code.tone(phase,x)){ack=phase==Receive?busy:resumeAck;if(phase==Receive)lastByte=now;break;}
        if(rise){code.rise((data>>2)&1);ack=false;}
        else if(code.waiting())ack=(data&4)!=0;
        if(fall){
            // 1ビット目が 1 のコードだけ、次のビットのために ACK を上げる。
            if(!code.complete())ack=(code.value&1)!=0;
            else if(code.value==0x41){resetBus();code.bits=8;code.value=0x41;phase=Selected;note("Device select 41");}
            else {phase=Selected;ack=false;}
        }
        break;
    case Selected:
        // XOUT と BUSY が下がったら：41h なら受け付けを知らせ、ほかの機器なら転送を打ち切るか待機へ。
        if(!x&&!busy) {
            if(code.value==0x41){ack=true;phase=SelectedAck;deadline=now+9216;note("Device 41 acknowledged");}
            // The ROM has turned to another device mid-transfer (after a BREAK,
            // say): the transfer is over, or the other device's data strobes
            // would be taken for our nibbles or answer handshakes. So too a SAVE
            // or LOAD between its packets: the ROM never puts another device's
            // code inside one, and left open it would hold off every snapshot.
            // Files OPENed from BASIC stay open: a program may print what it reads.
            // ほかの機器のコードが来たら、自分の転送は終わり。パケットの途中でも、SAVE/LOAD の
            // パケットの間でも打ち切る（残ると状態保存がずっと断られる）。OPEN したファイルは残す。
            else if(code.resume!=Idle || transfer.mode!=0 || continuation){char s[48];snprintf(s,sizeof(s),"Transfer dropped for device %02X",code.value);note(s);resetBus();transfer={};}
            else {phase=Idle;ack=false;}
        }
        break;
    // 受け付けの ACK を一定時間出したあと、パケットの受信へ。
    case SelectedAck:if(now>=deadline){ack=false;phase=Receive;rx.clear();nibble=0;lastByte=now;}break;
    case Receive:
        // ニブル受信：BUSY の立ち上がりで ACK を上げて4ビット取り、2つで1バイト。
        // 51200 サイクル（約 67 ms）途切れたらパケットの終わりとして処理し、応答の送信へ。
        if(rise){ack=true;if(nibble==0){low=data;nibble=1;}else{rx.push_back(low|(data<<4));nibble=0;lastByte=now;}}
        if(fall)ack=false;
        if(!busy&&!nibble&&!rx.empty()&&now-lastByte>=51200){tx=command(rx);rx.clear();txNibble=0;ack=false;phase=TransmitGap;deadline=now+1024;}
        if(rx.size()>MaxFile){error=true;resetBus();}break;
    // 応答の送信：間を置いてからデータ線に4ビットを出して ACK を上げ（Transmit）、
    // ROM の BUSY の立ち上がりで ACK を下げ（TransmitAck）、BUSY が下がったら次のニブルへ。
    // すべて返したら、続きのパケットを待つコマンドなら受信へ、そうでなければ待機へ戻る。
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
    // 5秒バスが動かなければタイムアウトとしてエラーにする。ただし、待機から始まって
    // 最初のビットを待つコードは、XOUT が High に保たれているだけなので、黙って待機に戻る。
    // XOUT held high from idle with no bit is no stalled transfer: it is let go
    // with no error (see BusCode::unanswered).
    if(phase!=Idle && now-lastActivity>CpuHz*5) {
        if(phase==Device && code.unanswered()){phase=Idle;ack=false;}
        // The transfer goes too: left behind, it held off every snapshot until something else ended it.
        else {note("Bus timeout");resetBus();transfer={};error=true;}
    }
    oldBusy=busy;oldXout=x;
}
