// SPDX-License-Identifier: GPL-2.0-or-later
// The device code as every device on the 11-pin bus takes it, shared by the
// CE-126P and the CE-140F so that the two cannot drift apart.
//
// 11ピンバスのデバイスコードの受け取り（CE-126P と CE-140F で共通）。
// ROM は XOUT を High にしてから、1ビットごとに BUSY を上げ下げし、DOUT に LSB から
// 8ビットのコードを出す。応答する機器は ACK を High にしてビットを待ち、BUSY の立ち上がりで
// 1ビット取って ACK を下げ、立ち下がりで次のビットのために ACK を上げる。
// 8ビットそろう前に XOUT が下がったら、コードではなく音（BEEP など）だったとみなす。
#pragma once
#include <cstdint>

/// デバイスコード1つ分の受け取りの状態。どの段階に移るか・ACK をどうするかは各機器が決める。
struct DeviceCode {
    /// 受け取ったビット数（0〜8）と、組み立て中のコード（LSB から）。
    uint8_t bits=0,value=0;
    /// XOUT の立ち上がりで、新しいコードの受け取りを始める。
    void start() {bits=0;value=0;}
    /// BUSY の立ち上がり：1ビット取る（8ビットそろっていれば無視）。
    /// @param bit DOUT の値
    void rise(bool bit) {if(bits<8)value|=uint8_t(bit)<<bits++;}
    /// 8ビットそろったか。
    bool complete() const {return bits==8;}
    /// まだ1ビットも来ていないか（XOUT が High のまま待っている）。
    bool waiting() const {return bits==0;}
    /// 保存した状態としてありうるか（受け取ったビットより上の桁が立っていない）。
    static bool valid(uint8_t bits,uint8_t value) {return bits<=8 && (bits==8 || (value>>bits)==0);}
};

// Going into a code and back out of it, the same on both devices: a code
// starts on the rise of XOUT from whatever the device was doing, and a tone -
// XOUT down before the eighth bit - goes back to that.
/// デバイスコードの段階への出入り（2つの機器で共通）。
/// Phase は機器の段階の型で、Phase{}（最初の値）が待機であること。
template<class Phase> struct BusCode : DeviceCode {
    /// Device に移る前の段階（音のあと戻る先）。
    Phase resume{};
    /// XOUT の立ち上がり：今の段階を resume に残し、device に移ってコードの受け取りを始める。
    /// @param phase  機器の今の段階（device に書き換える）
    /// @param device 機器のデバイスコードの段階
    void begin(Phase& phase,Phase device) {resume=phase;phase=device;start();}
    /// 8ビットそろう前に XOUT が下がった（音）なら、phase を resume に戻して true を返す。
    /// @param phase 機器の今の段階
    /// @param xout  XOUT 信号
    bool tone(Phase& phase,bool xout) {if(xout || complete())return false;phase=resume;return true;}
    // XOUT held high from idle with no bit (a tone mode, a program setting the
    // line) is no code: the ROM sends its first bit right after the ACK it
    // waits a few polls for. Past the device's timeout it is let go quietly,
    // with no error and no ACK held up on the wired-OR line.
    /// 待機から始まったコードが、まだ1ビットも来ていないか。この状態のまま機器の
    /// タイムアウトを過ぎたら、エラーにせず待機に戻る（ACK も下げる）。
    bool unanswered() const {return waiting() && resume==Phase{};}
};
