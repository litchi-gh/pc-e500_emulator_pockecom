// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace PokeCom;

// The LCD used to be drawn on every other 8 ms tick, 62.5 times a second, on a
// 60 Hz screen: every 0.4 s two drawings fell into one refresh and a frame was
// skipped, a regular hitch in anything that moved, and the afterimage faded by
// uneven steps. Now a thread waits for each refresh of the desktop (DwmFlush
// returns once the compositor has presented a frame) and asks the window to run
// the machine up to that moment and draw, once per refresh, whatever the
// screen's rate. The 8 ms timer still runs the machine between frames for the
// keys and the sound.
// 以前は 8ms ごとのタイマーの2回に1回（1秒に62.5回）描いていたため、60Hz の画面では0.4秒ごとに1コマ飛んで動きが引っかかり、
// 残像も不ぞろいに薄れた。いまはデスクトップの書き換え（DwmFlush）を待つスレッドが、書き換えのたびに1回、本体を今まで進めて描かせる。
/// <summary>画面の書き換えごとに、ウィンドウに1回描かせる。</summary>
internal sealed class FramePacer : IDisposable
{
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    readonly Control owner;
    readonly Action frame;
    readonly Thread thread;
    volatile bool stopped;
    int pending;
    /// <param name="owner">描かせるウィンドウ（その UI スレッドで frame を呼ぶ）</param>
    /// <param name="frame">書き換えごとに呼ぶ処理</param>
    internal FramePacer(Control owner,Action frame) {
        this.owner=owner;this.frame=frame;
        thread=new Thread(Run){IsBackground=true,Name="Frame pacer",Priority=ThreadPriority.AboveNormal};
        thread.Start();
    }
    void Run() {
        var clock=Stopwatch.StartNew();
        while(!stopped) {
            double before=clock.Elapsed.TotalMilliseconds;
            int result;
            try{result=DwmFlush();}catch(Exception){result=-1;}
            // A minimised window, or no compositor, returns at once: wait a frame's time instead.
            if(result!=0 || clock.Elapsed.TotalMilliseconds-before<1)Thread.Sleep(16);
            // One frame waiting at a time: a busy window skips refreshes, it never queues them.
            if(stopped || Interlocked.Exchange(ref pending,1)!=0)continue;
            try{owner.BeginInvoke(()=>{Volatile.Write(ref pending,0);if(!stopped)frame();});}
            catch(Exception){Volatile.Write(ref pending,0);if(owner.IsDisposed)return;}
        }
    }
    public void Dispose(){stopped=true;}
}
