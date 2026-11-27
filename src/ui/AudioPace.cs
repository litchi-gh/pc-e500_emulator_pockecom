// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// The machine makes sound at exactly the rate the device plays it, so whatever
// is buffered stays buffered. A burst at start (the first frames run late) or a
// stalled frame filled the queue to its cap, and there it stayed: from then on
// every frame a little longer than the last went over, and the oldest samples
// were cut, tens at a time every second or two. A tone that does not stop - the
// engine of a racing game - clicked at each cut; a recording made outside real
// time has no queue and never did. Measured on this PC: the queue held 3,500 to
// 4,096 of its 4,096 samples for the whole run, and lost 10 to 80 samples every
// 0.4 to 2.5 s. So the machine is run up to 2% slower while too much waits, and
// up to 2% faster while too little does, until the queue sits at its target.
// The sound keeps its pitch (it is made in the machine's own time); only the
// machine's pace moves, by less than anyone can see, and nothing is cut.
// 本体は音を再生と同じ速さで作るので、たまった分は減らない。起動直後のまとめ出しや遅れたコマで上限まで
// たまると、そのあとはコマの長さのぶれごとに古いサンプルが数十個ずつ捨てられ、鳴り続ける音にプチッと乗った。
// たまりすぎなら本体を最大 2% 遅く、足りなければ最大 2% 速く進め、目標の量に戻す（音の高さは変わらない）。
/// <summary>音声のたまり具合から、本体を進める速さの倍率を決める。</summary>
internal sealed class AudioPace
{
    // How far the pace may move, and how hard it pulls: 2% drains 960 samples a
    // second, so the 1,500 over the target left by a start goes in under 2 s;
    // 40% over the target is already the whole 2%, 10% over is 0.5%.
    const double Limit=0.02,Gain=0.05;
    // The queue changes by a frame's samples at each call: it is smoothed over
    // about 20 calls (0.1 to 0.3 s), so one frame does not swing the pace.
    const double Smooth=0.05;
    double level=-1;

    /// <summary>倍率（1 ± 0.02）。たまっている量 buffered が目標 target より多ければ 1 より小さい。</summary>
    /// <param name="buffered">いまたまっているサンプル数</param>
    /// <param name="target">保ちたいサンプル数</param>
    internal double Rate(int buffered,int target) {
        if(target<=0)return 1;
        level=level<0?buffered:level+(buffered-level)*Smooth;
        return 1+Math.Clamp((target-level)/target*Gain,-Limit,Limit);
    }
    /// <summary>なめらかにした量を忘れる（一時停止や ROM の切り替えで、たまった分を捨てたとき）。</summary>
    internal void Reset()=>level=-1;
}
