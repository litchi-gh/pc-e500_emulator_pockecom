// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace PokeCom;

/// <summary>音声の出力（48kHz・モノラル）。Windows は waveOut、Linux は ALSA。</summary>
internal interface ISound : IDisposable
{
    /// <summary>消音するか。</summary>
    bool Muted {get;set;}
    /// <summary>音量（0〜1）。</summary>
    float Volume {get;set;}
    /// <summary>再生のエラー（なければ null）。</summary>
    string? Error {get;}
    /// <summary>サンプルを預ける。たまりすぎたら古いものから捨てる。</summary>
    void Submit(short[] samples,int count);
    /// <summary>預けたサンプルを捨てる（一時停止や ROM の切り替えのとき）。</summary>
    void Flush();
    /// <summary>本体を進める速さの倍率（たまり具合を目標に戻す。消音中や出力がないときは 1）。UI スレッドから呼ぶ。</summary>
    double Pace();
    /// <summary>今のシステムの音声の出力を開く。</summary>
    internal static ISound Open()=>OperatingSystem.IsWindows()?new WaveSound():new AlsaSound();
}

/// <summary>Windows の音声（AudioOutput をそのまま使う）。</summary>
[SupportedOSPlatform("windows")]
internal sealed class WaveSound : ISound
{
    readonly AudioOutput output=new();
    public bool Muted {get=>output.Muted;set=>output.Muted=value;}
    public float Volume {get=>output.Volume;set=>output.Volume=value;}
    public string? Error=>output.Error;
    public void Submit(short[] samples,int count)=>output.Submit(samples,count);
    public void Flush()=>output.Flush();
    public double Pace()=>output.Pace();
    public void Dispose()=>output.Dispose();
}

// libasound is on every Ubuntu desktop, and its "default" device goes to
// PipeWire. A thread of its own writes: snd_pcm_writei blocks until the device
// has room, which paces it. The buffer is 60 ms; what waits behind it is capped,
// the oldest going, so a late frame never becomes lasting delay.
/// <summary>Linux の音声（ALSA の既定の出力。Ubuntu では PipeWire につながる）。書き込みは専用のスレッドで行う。</summary>
internal sealed class AlsaSound : ISound
{
    const string Lib="libasound.so.2";
    [DllImport(Lib)] static extern int snd_pcm_open(out IntPtr pcm,[MarshalAs(UnmanagedType.LPUTF8Str)] string name,int stream,int mode);
    [DllImport(Lib)] static extern int snd_pcm_set_params(IntPtr pcm,int format,int access,uint channels,uint rate,int resample,uint latency);
    [DllImport(Lib)] static extern nint snd_pcm_writei(IntPtr pcm,short[] buffer,nuint frames);
    [DllImport(Lib)] static extern int snd_pcm_recover(IntPtr pcm,int error,int silent);
    [DllImport(Lib)] static extern int snd_pcm_drop(IntPtr pcm);
    [DllImport(Lib)] static extern int snd_pcm_prepare(IntPtr pcm);
    [DllImport(Lib)] static extern int snd_pcm_close(IntPtr pcm);
    [DllImport(Lib)] static extern IntPtr snd_strerror(int error);
    const int Rate=48000,Chunk=256,Latency=60000,MaxQueued=Chunk*16,Primer=Chunk*8;
    readonly object gate=new();
    readonly Queue<short> queued=new();
    readonly AutoResetEvent work=new(false);
    readonly Thread? worker;
    readonly IntPtr pcm;
    volatile bool running=true,resetWanted;
    double previous,filtered;
    public bool Muted {get;set;}
    public float Volume {get;set;}=0.5f;
    public string? Error {get;private set;}
    /// <summary>デバイスに渡したサンプル数と、足りなくなった回数（テスト用）。</summary>
    internal long Delivered;
    internal int Underruns;
    internal AlsaSound() {
        try {
            int result=snd_pcm_open(out pcm,"default",0,0);
            // S16_LE, interleaved, one channel.
            if(result>=0)result=snd_pcm_set_params(pcm,2,3,1,Rate,1,Latency);
            if(result<0)throw new InvalidOperationException(Marshal.PtrToStringUTF8(snd_strerror(result)));
        } catch(Exception ex) {
            Error=L.T("音声出力を開けません: ","Could not open the audio output: ")+ex.Message;
            if(pcm!=IntPtr.Zero)snd_pcm_close(pcm);
            pcm=IntPtr.Zero;return;
        }
        worker=new Thread(Pump){IsBackground=true,Name="PokeCom audio"};
        worker.Start();
    }
    public void Submit(short[] samples,int count) {
        if(pcm==IntPtr.Zero)return;
        lock(gate) {
            if(Muted){queued.Clear();return;}
            for(int i=0;i<count;i++)queued.Enqueue(samples[i]);
            while(queued.Count>MaxQueued)queued.Dequeue();
        }
        work.Set();
    }
    public void Flush() {lock(gate)queued.Clear();pace.Reset();resetWanted=true;work.Set();}
    // The device holds its 60 ms; the pace keeps the primer waiting behind it.
    readonly AudioPace pace=new();
    public double Pace() {int n;lock(gate)n=queued.Count;return pcm==IntPtr.Zero || Muted?1:pace.Rate(n,Primer);}
    void Pump() {
        bool primed=false;var chunk=new short[Chunk];
        while(running) {
            if(resetWanted){resetWanted=false;snd_pcm_drop(pcm);snd_pcm_prepare(pcm);previous=filtered=0;primed=false;}
            bool take;
            lock(gate) {
                // A cushion before the first write, so that the device does not run dry at once.
                if(!primed && queued.Count>=Primer)primed=true;
                take=primed && queued.Count>=Chunk;
                if(take)for(int i=0;i<Chunk;i++)chunk[i]=queued.Dequeue();
            }
            if(!take){work.WaitOne(4);continue;}
            for(int i=0;i<Chunk;i++){double s=chunk[i];filtered=0.995*(filtered+s-previous);previous=s;chunk[i]=(short)Math.Clamp(filtered*Volume,-32767,32767);}
            nint wrote=snd_pcm_writei(pcm,chunk,Chunk);
            if(wrote<0) {
                // Run dry (or suspended): started again, with a cushion.
                Underruns++;primed=false;
                int result=snd_pcm_recover(pcm,(int)wrote,1);
                Error=result<0?L.T("音声再生に失敗: ","Audio playback failed: ")+Marshal.PtrToStringUTF8(snd_strerror(result)):null;
                if(result<0)Thread.Sleep(200);
            } else {Error=null;Delivered+=wrote;}
        }
    }
    public void Dispose() {
        running=false;work.Set();
        if(worker is null || !worker.Join(2000))return;
        snd_pcm_drop(pcm);snd_pcm_close(pcm);work.Dispose();
    }
}
