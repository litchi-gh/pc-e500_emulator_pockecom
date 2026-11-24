// SPDX-License-Identifier: GPL-2.0-or-later
// PC-E500 シリーズ本体（CPU・メモリ・LCD・キーボード・11ピンの周辺機器）をまとめたクラス。
// デスクトップ側からは machine.cpp の e500_* 関数（DLL の公開 API）を通して使う。
#pragma once
#include "sc62015.h"
#include <array>
#include <algorithm>
#include <deque>
#include <vector>
#include "ce140f.h"
#include "ce126p.h"
#include "serial.h"
#include "cassette.h"

/// LCD コントローラ1個分（PC-E500 は2個で 240×32 ドットを表示する）。
struct Lcd {
    /// 表示 RAM（8ページ×64桁、1バイトが縦8ドット）。
    std::array<BYTE,512> ram{};
    /// page：今のページ。column：今の桁（読み書きで1つ進む）。start：表示を始める行。on：表示中か。
    BYTE page=0,column=0,start=0,on=0;
    /// コマンドかデータを1つ処理する。
    /// @param cmd 下位8ビットが値。0x100：データの書き込み、0x200：状態の読み出し、0x300：データの読み出し、
    ///            それ以外はコマンド（ページ・桁・開始行・表示の ON/OFF）
    /// @return 読み出した値（読み出し以外は 0）
    BYTE command(int cmd);
};

