// SPDX-License-Identifier: GPL-2.0-or-later
// CE-126P thermal printer on the 11-pin bus, as the PC-E500 ROM drives it.
// Protocol: https://github.com/litchi-gh/pc-e500_tools/blob/main/Analysis/CE-126P_PRINTER.md
//
// CE-126P（11ピン接続のサーマルプリンタ）のエミュレーション。
// ROM のプリンタドライバが 11ピンバスに出す信号（XOUT・BUSY・DOUT）を CPU の
// 命令ごとに受け取り、ACK を返しながらデバイスコードと印字データを解読する。
// 印字された行は内部の紙バッファにためておき、デスクトップ側が read で取り出す。
#pragma once
#include <cstdint>
#include <deque>
#include <string>
#include <vector>
#include "devicecode.h"

/// CE-126P プリンタ本体。
///
/// バス上の状態は3つの段階で進む。
/// - Idle：何も受け取っていない。
/// - Device：XOUT が High の間、デバイスコードを1ビットずつ受け取る（LSB から）。
///   0Fh（有無の確認）か 20h（行が続く）なら選択されて Data へ、それ以外なら Idle へ戻る。
/// - Data：XOUT が Low の間、印字データを1バイトずつ受け取る。0Dh で行を紙に送り、
///   0Eh・0Fh で選択を終える。
/// 行の途中で XOUT が立ち上がると Device に移り、8ビットそろう前に下がれば（音）行の受信に戻る。
class Ce126p {
public:
    /// 1行の桁数（CE-126P は24桁）。
    static constexpr int Columns=24;
    /// 接続されているか。false の間は ACK を返さず、ROM は Printer error になる。
    bool connected=true;
    /// プリンタが出している ACK 信号（11ピンの ACK は各機器のワイヤード OR）。
    bool ack=false;
    /// CPU の命令ごとに呼ばれ、バスの信号の変化を処理する。
    /// @param now  CPU のサイクル数（タイムアウトの判定に使う）
    /// @param xout XOUT 信号（High でデバイスコード、Low でデータ）
    /// @param busy BUSY 信号（ROM が1ビットごとに上げ下げするストローブ）
    /// @param dout DOUT 信号（送られてくる1ビットの値）
    void tick(uint64_t now,bool xout,bool busy,bool dout);
    /// バス上の状態を待機（Idle）に戻す。送りかけの行も捨てる。紙バッファと、線の前回の状態は残す。
    void resetBus();
    // Printed lines in the PC-E500 character set, each ending in '\n'.
    /// 印字済みの行を PC-E500 の文字コードで取り出し、取り出した分を紙バッファから消す。
    /// @param out    取り出し先
    /// @param length out の大きさ（バイト）
    /// @return 取り出したバイト数（各行は '\n' で終わる）
    int read(uint8_t *out,int length);
    // Bit 0: connected. Bit 1: busy on the bus - taking the bits of a code (any
    // device's) or a line's bytes; not while XOUT waits for a first bit.
    /// 状態のビット。bit 0：接続中。bit 1：デバイスコードのビットか行のバイトを受け取り中。
    int status() const {return connected?(1|(phase==Data || (phase==Device && !code.waiting())?2:0)):0;}
    // Where the printer is on the bus, carried in a snapshot so that one taken
    // mid-code or mid-line resumes the ROM's driver against a printer that
    // remembers where it was.
    /// スナップショット（状態保存）に入れるバス上の状態。固定長のバイナリとしてそのまま保存する。
    /// 行の途中で保存しても、読み込めば続きを印字できる。
    struct State {uint8_t phase,resume,codeBits,codeValue,bits,value,ack,oldBusy,oldXout,length,reserved[6];uint8_t line[Columns];uint64_t lastActivity;};
    /// 今のバス上の状態を State にして返す。待機中なら、意味のない項目（時刻など）は 0 にする。
    State save() const;
    // Whether the printer could be in this state by time now.
    /// State がプリンタのありうる状態か（各項目の範囲と、項目どうしの整合）を確かめる。
    /// @param s   確かめる状態
    /// @param now 読み込み先のサイクル数（待機中でなければ、lastActivity がこれより後なら不正）
    static bool valid(const State& s,uint64_t now);
    // False, changing nothing, for a state valid refuses.
    /// State を復元する。valid が断る状態なら何も変えずに false を返す。
    /// 接続されていなければ、復元したうえで待機に戻す。
    bool restore(const State& s,uint64_t now);
    // The line the ROM sent, decoded back from the printer's codes.
    /// ROM がプリンタ用に変換して送った文字コードを、PC-E500 の文字コードに戻す。
    static uint8_t toE500(uint8_t code);
private:
    /// バス上の段階（クラスの説明を参照）。
    enum Phase {Idle,Device,Data} phase=Idle;
    // The lines as last seen, kept up while unplugged and across resetBus:
    // they are the bus's, and a device plugged in under XOUT already high must
    // not take that for the start of a code.
    /// 前回の tick での BUSY と XOUT（立ち上がり・立ち下がりの検出用）。線の状態なので、
    /// 未接続の間も追い、resetBus でも残す（XOUT が High のまま接続しても、コードの始まりとみなさない）。
    bool oldBusy=false,oldXout=false;
    /// 最後にバスが動いたサイクル数（1秒動かなければ選択を終える）。
    uint64_t lastActivity=0;
    /// 受け取り中のデバイスコード。code.resume は、Device に移る前の段階（音のあと戻る先、Idle か Data）。
    BusCode<Phase> code;
    /// 受け取り中の印字データのビット数（0〜8）と、組み立て中のバイト。
    int bits=0;
    uint8_t value=0;
    /// 受け取り中の行（0Dh で紙へ送る）。
    std::vector<uint8_t> line;
    /// 印字済みでまだ read されていない紙（最大 64KB、あふれたら古い行から捨てる）。
    std::deque<uint8_t> paper;
    /// 受け取った1バイトの印字データを処理する（文字・0Dh・0Eh・0Fh）。
    void byte(uint8_t b);
};
