// SPDX-License-Identifier: GPL-2.0-or-later
// Protocol reference: ../../../../Sharp_ce140f_emul/{main.cpp,commands.cpp,protocol.md}
//
// CE-140F（ポケットディスクドライブ、X: ドライブ）のエミュレーション。
// ディスクの代わりに PC のフォルダを使い、ROM がパケットで送るコマンド
// （SAVE・LOAD・FILES・OPEN など）をフォルダ上のファイル操作に置き換える。
#pragma once
#include <cstdint>
#include <filesystem>
#include <vector>
#include <array>
#include <string>
#include "devicecode.h"

/// CE-140F ディスクドライブ本体。
///
/// 11ピンバス上のやり取りは次の段階で進む。
/// - Device：XOUT が High の間、デバイスコードを1ビットずつ受け取る。ACK を返すのは
///   1ビット目が 1 のコード（41h と同じ）だけで、そのコードには8ビットすべてに応答する。
///   41h なら自分が選ばれる。
/// - Selected → SelectedAck：41h を受け付けたことを ACK で知らせる。
/// - Receive：XOUT が Low の間、ROM からパケットを4ビット（ニブル）ずつ受け取る。
///   しばらく途切れたらパケットの終わりとみなし、command で処理する。
/// - TransmitGap → Transmit → TransmitAck：応答を4ビットずつ ROM に返す。
/// 転送の途中で XOUT が立ち上がると、いったん Device に移る。8ビットそろう前に下がれば
/// 音（BEEP など）とみなして転送に戻り、ほかの機器のコードなら転送を打ち切る。
class Ce140f {
public:
    /// ドライブが出している ACK 信号（ワイヤード OR）。
    bool ack=false;
    /// 応答を返すときに4本のデータ線へ出す値（EIH の上位4ビットになる）。
    uint8_t lines=0;
    /// フォルダをドライブとして接続する。
    /// 接続し直し（フォルダや書き込み禁止の変更）の場合は、通り過ぎているデバイスコードの
    /// 受信状態を保つ。開いていたファイルと転送は閉じる。
    /// 開けないフォルダなら何も変えない（それまでのフォルダ・ファイル・転送のまま）。
    /// @param path    フォルダのパス。null か空文字列なら取り外す
    /// @param protect 書き込み禁止にするか
    /// @return 接続できたか（取り外しは常に true）
    bool mount(const wchar_t *path,bool protect);
    /// CPU の命令ごとに呼ばれ、バスの信号の変化を処理する。
    /// @param now  CPU のサイクル数
    /// @param xout XOUT 信号
    /// @param busy BUSY 信号（ROM のストローブ）
    /// @param data E ポート出力（EOH）の下位4ビット。ビット2がデバイスコードの1ビット
    void tick(uint64_t now,bool xout,bool busy,uint8_t data);
    // Busy (4) counts from the acknowledge of our own code, not while any code
    // passes - except a code (a tone) cutting into a transfer, which goes on.
    /// 状態のビット。1：接続中、2：書き込み禁止、4：通信中（自分のコードを受け付けてから。
    /// 音などのコードが転送に割り込んでいる間も含む）、8：エラー、16：今は状態保存できない。
    int status() const {
        Phase at=phase==Device || phase==Selected?code.resume:phase;
        return mounted?(1|(readOnly?2:0)|(at>=SelectedAck?4:0)|(error?8:0)|(!canSnapshot()?16:0)):0;
    }
    /// トレース（コマンドとエラーの記録）を文字列で取り出す。記録は消さない。
    /// @return 書き込んだバイト数（終端の 0 を除く）
    int trace(char *out,int length);
    /// 今、状態保存してよいか。転送中・ファイルを開いている間は不可。
    /// ほかの機器のコードが通り過ぎているだけなら可（その状態は saveCode で保存する）。
    bool canSnapshot() const;
    // A device code going by with no transfer of ours under way - the printer's,
    // or our own before it is accepted - goes into a snapshot, so that the
    // printer never holds one off and a restored machine finishes the code.
    /// スナップショットに入れる、デバイスコードの受信状態。自分の転送がないときだけ意味を持つ。
    struct CodeState {uint8_t phase,bits,device,ack,oldBusy,oldXout,reserved[2];uint64_t lastActivity;};
    /// 今のデバイスコードの受信状態を返す。待機中か保存できない状態なら、空（待機）を返す。
    CodeState saveCode() const;
    // Whether a device code could leave the drive in this state by time now.
    /// CodeState が、デバイスコードの受信でありうる状態かを確かめる。
    static bool validCode(const CodeState& s,uint64_t now);
    // False, changing nothing, for a state validCode refuses. Unmounted, the
    // drive has no part in the bus and keeps none of it.
    /// CodeState を復元する。validCode が断れば何も変えずに false。未接続なら待機に戻すだけ。
    bool restoreCode(const CodeState& s,uint64_t now);
    /// バス上の状態と転送中のデータを捨て、待機に戻す。開いているファイルと、線の前回の状態は残す。
    void resetBus();
    /// 本体の RESET：resetBus に加えて、開いているファイルと転送を閉じ、エラーを消す。接続は残す。
    void reset();
    /// 書きかけのファイル（PRINT# の書き込み先）を書き出す。
    ~Ce140f();
    /// 受け取ったパケット（末尾はチェックサム）を1つ処理し、ROM へ返す応答を返す。
    /// @param packet コマンドのバイト列とチェックサム
    /// @return 応答のバイト列（エラーなら FFh）
    std::vector<uint8_t> command(const std::vector<uint8_t>& packet);
private:
    /// バス上の段階（クラスの説明を参照）。
    enum Phase {Idle,Device,Selected,SelectedAck,Receive,Transmit,TransmitAck,TransmitGap} phase=Idle;
    /// mounted：接続中。readOnly：書き込み禁止。error：直前のコマンドがエラー。
    /// oldBusy・oldXout：前回の信号（線の状態なので、未接続の間も追い、resetBus でも残す）。
    /// resumeAck：Device に移る前の ACK。
    bool mounted=false,readOnly=false,error=false,oldBusy=false,oldXout=false,resumeAck=false;
    /// deadline：応答の待ち時間の期限。lastByte：最後にバイトを受け取った時刻。
    /// lastActivity：最後にバスが動いた時刻（5秒動かなければタイムアウト）。
    uint64_t deadline=0,lastByte=0,lastActivity=0;
    /// 受け取り中のデバイスコード。code.resume は、Device に移る前の段階（音のあと戻る先）。
    BusCode<Phase> code;
    /// nibble：受信中のニブルの位置（0 か 1）。txNibble：応答の何ニブル目まで返したか。
    /// continuation：続きのパケットを待つコマンド。
    int nibble=0,txNibble=0,continuation=0;
    /// 受信中のバイトの下位ニブル。
    uint8_t low=0;
    /// 受信中のパケットと、返す応答。
    std::vector<uint8_t> rx,tx;
    /// ドライブとして使うフォルダ。
    std::filesystem::path root;
    /// トレースの記録（16KB を超えたら古い分を捨てる）。
    std::string log;
    /// 開いているファイル。mode：0 閉じている、1 読み出し、2 書き込み、3 追記。
    /// dirty：PRINT# で書いたがまだファイルに書き出していない（CLOSE などで書き出す）。
    struct File {std::string name;std::vector<uint8_t> bytes;size_t position=0;int mode=0;bool dirty=false;};
    /// SAVE・LOAD で転送中のファイル。
    File transfer;
    /// OPEN で開いたファイル（ファイル番号 #2〜#7）。
    std::array<File,6> files;
    /// PRINT# の書き込み先、SAVE の予定サイズ、FILES の一覧と今の位置。
    int printFile=0,expectedSize=0,listIndex=-1;
    std::vector<std::string> listing;
    /// 8.3 形式のファイル名を検査し、フォルダ内のパスにする（予約名やリンクは拒否）。
    std::filesystem::path safePath(const std::string& name) const;
    /// パケットの offset から12バイトのファイル名を取り出す（空白を除き大文字に）。
    std::string fileName(const std::vector<uint8_t>& data,size_t offset) const;
    /// ファイルをまるごと読む（1MB まで）。
    std::vector<uint8_t> read(const std::string& name) const;
    /// ファイルを一時ファイル経由で書き換える（書き込み禁止なら例外）。
    void write(const std::string& name,const std::vector<uint8_t>& bytes);
    /// ファイルに読み取り専用の属性が付いているか。
    bool protectedFile(const std::string& name) const;
    /// 書き込めなければ例外を投げる（ドライブかファイルが書き込み禁止）。
    void requireWrite(const std::string& name) const;
    /// トレースに1行書き足す。
    void note(const std::string& text);
    /// 書きかけのファイルをファイルに書き出す（失敗は例外）。
    void flush(File& file);
    /// 開いているファイルの書きかけをすべて書き出す（失敗はトレースに残す）。
    void flushAll();
};