/// PC-E500 シリーズの本体。
///
/// 1MB のメモリ空間（ROM・内蔵 RAM・RAM カード・LCD）、SC62015 CPU、キーボードの行列、
/// 11ピンにつながる CE-140F と CE-126P、音声（XOUT から作る 48kHz の波形）を持つ。
class Machine {
public:
    /// メモリ空間（20ビット）。RAM と ROM の中身をそのまま持つ。
    std::array<BYTE,0x100000> mem;
    /// キーボードの行列（12行×8ビット）。押されているキーのビットが 1。
    std::array<BYTE,12> keys{};
    Lcd lcd[2]; // controller 1, controller 2
    /// CPU（SC62015）。
    Csc62015 cpu;
    /// 内蔵 RAM と RAM カードの容量（KB、カードなしは 0）。
    int internalKB=32,cardKB=0;
    /// ROM の容量（KB）。PC-E500 は 256、PC-E500S・PC-E650・PC-U6000 は 384。
    int romKB=256;
    /// 標準の内蔵 RAM（KB）。62256 が 1 個の PC-E500・E500S は 32、2 個の PC-E650・U6000 は 64。
    int standardKB=32;
    /// ROM の中身のチェックサム（FNV-1a）。状態ファイルが同じ ROM のものかを確かめる。
    uint32_t romSum=0;
    /// 11ピンにつながる周辺機器。
    Ce140f disk;
    Ce126p printer;
    SerialPort serial;
    CassetteSignals tape;
    /// 音声（48kHz・モノラル）。デスクトップ側が e500_audio で取り出す。
    std::deque<int16_t> audio;
    /// 音声のサンプルの位置と、音のモードで XOUT を切り替える位相。
    uint64_t samplePhase=0,tonePhase=0;
    /// 前回の音のモード（SCR のビット4〜6）。変わったら位相を戻す。
    int lastSoundMode=-1;
    /// 今の XOUT 信号。
    bool xout=false;
    /// デバッガのウォッチ：このアドレスを読んだ回数を数える。
    UINT32 watchAddr=0xffffffff;
    UINT32 watchHits=0;
    // The debugger API (e500_debug_*, used by the VS Code debug server) only:
    // the emulator's own run (e500_run) never looks at these, so it costs it nothing.
    /// デバッガのブレークポイント（1MB を1ビットずつ。設定したときだけ確保する）。
    std::vector<BYTE> breakBits;
    /// 呼び出しの記録1つ：呼んだ番地、飛び先、戻り先を積んだあとの S、割り込みか。
    struct CallFrame {UINT32 site,target,sp,interrupt;};
    /// デバッガが数える呼び出しの記録（CALL・CALLF・割り込みで積み、戻り先が取り出されたら外す）。
    std::vector<CallFrame> frames;
    /// 読み出しウォッチ（実際の格納先の番地で 1MB を1ビットずつ）と、最後に引っかかった番地（なければ 0xffffffff）。
    /// BASIC のデバッグで、インタプリタが行の最初のバイトを読んだところで止めるのに使う。
    std::vector<BYTE> readBits;
    UINT32 readHit=0xffffffff;
    /// 読み出しウォッチの絞り込み：読んだ命令のあとの P がこの番地のときだけ止まる（0xffffffff なら絞らない）。
    /// BASIC の行は、インタプリタが文を取り出す同じ1か所で読み始めるので、その場所だけに絞る。
    UINT32 readFilter=0xffffffff;
    /// a がブレークポイントか。
    bool isBreak(UINT32 a) const {a&=0xfffff;return !breakBits.empty() && (breakBits[a>>3]>>(a&7)&1);}
    /// デバッガの1命令（プリフィクスとその命令は1つ）。呼び出しの記録も更新する。budget は HALT 中に進めてよいサイクル数。
    void debugStep(uint64_t budget=3);
    Machine():cpu(this) {mem.fill(0xff);}
    /// アドレスが RAM（内蔵 RAM の領域、またはカードがあるならカードの領域）か。
    bool isRam(UINT32 a) const {
        return (a>=0x80000 && a<0xc0000) || (cardKB && a>=0x40000 && a<0x80000);
    }
    /// AMC AM5..0 describes CE0 capacity in2KB units; bit6 is reserved.
    UINT32 amcBase() const {return 0xc0000-((UINT32(cpu.imem[IMEM_AMC])&63)+1)*2048;}
    /// アドレスを実際の格納先に変換する。RAM の容量より広い領域は、容量ごとに同じ中身が繰り返し見える（ミラー）。
    UINT32 mirror(UINT32 a) {
        a&=0xfffff;
        if(a>=0x80000 && a<0xc0000) {
            // Sharp ESR-L Instruction Manual pp.8-9: AME links the end of
            // CE1 to the start of populated CE0. Decode the entire CE0 hole,
            // then apply the card's ordinary physical mirroring. The CPU
            // knows the CE0 size, not the inserted CE1 card's capacity.
            if((cpu.imem[IMEM_AMC]&0x80) && a<amcBase())
                a=0x40000+((a-amcBase())&0x3ffff);
            else return (a&(internalKB*1024-1))|(0xc0000-internalKB*1024);
        }
        if(cardKB && a>=0x40000 && a<0x80000)return (a&(cardKB*1024-1))|0x40000;
        return a;
    }
    /// 音のモード（SCR のビット4〜6）から、今の XOUT の値を決める。
    /// 1・5：常に High、2・3：2 kHz・4 kHz で切り替え、6・7：SSR のビット1（シリアル送信など）。
    bool toneLevel(int mode) const {
        switch(mode) {
        case 1:case 5:return true;
        case 2:return ((tonePhase/(CpuHz/4000))&1)==0;
        case 3:return ((tonePhase/(CpuHz/8000))&1)==0;
        case 6:case 7:return (cpu.imem[IMEM_SSR]&2)!=0;
        default:return false;
        }
    }
    /// 音のモード（SCR のビット4〜6）。
    int soundMode() const {return (cpu.imem[IMEM_SCR]>>4)&7;}
    /// 11ピンバスの XOUT と BUSY の値。
    struct BusLevels {bool xout,busy;};
    // The one place the lines are read from the ports: every step, and an older
    // snapshot's load, which has no levels of its own.
    /// 今の XOUT（音のモード mode の波形）と BUSY（EOL のビット6）。peripherals と状態の読み込みで共通。
    BusLevels busLevels(int mode) const {return {toneLevel(mode),(cpu.imem[IMEM_EOL]&0x40)!=0};}
    // The tone's phase is not in a snapshot: a mode-2 or mode-3 wave starts
    // over in the half that has XOUT where it was saved, so the devices, which
    // saved that level, see no edge that never happened.
    /// 状態を読み込んだあと、音の波形を始め直す。位相は保存しないので、モード2・3では
    /// XOUT が保存したときの値（level）から続くように、半周期ずらして始める。
    void restartTone(bool level) {
        int mode=soundMode();lastSoundMode=mode;tonePhase=0;
        // Half a period of the mode-2 (2 kHz) or mode-3 (4 kHz) wave, at the clock the core runs at.
        if(toneLevel(mode)!=level)tonePhase=mode==2?CpuHz/4000:mode==3?CpuHz/8000:0;
    }
    /// LCD の I/O（アドレスの下位4ビット）を2つのコントローラへ振り分ける。
    BYTE display(int cmd,BYTE d);
    /// メモリを1バイト読む（ミラーと LCD の I/O を含む）。Get_16・Get_24・Get_20 はリトルエンディアン。
    BYTE Get_8(UINT32 a);
    BYTE Get_PC(UINT32 a) {return Get_8(a);}
    WORD Get_16(UINT32 a) {return Get_8(a)|(Get_8(a+1)<<8);}
    UINT32 Get_24(UINT32 a) {return Get_16(a)|(Get_8(a+2)<<16);}
    UINT32 Get_20(UINT32 a) {return Get_24(a)&0xfffff;}
    /// メモリに1バイト書く（RAM 以外は無視、LCD の I/O は LCD へ）。
    void Set_8(UINT32 a,BYTE d);
    void Set_16(UINT32 a,WORD d) {Set_8(a,d);Set_8(a+1,d>>8);}
    void Set_24(UINT32 a,UINT32 d) {Set_16(a,d);Set_8(a+2,d>>16);}
    void Set_20(UINT32 a,UINT32 d) {Set_16(a,d);Set_8(a+2,(d>>16)&15);}
    /// CPU が選んでいるキーの行を読み、KI レジスタと割り込み要因を更新する。
    void scanKeys();
    /// 1命令を実行し、割り込みがあれば受け付ける。HALT・電源 OFF の間は、次のタイマーまでまとめて時間を進める（budget サイクルまで）。
    void step(uint64_t budget=3);
    /// LCD の 240×32 ドットを out に書く（1 なら点灯）。
    void pixels(BYTE *out);
    /// 命令を実行するたびに CPU から呼ばれる。音声のサンプルを作り、11ピンの周辺機器を動かす。
    /// @param cycles その命令にかかったサイクル数
    void peripherals(BYTE cycles);
};
