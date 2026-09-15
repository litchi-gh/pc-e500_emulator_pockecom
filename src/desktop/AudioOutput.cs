// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
namespace PokeCom;

internal sealed class AudioOutput : IDisposable
{
    [StructLayout(LayoutKind.Sequential,Pack=2)] struct Format {public ushort tag,channels;public uint rate,bytes;public ushort align,bits,extra;}
    [StructLayout(LayoutKind.Sequential)] struct Header {public IntPtr data;public uint length,recorded;public UIntPtr user;public uint flags,loops;public IntPtr next;public UIntPtr reserved;}
    [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr handle,uint device,ref Format format,IntPtr callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);

    // waveOutWrite is not the quick hand-off its name suggests. winmm sits on the
    // Windows audio engine, and a write that has to start or restart a stream blocks
    // whoever called it: measured on this machine at 94 ms on average and 1.4 s at
    // worst, 18 times in 26 seconds. That used to happen on the UI thread, which is
    // also the thread that steps the CPU, reads the keyboard and repaints the LCD, so
    // every one of those blocks froze the whole machine - a key pressed during one
    // did not reach the ROM until it ended. Nothing but this thread touches the
    // device now. Submit copies samples under a lock and returns.
    // Cushion is what the device holds - 4 x 256 samples is 21 ms, the delay from
    // the machine driving XOUT to the click being heard. Staging has to hold a whole
    // frame on top of that, because one 15.6 ms frame hands over 740 samples in one
    // go: capping the total at Cushion+1 chunks left the staging queue 256 samples,
    // so two thirds of every frame was thrown away, the device kept running dry, and
    // only 1.15 s of the 26 s produced ever reached it.
    // Cushion is what the device is kept holding. 8 x 256 samples is 42 ms, and that
    // number is not a taste setting: below about 32 ms this device runs dry between
    // frames, and every write that has to restart a stopped stream blocks its caller.
    // At the 21 ms this had before, waveOutWrite took 91 ms on average and 1.4 s at
    // worst, and only 6% of the audio the machine produced ever reached the speaker.
    // At 42 ms it never runs dry, waveOutWrite returns in under a millisecond, and
    // 97% gets through. Slack is what may queue behind the cushion while a late
    // frame catches up; past that the oldest samples go, because anything kept here
    // is delay that nothing ever drains.
    const int Chunk=256,Cushion=8,Slack=8,MaxBuffered=Chunk*(Cushion+Slack);
    readonly object gate=new();
    readonly Queue<short> queued=new();
    readonly List<(IntPtr Header,IntPtr Data)> pending=[];
    readonly ManualResetEventSlim work=new(false);
    readonly Thread worker;
    IntPtr device;
    double previous,filtered;
    bool primed;
    volatile bool running=true,resetWanted;
    volatile int outstanding;
    // Kept for the smoke test: it is the only way to see the starvation collapse,
    // which is silent from the outside apart from the audio being wrong.
    internal long Delivered;
    internal int Restarts;
    internal bool Muted;
    internal float Volume=0.5f;
    internal string? Error {get;private set;}
    // What the device is holding, and everything buffered ahead of it.
    internal int PendingCount=>outstanding;
    internal int BufferedSamples {get{lock(gate)return queued.Count+outstanding*Chunk;}}

    internal AudioOutput() {
        // A default Windows timer only fires every ~15.6 ms, which is both the audio
        // cushion this has to keep and the emulator's input latency.
        timeBeginPeriod(1);
        var format=new Format{tag=1,channels=1,rate=48000,bytes=96000,align=2,bits=16};
        uint result=waveOutOpen(out device,uint.MaxValue,ref format,IntPtr.Zero,IntPtr.Zero,0);
        if(result!=0){device=IntPtr.Zero;Error=L.T($"音声出力を開けません (MMRESULT {result})",$"Could not open the audio output (MMRESULT {result})");}
        worker=new Thread(Pump){IsBackground=true,Name="PokeCom audio"};
        worker.Start();
    }

