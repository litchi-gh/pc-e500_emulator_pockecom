// SPDX-License-Identifier: GPL-2.0-or-later
using System.Numerics;

namespace PokeCom;

// Decodes the 40 x 4 character layout by matching LCD cells against the ROM's 6 x 8 font.
/// <summary>
/// LCD のドットを、ROM の文字フォントと照らし合わせて文字に読み取る。
/// ROM の中からフォントを探し（数字 0〜3 の形を目印にする）、20h〜7Eh と A1h〜DFh の文字を候補にする。
/// </summary>
internal sealed class LcdTextDecoder
{
    const ulong Mask48=(1UL<<48)-1;
    readonly Dictionary<ulong,char> exact=[];
    readonly List<(ulong Pattern,char Character)> candidates=[];
    /// <summary>ROM の中にフォントが見つかったか。</summary>
    internal bool FontFound {get;}
    // The ROM's 6 x 8 font from 20h, six column bytes per code: up to FFh, or DFh
    // when the ROM ends sooner; empty if not found. The CE-126P paper prints with it.
    /// <summary>ROM のフォント（20h から、1文字6バイト、各バイトが縦8ドット）。見つからなければ空。</summary>
    internal byte[] Font {get;} = [];

    /// <summary>ROM からフォントを探し、読み取りの候補を作る。</summary>
    /// <param name="rom">ROM イメージ</param>
    internal LcdTextDecoder(byte[] rom)
    {
        int fontAt=FindFont(rom);
        if(fontAt<0)return;
        Font=rom.AsSpan(fontAt,Math.Min(rom.Length-fontAt,(0x100-0x20)*6)).ToArray();
        // Screen text keeps to letters, digits, signs and kana. The symbols at
        // E8h-F4h are left out: they are near enough to graphics cells that a
        // nearest-glyph match would misread those.
        for(int code=0x20;code<=0xdf;code++)if(code!=0xa0 && Unicode((byte)code) is char c)Add(rom,fontAt,code,c);
        FontFound=exact.Count>=95;
    }
    // The PC-E500 character set in Unicode, for the screen text and the printer
    // paper alike; null for codes without a counterpart (graphics, controls).
    /// <summary>PC-E500 の文字コードを Unicode の文字にする。</summary>
    /// <param name="code">文字コード</param>
    /// <returns>対応する文字（なければ null）</returns>
    internal static char? Unicode(byte code)=>code switch {
        >=0x20 and <=0x7e=>(char)code,
        0xa0=>' ',
        >=0xa1 and <=0xdf=>(char)(0xff61+code-0xa1),
        0xe8=>'♠',0xe9=>'♥',0xea=>'♦',0xeb=>'♣',
        0xf1=>'円',0xf2=>'年',0xf3=>'月',0xf4=>'日',
        _=>null
    };
    // Where the font for 20h-DFh starts in the ROM image, found by the glyphs of 0-3; -1 if absent.
    /// <summary>ROM の中のフォントの位置を探す。</summary>
    /// <returns>20h の位置（見つからなければ -1）</returns>
    static int FindFont(byte[] rom)
    {
        byte[] digits=[
            0x3e,0x51,0x49,0x45,0x3e,0x00, 0x00,0x42,0x7f,0x40,0x00,0x00,
            0x42,0x61,0x51,0x49,0x46,0x00, 0x41,0x49,0x4d,0x4b,0x31,0x00
        ];
        int digitAt=rom.AsSpan().IndexOf(digits),fontAt=digitAt-(0x30-0x20)*6;
        return digitAt<0 || fontAt<0 || fontAt+(0xe0-0x20)*6>rom.Length?-1:fontAt;
    }
    /// <summary>文字コード code の形を、読み取りの候補に加える。</summary>
    void Add(byte[] rom,int fontAt,int code,char character)
    {
        int at=fontAt+(code-0x20)*6;if(at<0||at+6>rom.Length)return;
        ulong pattern=0;for(int x=0;x<6;x++)pattern|=(ulong)rom[at+x]<<(x*8);
        if(exact.TryAdd(pattern,character))candidates.Add((pattern,character));
    }
    /// <summary>LCD のドットを 40桁×4行の文字列に読み取る。</summary>
    /// <param name="pixels">240×32 のドット</param>
    internal string Decode(byte[] pixels)
    {
        if(!FontFound)return L.T("（このROMの文字フォントを検出できません）","(The character font of this ROM was not found)");
        string[] lines=new string[4];
        for(int row=0;row<4;row++) {
            char[] text=new char[40];
            for(int col=0;col<40;col++)text[col]=DecodeCell(pixels,row,col);
            lines[row]=new string(text).TrimEnd();
        }
        return string.Join(Environment.NewLine,lines).TrimEnd('\r','\n');
    }
    /// <summary>1文字分（6×8ドット）を読み取る。完全に一致（反転も可）しなければ、差が2ドット以内の最も近い文字。なければ □。</summary>
    char DecodeCell(byte[] pixels,int row,int col)
    {
        ulong pattern=0;
        for(int x=0;x<6;x++) {
            byte bits=0;for(int y=0;y<8;y++)if(pixels[(row*8+y)*240+col*6+x]!=0)bits|=(byte)(1<<y);
            pattern|=(ulong)bits<<(x*8);
        }
        if(exact.TryGetValue(pattern,out char value))return value;
        ulong inverse=(~pattern)&Mask48;if(exact.TryGetValue(inverse,out value))return value;
        int best=49;char nearest='□';
        foreach(var glyph in candidates) {
            int distance=Math.Min(BitOperations.PopCount(pattern^glyph.Pattern),BitOperations.PopCount(inverse^glyph.Pattern));
            if(distance<best){best=distance;nearest=glyph.Character;}
        }
        return best<=2?nearest:'□';
    }
}

