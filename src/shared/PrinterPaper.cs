// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text;
namespace PokeCom;

// The paper roll of the emulated CE-126P: printed lines in the PC-E500
// character set, kept while the program runs whether or not the window is open.
/// <summary>
/// CE-126P の紙（印字された行の巻紙）。ウィンドウを閉じても、ROM を切り替えても残る。
/// 行は PC-E500 の文字コードで持ち、2000行を超えたら古い行から捨てる。
/// </summary>
internal sealed partial class PrinterPaper
{
    // The line width is the native printer's, so the two cannot drift apart.
    /// <summary>1行の桁数（ネイティブの Ce126p::Columns）。</summary>
    internal static readonly int Columns=Native.e500_printer_columns();
    /// <summary>持っておく最大の行数。</summary>
    internal const int MaxLines=2000;
    // One character cell: the ROM font is 6 x 8, lines are fed 10 dots apart.
    /// <summary>1文字の幅、行の送り、紙の余白（ドット）。</summary>
    internal const int CellWidth=6,LineHeight=10,Margin=6;
    readonly Roll lines=new(MaxLines);
    // A read from the core can end mid-line; the rest comes with the next one.
    readonly List<byte> pending=[];
    /// <summary>紙が変わるたびに増える番号（ウィンドウが描き直すかの判断に使う）。</summary>
    internal int Version {get;private set;}
    // Lines cut from the top since the start, so a view can keep its place.
    /// <summary>上限を超えて先頭から捨てた行数の合計。</summary>
    internal long Dropped {get;private set;}
    /// <summary>今ある行数と、行の一覧（古い順）。</summary>
    internal int Count=>lines.Count;
    internal IReadOnlyList<byte[]> Lines=>lines;
    /// <summary>コアから取り出した印字を紙に加える（改行で1行、途中の行は次の分とつなぐ）。</summary>
    /// <param name="data">PC-E500 の文字コードの並び</param>
    internal void Feed(ReadOnlySpan<byte> data)
    {
        foreach(byte b in data) {
            if(b!=(byte)'\n'){pending.Add(b);continue;}
            byte[] line=[..pending];
            if(lines.Add(line))Dropped++;
            pending.Clear();Version++;
            LineAdded?.Invoke(line);
        }
    }
    /// <summary>1行が紙に加わったとき（プリンタへ送るため）。</summary>
    internal event Action<byte[]>? LineAdded;
    /// <summary>紙を切る（すべての行を消す）。</summary>
    internal void Clear(){lines.Clear();pending.Clear();Version++;}
    // The newest lines up to a fixed count, the oldest overwritten: a full roll
    // takes a line without moving the others along.
    /// <summary>決まった行数までの行を持つリングバッファ。いっぱいなら一番古い行を上書きする。</summary>
    sealed class Roll(int capacity) : IReadOnlyList<byte[]>
    {
        readonly byte[][] slots=new byte[capacity][];
        int start;
        public int Count {get;private set;}
        public byte[] this[int index]=>(uint)index<(uint)Count?slots[(start+index)%slots.Length]:throw new ArgumentOutOfRangeException(nameof(index));
        // True when the oldest line made room.
        public bool Add(byte[] line) {
            if(Count<slots.Length){slots[(start+Count++)%slots.Length]=line;return false;}
            slots[start]=line;start=(start+1)%slots.Length;return true;
        }
        public void Clear(){Array.Clear(slots);start=Count=0;}
        public IEnumerator<byte[]> GetEnumerator(){for(int i=0;i<Count;i++)yield return this[i];}
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
    }
    // The paper is text as printed: 5Ch and 7Ch are the ROM's ¥ and ¦, the way
    // the keycaps show them. The screen text keeps \ and | for them instead, to
    // be pasted back into a program (either types the same byte).
    /// <summary>PC-E500 の文字コードを Unicode の文字にする（5Ch・7Ch は印字のとおり ¥・¦。対応がなければ □）。</summary>
    internal static char ToChar(byte b)=>b switch {0x5c=>'¥',0x7c=>'¦',_=>LcdTextDecoder.Unicode(b) ?? '□'};
    /// <summary>紙の全体をテキスト（行は CRLF 区切り）にする。</summary>
    internal string Text()
    {
        var text=new StringBuilder();
        foreach(var line in lines){foreach(byte b in line)text.Append(ToChar(b));text.Append("\r\n");}
        return text.ToString();
    }
    // The whole sheet in dots, margins included.
    /// <summary>紙の全体の高さ（ドット、余白を含む）。</summary>
    internal int Height=>Margin*2+Math.Max(1,lines.Count)*LineHeight;
    /// <summary>紙の幅（ドット、余白を含む）。</summary>
    internal static readonly int Width=Margin*2+Columns*CellWidth;
}