    // Called from the UI thread once a frame. Everything buffered is delay, and a
    // stalled frame hands over a burst of catch-up audio in one go while the device
    // only ever takes the cushion, so the rest would sit here adding permanent lag
    // that nothing drains. Drop the oldest samples instead: a few skipped
    // milliseconds once beats hearing every click late for the rest of the session.
    internal void Submit(short[] samples,int count) {
        if(device==IntPtr.Zero)return;
        lock(gate) {
            if(Muted){queued.Clear();return;}
            for(int i=0;i<count;i++)queued.Enqueue(samples[i]);
            while(queued.Count+outstanding*Chunk>MaxBuffered)queued.Dequeue();
        }
        work.Set();
    }

    internal void Flush() {
        lock(gate)queued.Clear();
        resetWanted=true;work.Set();
    }

    void Pump() {
        while(running) {
            if(resetWanted) {
                resetWanted=false;
                if(device!=IntPtr.Zero)waveOutReset(device);
                Reap();previous=filtered=0;primed=false;
            }
            Reap();
            bool wrote=false;
            if(pending.Count==0 && primed){primed=false;Restarts++;}
            while(device!=IntPtr.Zero && pending.Count<Cushion && Take() is {} chunk){Write(chunk);wrote=true;}
            if(wrote)continue;
            work.Reset();
            if(running)work.Wait(4);
        }
    }

    // Hold the first chunks back until there is a cushion to hand over: the device
    // goes silent the moment its queue runs out, and every one of those gaps clicks.
    short[]? Take() {
        lock(gate) {
            if(!primed) {
                if(queued.Count<Chunk*Cushion)return null;
                primed=true;
            }
            if(queued.Count<Chunk)return null;
            var samples=new short[Chunk];
            for(int i=0;i<Chunk;i++)samples[i]=queued.Dequeue();
            return samples;
        }
    }

    void Write(short[] samples) {
        var data=Marshal.AllocHGlobal(samples.Length*2);var header=Marshal.AllocHGlobal(Marshal.SizeOf<Header>());
        short[] pcm=new short[samples.Length];
        for(int i=0;i<samples.Length;i++){double sample=samples[i];filtered=0.995*(filtered+sample-previous);previous=sample;pcm[i]=(short)Math.Clamp(filtered*Volume,-32767,32767);}
        Marshal.Copy(pcm,0,data,pcm.Length);Marshal.StructureToPtr(new Header{data=data,length=(uint)pcm.Length*2},header,false);
        uint size=(uint)Marshal.SizeOf<Header>(),result=waveOutPrepareHeader(device,header,size);
        if(result==0){result=waveOutWrite(device,header,size);if(result!=0)waveOutUnprepareHeader(device,header,size);}
        if(result!=0){Marshal.FreeHGlobal(data);Marshal.FreeHGlobal(header);Error=L.T($"音声再生に失敗 (MMRESULT {result})",$"Audio playback failed (MMRESULT {result})");return;}
        pending.Add((header,data));outstanding=pending.Count;Delivered+=pcm.Length;
    }

    void Reap() {
        for(int i=pending.Count-1;i>=0;i--){var p=pending[i];if((Marshal.PtrToStructure<Header>(p.Header).flags&1)==0)continue;
            waveOutUnprepareHeader(device,p.Header,(uint)Marshal.SizeOf<Header>());Marshal.FreeHGlobal(p.Data);Marshal.FreeHGlobal(p.Header);pending.RemoveAt(i);}
        outstanding=pending.Count;
    }

    public void Dispose() {
        running=false;work.Set();worker.Join(1000);
        lock(gate)queued.Clear();
        if(device!=IntPtr.Zero){waveOutReset(device);Reap();waveOutClose(device);device=IntPtr.Zero;}
        work.Dispose();timeEndPeriod(1);
    }
}