/// <summary>LCD 文字のコピー・貼り付けのウィンドウ。</summary>
internal sealed class ScreenTextDialog : Form
{
    readonly TextBox text=new(){Multiline=true,ReadOnly=true,AcceptsTab=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=Fonts.MonoLarge};
    readonly Label result=new(){Dock=DockStyle.Bottom,Height=30,TextAlign=ContentAlignment.MiddleLeft};
    readonly Func<string> read;
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="read">LCD を文字列で返す関数</param>
    /// <param name="paste">文字列をキー入力として送り、結果のメッセージを返す関数</param>
    internal ScreenTextDialog(Func<string> read,Func<string,string> paste)
    {
        this.read=read;Text=L.T("LCD文字のコピー・貼り付け","LCD text copy and paste");ClientSize=new(760,330);MinimumSize=new Size(580,280);StartPosition=FormStartPosition.CenterParent;
        Font=Fonts.Ui;
        var note=new Label{Dock=DockStyle.Top,Height=62,Text=L.T("LCDを40桁×4行の文字として読み取ります。範囲を選択してコピーできます。\n貼り付けはASCII文字と改行をPC-E500のキー入力として送ります。","Reads the LCD as 40 columns by 4 rows of text. Select a range to copy it.\nPaste sends ASCII characters and line breaks as PC-E500 key input."),Padding=new Padding(8,5,0,0)};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=45,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(4)};
        var close=new Button{Text=L.T("閉じる","Close"),AutoSize=true,DialogResult=DialogResult.Cancel};
        var pasteButton=new Button{Text=L.T("クリップボードから貼り付け","Paste from clipboard"),AutoSize=true};
        var copy=new Button{Text=L.T("選択範囲をコピー","Copy selection"),AutoSize=true};
        var all=new Button{Text=L.T("全選択","Select all"),AutoSize=true};var refresh=new Button{Text=L.T("画面を更新","Refresh screen"),AutoSize=true};
        close.Click+=(_,_)=>Close();
        refresh.Click+=(_,_)=>RefreshText();
        all.Click+=(_,_)=>{text.SelectAll();text.Focus();};
        copy.Click+=(_,_)=>{try {
            string value=text.SelectionLength>0?text.SelectedText:text.Text;
            if(value.Length==0){result.Text=L.T("コピーする文字がありません。","Nothing to copy.");return;}
            Clipboard.SetText(value,TextDataFormat.UnicodeText);result.Text=L.T($"{value.Length}文字をコピーしました。",$"Copied {value.Length} characters.");
        }catch(Exception ex){result.Text=L.T("コピーできません: ","Could not copy: ")+ex.Message;}};
        pasteButton.Click+=(_,_)=>{try {
            if(!Clipboard.ContainsText(TextDataFormat.UnicodeText)){result.Text=L.T("クリップボードに文字列がありません。","The clipboard has no text.");return;}
            result.Text=paste(Clipboard.GetText(TextDataFormat.UnicodeText));
        }catch(Exception ex){result.Text=L.T("貼り付けできません: ","Could not paste: ")+ex.Message;}};
        buttons.Controls.Add(close);buttons.Controls.Add(pasteButton);buttons.Controls.Add(copy);buttons.Controls.Add(all);buttons.Controls.Add(refresh);
        Controls.Add(text);Controls.Add(result);Controls.Add(buttons);Controls.Add(note);CancelButton=close;
        Shown+=(_,_)=>RefreshText();
    }
    /// <summary>LCD を読み直す（選択範囲はできるだけ保つ）。</summary>
    void RefreshText(){int start=text.SelectionStart,length=text.SelectionLength;text.Text=read();text.Select(Math.Min(start,text.TextLength),Math.Min(length,Math.Max(0,text.TextLength-start)));result.Text=L.T("画面を読み取りました。","Screen read.");}
}
