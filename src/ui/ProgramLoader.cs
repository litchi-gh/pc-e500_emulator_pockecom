// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>
/// 「プログラム読込」：BAS・BIN のファイルを本体に読み込む。機械語エリアの確保も行う。
/// </summary>
/// <param name="getCore">今の本体（なければ null）</param>
/// <param name="input">キー入力</param>
/// <param name="paused">一時停止中か</param>
/// <param name="driveFolder">CE-140F のフォルダ</param>
/// <param name="notify">メッセージを知らせる</param>
/// <param name="refreshed">メモリを書き換えたあと、画面を更新する</param>
/// <param name="ask">はい・いいえを聞く（題、質問、取り消しも選べるか）</param>
internal sealed class ProgramLoader(Func<Core?> getCore,IKeyInput input,Func<bool> paused,Func<string> driveFolder,Action<string> notify,Action refreshed,Func<string,string,bool,Task<ProgramLoader.Answer>> ask)
{
    /// <summary>質問の答え。</summary>
    internal enum Answer {Yes,No,Cancel}
    const int CpuHz=768000;
    /// <summary>機械語エリアについての質問の題。</summary>
    internal static string MachineCodeTitle=>L.T("機械語エリア","Machine-code area");
    /// <summary>確保が終わるのを待って書き込む BIN（名前、ファイルの中身、確保する大きさ）。</summary>
    (string Name,byte[] Data,int Size)? pendingBin;
    /// <summary>確保を待っている BIN があれば、やめる。</summary>
    /// <returns>やめた BIN の名前（なければ null）</returns>
    internal string? Drop(){string? name=pendingBin?.Name;pendingBin=null;return name;}
    // Asked only when the bytes go into the internal RAM below the system bytes and
    // not wholly into the area as it is; the area is taken from the BIN's start
    // up. Yes reserves (at BASIC's prompt) and loads once that is done; No loads
    // as before; Cancel loads nothing.
    /// <summary>機械語エリアの外に書く BIN なら、確保してから読み込むかを聞く。確保するなら始めたことのメッセージ、
    /// このまま読み込むなら null を返す（やめたら例外）。</summary>
    async Task<string?> MachineCodeOffer(Core core,string name,int start,int length) {
        if(length<=0)return null;
        int bottom=0xc0000-core.InternalKB*1024,end=start+length;
        // Only the RAM really there: an address beyond it lands on a mirror of
        // another part (the program, the stacks), so such a file is refused.
        // 実際にある RAM だけ。範囲外の番地は別の場所（プログラムやスタック）の写しに当たるので断る。
        bool inInternal=start>=bottom && end<=0xc0000,inCard=core.CardKB>0 && start>=0x40000 && end<=0x40000+core.CardKB*1024;
        if(!inInternal && !inCard)throw new InvalidDataException(L.T($"{name} は {start:X5}〜{end-1:X5} に入るファイルですが、この本体の RAM にはその範囲がありません。デバッグ（Ctrl+D）の「BIN書込み」で書く場所を指定してください。",
            $"{name} goes to {start:X5}-{end-1:X5}, which is not in this machine's RAM. Use BIN load in the debugger (Ctrl+D) and give the address."));
        // What is in use there (the program, the variables, the stacks, the work
        // areas, the system bytes) is not written over as it is: only a reservation,
        // which moves it out of the way first, can make the room.
        // 使用中の所（プログラム、変数、スタック、作業域、システム）にはそのまま書かない。確保（先にそれらをずらす）だけができる。
        bool clobbers=(inInternal?RamLayout.Internal(core):RamLayout.Card(core)).Any(r=>r.Kind is not (RegionKind.Free or RegionKind.MachineCode) && r.Start<end && start<r.End);
        string inUse=L.T($"{name} は {start:X5}〜{end-1:X5} に入りますが、ここには使用中の所（BASIC のプログラム・変数・作業域など）があり、そのまま書くと壊れます。",
            $"{name} goes to {start:X5}-{end-1:X5}, where parts in use (the BASIC program, variables, work areas) are; writing it there as it is would break them.");
        if(!inInternal || end>0xbfc00 || MachineCodeArea.Read(core) is not {} area || start>=area.Start) {
            if(clobbers)throw new InvalidDataException(inUse+L.T(" デバッグ（Ctrl+D）の「BIN書込み」なら書けます。"," BIN load in the debugger (Ctrl+D) can still write it."));
            return null;
        }
        int size=0xbfc00-start,free=BasicImage.FreeBytes(core);
        int mode=Native.e500_symbols(core.Handle,0);
        bool canReserve=free>=0 && size<=MachineCodeArea.Largest(area.Size,free) && (mode&1)==0 && (mode&6)!=0 && !paused();
        if(clobbers) {
            if(!canReserve)throw new InvalidDataException(inUse+L.T(size>MachineCodeArea.Largest(area.Size,free)?" 空きが足りないため、機械語エリアとしても確保できません。":" BASIC の RUN か PRO の表示で読み込むと、先に機械語エリアを確保できます。",
                size>MachineCodeArea.Largest(area.Size,free)?" There is not enough free RAM to reserve it as the machine-code area either.":" Load it at BASIC's RUN or PRO prompt to reserve the machine-code area first."));
            string reserve=inUse+L.T($"\n\n機械語エリアを {size:X}h バイト（{start:X5}〜BFBFF）確保してから読み込みますか？",$"\n\nReserve {size:X}h bytes ({start:X5}-BFBFF) for machine code first, then load?");
            var yes=await ask(MachineCodeTitle,reserve,false);
            if(yes!=Answer.Yes)throw new OperationCanceledException(L.T("読み込みをやめました。","Loading was cancelled."));
            Reserve(size);
            return L.T($"機械語エリアを {size:X}h バイト確保してから、{name} を読み込みます。",$"Reserving {size:X}h bytes for machine code, then loading {name}.");
        }
        string where=L.T($"{name} は {start:X5}〜{end-1:X5} に入りますが、ここは機械語エリアの外（BASIC の空き）です。BASIC のプログラムや変数に上書きされることがあります。",
            $"{name} goes to {start:X5}-{end-1:X5}, outside the machine-code area (in BASIC's free space), where the BASIC program or variables can overwrite it.");
        string question=canReserve
            ?where+L.T($"\n\n機械語エリアを {size:X}h バイト（{start:X5}〜BFBFF）確保してから読み込みますか？\n「いいえ」はこのまま読み込みます。",$"\n\nReserve {size:X}h bytes ({start:X5}-BFBFF) for machine code first, then load?\nNo loads it as it is.")
            :where+L.T(size>MachineCodeArea.Largest(area.Size,free)?"\n\n空きが足りないため、ここまでは確保できません。このまま読み込みますか？":"\n\n確保は BASIC の RUN か PRO の表示でできます。このまま読み込みますか？",
                size>MachineCodeArea.Largest(area.Size,free)?"\n\nThere is not enough free RAM to reserve that much. Load it as it is?":"\n\nReserving needs BASIC's RUN or PRO prompt. Load it as it is?");
        var answer=await ask(MachineCodeTitle,question,canReserve);
        if(answer==Answer.Cancel || (!canReserve && answer==Answer.No))throw new OperationCanceledException(L.T("読み込みをやめました。","Loading was cancelled."));
        if(!canReserve || answer!=Answer.Yes)return null;
        Reserve(size);
        return L.T($"機械語エリアを {size:X}h バイト確保してから、{name} を読み込みます。",$"Reserving {size:X}h bytes for machine code, then loading {name}.");
    }
    // Once the typed reservation has gone through (nothing left to type), the BIN
    // goes in if the area came out as asked; otherwise nothing is written.
    /// <summary>確保を待っている BIN があれば、確保が終わったところで書き込む。</summary>
    internal void Finish() {
        if(pendingBin is not {} job || getCore() is not {} core || !input.Idle)return;
        pendingBin=null;
        if(MachineCodeArea.Read(core) is not {Size:var got} || got<job.Size){notify(L.T($"機械語エリアを確保できなかったため、{job.Name} は読み込みませんでした。",$"The machine-code area was not reserved, so {job.Name} was not loaded."));return;}
        try {
            var (address,length)=BinaryTransfer.Load(core,job.Data,0,true);
            refreshed();
            notify(L.T($"機械語エリアを確保し、{job.Name} を {address:X5}-{address+length-1:X5} に書き込みました。",$"Reserved the machine-code area and wrote {job.Name} to {address:X5}-{address+length-1:X5}."));
        } catch(Exception ex) {notify(ex.Message);}
    }
    /// <summary>機械語エリアを size バイトにする（ROM の確保の処理を打ち込み、メインメニューから BASIC に戻す）。</summary>
    /// <returns>結果のメッセージ</returns>
    internal string Reserve(int size) {
        if(getCore() is not {} core)throw new InvalidOperationException(L.T("ROMが読み込まれていません。","No ROM is loaded."));
        if(paused())throw new InvalidOperationException(L.T("一時停止を解除してください。","Resume the machine first."));
        var area=MachineCodeArea.Read(core);int free=BasicImage.FreeBytes(core);
        if(area is not {} a || free<0)throw new InvalidOperationException(L.T("今の RAM からは機械語エリアを読めません。","The machine-code area cannot be read from the RAM now."));
        if(size<0 || size>MachineCodeArea.Largest(a.Size,free))throw new InvalidOperationException(L.T($"大きすぎます。いちばん大きくて {MachineCodeArea.Largest(a.Size,free):X}h バイトです。",$"Too large: the most is {MachineCodeArea.Largest(a.Size,free):X}h bytes."));
        if(size==a.Size)return L.T("機械語エリアの大きさは変わりません。","The machine-code area stays the size it is.");
        int mode=Native.e500_symbols(core.Handle,0);
        if((mode&1)!=0)throw new InvalidOperationException(L.T("プログラムの実行中です。BRK（Esc）で止めてから行ってください。","A program is running. Stop it with BRK (Esc) first."));
        if((mode&6)==0)throw new InvalidOperationException(L.T("BASIC の RUN モードか PRO モードにしてから行ってください（BASIC キー、PC の F7）。","Switch to BASIC's RUN or PRO mode first (the BASIC key, F7 on the PC)."));
        input.Tap(Keyboard.Codes["C-CE"]);
        input.Paste(MachineCodeArea.Command(size)+"\n");
        // The ROM ends at the main menu; a second later, BASIC brings the prompt back.
        input.Queue.Enqueue((Keyboard.Codes["BASIC"],false,CpuHz));
        input.Tap(Keyboard.Codes["BASIC"]);
        return size==0?L.T("機械語エリアを返しています。","Giving the machine-code area back.")
            :L.T($"機械語エリアを {size:X}h バイト（{size} バイト）にしています。",$"Making the machine-code area {size:X}h bytes ({size} bytes).");
    }
    // A machine-code file with the SAVE M header goes straight into memory at the
    // address the header gives, in any mode. Anything else is a BASIC program,
    // text or the machine's own tokenised form: the ROM reads either from the
    // CE-140F, so the file is put in the drive's folder (under a name the drive
    // takes) and LOAD "X:..." is typed, as the user would.
    // SAVE M のヘッダーがある機械語は、ヘッダーのアドレスへそのまま書く（どのモードでも）。ほかは BASIC のプログラム
    //（テキストでも中間コードでも ROM が読む）として、CE-140F のフォルダに置き、LOAD "X:..." を打つ。
    /// <summary>BAS・BIN のファイルを本体に読み込む。</summary>
    /// <param name="path">読み込むファイル</param>
    /// <param name="run">BAS なら読み込んだあと実行する（LOAD "X:...",R）</param>
    /// <returns>結果のメッセージ</returns>
    internal async Task<string> Load(string path,bool run) {
        if(getCore() is not {} core)throw new InvalidOperationException(L.T("ROMが読み込まれていません。","No ROM is loaded."));
        if(paused())throw new InvalidOperationException(L.T("一時停止を解除してから読み込んでください。","Resume the machine before loading."));
        // One load at a time: the last one may still be typing (a reservation, a LOAD).
        // 読み込みは1つずつ（前の読み込みが、確保や LOAD をまだ打っていることがある）。
        if(pendingBin is not null || input.Queue.Count>0)throw new InvalidOperationException(L.T("前の読み込み（キー入力）が終わってから読み込んでください。","Wait for the last load (keys still being typed) to finish."));
        var info=new FileInfo(path);
        if(!info.Exists)throw new FileNotFoundException(L.T("ファイルが見つかりません。","The file was not found."),path);
        if(info.Length>0x100000+BinaryTransfer.HeaderLength)throw new InvalidDataException(L.T("1MBを超えるファイルは読み込めません。","Files larger than 1MB cannot be loaded."));
        byte[] data=File.ReadAllBytes(path);
        bool bin=string.Equals(info.Extension,".bin",StringComparison.OrdinalIgnoreCase);
        if(BinaryTransfer.TryReadHeader(data,out int binStart,out int binLength)) {
            // Machine code outside the machine-code area sits in BASIC's free space,
            // where the program, the variables or a later reservation can overwrite
            // it: offer to take the area down to its start first, then load.
            // 機械語エリアの外の機械語は BASIC の空きにあり、上書きされうるので、先に確保してから読み込むかを聞く。
            if(await MachineCodeOffer(core,info.Name,binStart,binLength) is string reserving){pendingBin=(info.Name,data,0xbfc00-binStart);return reserving;}
            var (address,length)=BinaryTransfer.Load(core,data,0,true);
            refreshed();
            return L.T($"{info.Name} を {address:X5}-{address+length-1:X5} に書き込みました（{length}バイト）。",$"Wrote {info.Name} to {address:X5}-{address+length-1:X5} ({length} bytes).");
        }
        // A program the machine saved goes straight into RAM: no LOAD, no CE-140F,
        // any mode. A layout this does not know, or a text file, goes on to LOAD.
        // 本体が保存した形のプログラムは RAM に直接入れる（LOAD も CE-140F も使わず、どのモードでも）。
        if(!bin && BasicImage.IsSaved(data)) {
            if((Native.e500_symbols(core.Handle,0)&1)!=0)throw new InvalidOperationException(L.T("プログラムの実行中です。BRK（Esc）で止めてから読み込んでください。","A program is running. Stop it with BRK (Esc) first."));
            if(BasicImage.Load(core,data)) {
                refreshed();
                string loaded=L.T($"{info.Name} を RAM に直接読み込みました（{data.Length}バイト）。",$"Loaded {info.Name} straight into RAM ({data.Length} bytes).");
                if(!run)return loaded;
                // RUN is typed at BASIC's prompt only; elsewhere it would be a stray key.
                if((Native.e500_symbols(core.Handle,0)&6)==0)return loaded+L.T(" 実行は BASIC の RUN か PRO モードで RUN と打ってください。"," To run it, type RUN in BASIC's RUN or PRO mode.");
                input.Tap(Keyboard.Codes["C-CE"]);input.Paste("RUN\n");
                return L.T($"{info.Name} を RAM に直接読み込んで実行します。",$"Loaded {info.Name} straight into RAM and running it.");
            }
        }
        if(bin)throw new InvalidDataException(L.T("SAVE M のヘッダーがない BIN は、書き込むアドレスが分かりません。デバッグ（Ctrl+D）の「BIN書込み」でアドレスを指定してください。","A BIN without the SAVE M header does not say where it goes. Use BIN load in the debugger (Ctrl+D) and give the address."));
        // A BASIC program goes through the CE-140F, with the machine at its BASIC prompt.
        if((Native.e500_disk_status(core.Handle)&1)==0)throw new InvalidOperationException(L.T("BAS の読み込みには CE-140F を使います。「設定」で CE-140F を接続してください。","A BAS file loads through the CE-140F. Connect the CE-140F in Settings."));
        int mode=Native.e500_symbols(core.Handle,0);
        if((mode&1)!=0)throw new InvalidOperationException(L.T("プログラムの実行中です。BRK（Esc）で止めてから読み込んでください。","A program is running. Stop it with BRK (Esc) first."));
        if((mode&6)==0)throw new InvalidOperationException(L.T("BASIC の RUN モードか PRO モードにしてから読み込んでください（BASIC キー、PC の F7）。","Switch to BASIC's RUN or PRO mode first (the BASIC key, F7 on the PC)."));
        string name=DriveName(info.Name),target=Path.Combine(driveFolder(),name);
        if(!string.Equals(Path.GetFullPath(target),info.FullName,StringComparison.OrdinalIgnoreCase)) {
            if(File.Exists(target) && !File.ReadAllBytes(target).AsSpan().SequenceEqual(data) &&
                await ask(L.T("プログラム読込","Load program"),L.T($"CE-140F のフォルダに、中身の違う {name} があります。上書きしますか？",$"The CE-140F folder has another {name}. Overwrite it?"),false)!=Answer.Yes)
                return L.T("読み込みをやめました。","Loading was cancelled.");
            File.WriteAllBytes(target,data);
        }
        // C-CE first: whatever is on the line is not run together with the command.
        input.Tap(Keyboard.Codes["C-CE"]);
        // ,R runs the program once it is in (the ROM's own LOAD option).
        string command=$"LOAD \"X:{name}\""+(run?",R":"");
        input.Paste(command+"\n");
        return run?L.T($"{name} を CE-140F から読み込んで実行します（{command}）。",$"Loading and running {name} from the CE-140F ({command}).")
            :L.T($"{name} を CE-140F から読み込んでいます（{command}）。",$"Loading {name} from the CE-140F ({command}).");
    }
    // The CE-140F takes an 8.3 name of A-Z, 0-9, _ and -. A PC name is cut down to
    // that: "my prog.bas" is MYPROG.BAS; one with nothing left is LOAD.BAS.
    /// <summary>PC のファイル名を、CE-140F が受け付ける 8.3 の名前（英大文字・数字・_・-）にする。</summary>
    /// <param name="fileName">PC のファイル名</param>
    internal static string DriveName(string fileName) {
        static string Keep(string s,int max)=>new string(s.ToUpperInvariant().Where(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-').Take(max).ToArray());
        string stem=Keep(Path.GetFileNameWithoutExtension(fileName),8),extension=Keep(Path.GetExtension(fileName).TrimStart('.'),3);
        if(stem.Length==0)stem="LOAD";
        // The device names are refused by the drive.
        if(stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length==4 && (stem.StartsWith("COM",StringComparison.Ordinal) || stem.StartsWith("LPT",StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9'))stem="_"+stem;
        return extension.Length==0?stem+".BAS":stem+"."+extension;
    }
}
