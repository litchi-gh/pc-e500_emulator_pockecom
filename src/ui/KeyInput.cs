// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>
/// キー入力の待ち行列：押す・離すを順に本体へ送り、ROM がキーを読み取れる長さだけ保つ。
/// PC のキーを押している間は本体のキーも押したままにする。
/// </summary>
/// <typeparam name="TBy">押したままにしている元（PC のキー、コントローラーの割り当て）</typeparam>
/// <param name="getCore">今の本体（なければ null）</param>
/// <param name="blocked">キーを受け付けないか（一時停止中など）</param>
/// <param name="show">画面のキーを押した見た目にする・戻す</param>
internal sealed class KeyInput<TBy>(Func<Core?> getCore,Func<bool> blocked,Action<string,bool> show) where TBy : notnull
{
    // The ROM scans the key matrix from its 64 Hz sub-timer, and only counts a key
    // whose press and release land in different scans with one scan between them.
    // 18000 down plus 24000 up (one and a half scans, then two) is the shortest pair that never drops a key; the
    // 120000/100000 at 1.024 MHz that this replaces spent 215 ms per key, so anything faster than
    // five keys a second piled up into a backlog that felt like input lag.
    // A shifted character only comes out shifted when SHIFT is already down for a
    // whole scan before the key joins it, and stays down for a whole scan after the
    // key leaves. The 8000 that both used to be is half a scan, so whichever
    // keystroke the scan phase fell on lost its SHIFT: pasting LOAD M "X:TETRIS.BIN"
    // dropped the closing quote to a bare W, and typing quotes gave one W in four.
    internal const int CpuHz=768000,KeyDownCycles=18000,KeyUpCycles=24000,ModeSettleCycles=200000,ModifierCycles=7500,
        ShiftLeadCycles=18000,ShiftTailCycles=18000;
    /// <summary>待ち行列：キーコード、押すか離すか、その状態を保つサイクル数。</summary>
    internal readonly Queue<(int Code,bool Down,int Cycles)> Queue=new();
    /// <summary>画面でクリックして押したままの SHIFT・CTRL。</summary>
    internal readonly HashSet<int> Held=new();
    // A PC key held down holds the machine's key down until it comes up, as a
    // finger on the real keys would: the ROM's own repeat (BS, DEL, the cursor
    // keys) and games that read a held key then work as on the machine.
    // Windows' repeats are not used; they only ever were taps.
    /// <summary>元ごとに、押したままにしている本体のキーと、一緒に押している SHIFT・CTRL（なければ -1）。</summary>
    internal readonly Dictionary<TBy,(int Code,int Modifier)> Holding=new();
    /// <summary>今のキーの状態を保つ残りのサイクル数（0 なら次のキーへ）。</summary>
    internal int Wait;
    /// <summary>待ち行列が空で、最後のキーの時間も過ぎたか。</summary>
    internal bool Idle=>Queue.Count==0 && Wait==0;
    /// <summary>送り終えるまでのサイクル数。</summary>
    internal int Backlog=>Wait+Queue.Sum(x=>x.Cycles);
    static int Shift=>Keyboard.Codes["SHIFT"];
    static bool ChangesMode(int code)=>code==Keyboard.Codes["BASIC"] || code==Keyboard.Codes["MENU"] || code==Keyboard.Codes["F1"];
    static string NameOf(int code)=>Keyboard.Matrix[code/8][code%8];
    /// <summary>キーを1回押して離す操作を待ち行列に入れ、すぐに始める。</summary>
    /// <param name="code">キーコード</param>
    internal void Tap(int code) {
        if(getCore() is null || blocked())return;
        Queue.Enqueue((code,true,KeyDownCycles));Queue.Enqueue((code,false,KeyUpCycles));
        if(ChangesMode(code))Queue.Enqueue((code,false,ModeSettleCycles));
        // Keep a clicked modifier held until the character's matrix scan completes.
        foreach(var modifier in Held.ToArray()) {Queue.Enqueue((modifier,false,ModifierCycles));Held.Remove(modifier);}
        // Press now instead of on the next frame: the 16 ms timer would otherwise
        // add its own wait in front of the ROM scan on every keystroke.
        Next();
    }
    // The PC's Ctrl is no longer held down in the machine while it is held on the
    // PC: with a key it goes into the queue around that key, the way SHIFT does.
    // Held down, it reached the first key of a Ctrl+V paste, and stayed down
    // after a Ctrl+letter that sent nothing.
    /// <summary>modifier（SHIFT・CTRL）を押したまま code のキーを押す操作を待ち行列に入れる。</summary>
    internal void TapWith(int modifier,int code) {
        Queue.Enqueue((modifier,true,ShiftLeadCycles));
        Tap(code);
        Queue.Enqueue((modifier,false,ShiftTailCycles));
    }
    /// <summary>画面のキーのクリック：SHIFT・CTRL は次のキーまで押したままにし、ほかは1回押す。</summary>
    /// <param name="name">キーの名前</param>
    /// <param name="ctrl">PC の Ctrl を押しているか（CTRL を付ける）</param>
    internal void Click(string name,bool ctrl) {
        int code=Keyboard.Codes[name];
        // A clicked SHIFT or CTRL joins the queue, so it reaches the key after the
        // ones still waiting, not those too; it stays down for the next key.
        // 画面の SHIFT・CTRL は待ち行列に入れる（待っている前のキーには効かせず、次のキーまで押したまま）。
        if(name is "SHIFT" or "CTRL") {
            bool down=Held.Add(code);if(!down)Held.Remove(code);
            Queue.Enqueue((code,down,down?ShiftLeadCycles:ModifierCycles));Next();show(name,down);
        }
        // The PC's Ctrl held while a key is clicked adds CTRL to it.
        else if(ctrl)TapWith(Keyboard.Codes["CTRL"],code);
        else Tap(code);
    }
    /// <summary>前のキーの時間が過ぎていれば、待ち行列の次の操作を本体へ送る。</summary>
    /// <returns>送ったか</returns>
    internal bool Next() {
        if(getCore() is not {} core || Wait!=0 || !Queue.TryDequeue(out var key))return false;
        core.Key(key.Code,key.Down);Wait=key.Cycles;
        show(NameOf(key.Code),key.Down);
        return true;
    }
    // Windows repeats a held key thirty-one times a second. Those repeats used to
    // be sent as taps (and thinned so they never piled up); now the key is simply
    // held, and the repeats are ignored, so nothing can pile up behind a held key.
    /// <summary>本体のキーを押し、by が離されるまで押したままにする（modifier は一緒に押す SHIFT・CTRL、なければ -1）。</summary>
    internal void Hold(TBy by,int code,int modifier) {
        if(getCore() is null || blocked() || Holding.ContainsKey(by))return;
        if(modifier>=0)Queue.Enqueue((modifier,true,ShiftLeadCycles));
        // The press stays at least one key time, so a quick tap still reaches a scan.
        Queue.Enqueue((code,true,KeyDownCycles));
        Holding[by]=(code,modifier);Next();
    }
    /// <summary>by で押したままにしていた本体のキーを離す。</summary>
    internal void Unhold(TBy by) {
        if(!Holding.Remove(by,out var h))return;
        Queue.Enqueue((h.Code,false,KeyUpCycles));
        if(ChangesMode(h.Code))Queue.Enqueue((h.Code,false,ModeSettleCycles));
        if(h.Modifier>=0)Queue.Enqueue((h.Modifier,false,ShiftTailCycles));
        // A SHIFT or CTRL clicked on the case goes with this key, as with a tap.
        foreach(var modifier in Held.ToArray()){Queue.Enqueue((modifier,false,ModifierCycles));Held.Remove(modifier);show(NameOf(modifier),false);}
        Next();
    }
    /// <summary>押したままの本体のキーをすべて離す（ウィンドウからフォーカスが離れたとき）。</summary>
    internal void UnholdAll(){foreach(var by in Holding.Keys.ToArray())Unhold(by);}
    /// <summary>本体のキーをすべて離し、待ち行列を空にする。</summary>
    internal void Clear() {Holding.Clear();getCore()?.Release();Held.Clear();Queue.Clear();Wait=0;}
    /// <summary>待ち行列を進めながら、CPU を cycles サイクル実行する。</summary>
    internal void Run(int cycles) {
        if(getCore() is not {} core)return;
        while(cycles>0) {
            Next();
            int batch=Wait>0?Math.Min(cycles,Wait):cycles;
            core.Run(batch);cycles-=batch;if(Wait>0)Wait-=batch;
        }
    }
    /// <summary>1文字を本体のキー操作に変えて待ち行列に入れる。</summary>
    /// <param name="character">文字</param>
    /// <returns>対応するキーがあったか</returns>
    internal bool Character(char character) {
        if(!CharacterKey(character,out int code,out int modifier))return false;
        if(modifier>=0)TapWith(modifier,code);else Tap(code);
        return true;
    }
    /// <summary>1文字に当たる本体のキーと、一緒に押す SHIFT（なければ -1）。</summary>
    /// <returns>対応するキーがあったか</returns>
    internal static bool CharacterKey(char character,out int code,out int modifier) {
        modifier=-1;
        string text=character==' '?"SPACE":char.ToUpperInvariant(character).ToString();
        if(Keyboard.Codes.TryGetValue(text,out code))return true;
        if(Keyboard.Shifted.TryGetValue(character,out string? baseKey)){code=Keyboard.Codes[baseKey];modifier=Shift;return true;}
        return false;
    }
    /// <summary>文字列をキー入力として送る（改行は ENTER、タブは空白4つ、対応しない文字は省く）。</summary>
    /// <param name="value">貼り付ける文字列（4096文字まで）</param>
    /// <returns>結果のメッセージ</returns>
    internal string Paste(string value) {
        if(getCore() is null)return L.T("ROMが読み込まれていません。","No ROM is loaded.");
        if(blocked())return L.T("一時停止を解除してから貼り付けてください。","Resume the machine before pasting.");
        if(value.Length>4096)return L.T("貼り付けは一度に4096文字までです。","Paste up to 4096 characters at a time.");
        value=value.Replace("\r\n","\n").Replace('\r','\n');int queued=0,skipped=0;
        foreach(char character in value) {
            if(character=='\n'){Tap(Keyboard.Codes["ENTER"]);queued++;continue;}
            if(character=='\t'){for(int i=0;i<4;i++)Character(' ');queued+=4;continue;}
            if(Character(character))queued++;else skipped++;
        }
        string message=L.T($"{queued}文字をキー入力へ送信しました。",$"Sent {queued} characters as key input.");
        if(skipped>0)message+=L.T($" 未対応の{skipped}文字は省略しました。",$" Skipped {skipped} unsupported characters.");
        return message;
    }
}
