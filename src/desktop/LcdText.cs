// SPDX-License-Identifier: GPL-2.0-or-later
using System.Numerics;

namespace PokeCom;

// Decodes the 40 x 4 character layout by matching LCD cells against the ROM's 6 x 8 font.
internal sealed class LcdTextDecoder
{
    const ulong Mask48=(1UL<<48)-1;
    readonly Dictionary<ulong,char> exact=[];
    readonly List<(ulong Pattern,char Character)> candidates=[];
    internal bool FontFound {get;}

    internal LcdTextDecoder(byte[] rom)
    {
        byte[] digits=[
            0x3e,0x51,0x49,0x45,0x3e,0x00, 0x00,0x42,0x7f,0x40,0x00,0x00,
            0x42,0x61,0x51,0x49,0x46,0x00, 0x41,0x49,0x4d,0x4b,0x31,0x00
        ];
        int digitAt=rom.AsSpan().IndexOf(digits),fontAt=digitAt-(0x30-0x20)*6;
        if(digitAt<0 || fontAt<0 || fontAt+(0xe0-0x20)*6>rom.Length)return;
        for(int code=0x20;code<=0x7e;code++)Add(rom,fontAt,code,(char)code);
        for(int code=0xa1;code<=0xdf;code++)Add(rom,fontAt,code,(char)(0xff61+code-0xa1));
        FontFound=exact.Count>=95;
    }
    void Add(byte[] rom,int fontAt,int code,char character)
    {
        int at=fontAt+(code-0x20)*6;if(at<0||at+6>rom.Length)return;
        ulong pattern=0;for(int x=0;x<6;x++)pattern|=(ulong)rom[at+x]<<(x*8);
        if(exact.TryAdd(pattern,character))candidates.Add((pattern,character));
    }
    internal string Decode(byte[] pixels)
    {
        if(!FontFound)return "（このROMの文字フォントを検出できません）";
        string[] lines=new string[4];
        for(int row=0;row<4;row++) {
            char[] text=new char[40];
            for(int col=0;col<40;col++)text[col]=DecodeCell(pixels,row,col);
            lines[row]=new string(text).TrimEnd();
        }
        return string.Join(Environment.NewLine,lines).TrimEnd('\r','\n');
    }
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

internal sealed class ScreenTextDialog : Form
{
    readonly TextBox text=new(){Multiline=true,ReadOnly=true,AcceptsTab=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Consolas",14)};
    readonly Label result=new(){Dock=DockStyle.Bottom,Height=30,TextAlign=ContentAlignment.MiddleLeft};
    readonly Func<string> read;
    internal ScreenTextDialog(Func<string> read,Func<string,string> paste)
    {
        this.read=read;Text="LCD文字のコピー・貼り付け";ClientSize=new(760,330);MinimumSize=new Size(580,280);StartPosition=FormStartPosition.CenterParent;
        Font=new Font("Yu Gothic UI",10);
        var note=new Label{Dock=DockStyle.Top,Height=62,Text="LCDを40桁×4行の文字として読み取ります。範囲を選択してコピーできます。\n貼り付けはASCII文字と改行をPC-E500のキー入力として送ります。",Padding=new Padding(8,5,0,0)};
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=45,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(4)};
        var close=new Button{Text="閉じる",AutoSize=true,DialogResult=DialogResult.Cancel};
        var pasteButton=new Button{Text="クリップボードから貼り付け",AutoSize=true};
        var copy=new Button{Text="選択範囲をコピー",AutoSize=true};
        var all=new Button{Text="全選択",AutoSize=true};var refresh=new Button{Text="画面を更新",AutoSize=true};
        close.Click+=(_,_)=>Close();
        refresh.Click+=(_,_)=>RefreshText();
        all.Click+=(_,_)=>{text.SelectAll();text.Focus();};
        copy.Click+=(_,_)=>{try {
            string value=text.SelectionLength>0?text.SelectedText:text.Text;
            if(value.Length==0){result.Text="コピーする文字がありません。";return;}
            Clipboard.SetText(value,TextDataFormat.UnicodeText);result.Text=$"{value.Length}文字をコピーしました。";
        }catch(Exception ex){result.Text="コピーできません: "+ex.Message;}};
        pasteButton.Click+=(_,_)=>{try {
            if(!Clipboard.ContainsText(TextDataFormat.UnicodeText)){result.Text="クリップボードに文字列がありません。";return;}
            result.Text=paste(Clipboard.GetText(TextDataFormat.UnicodeText));
        }catch(Exception ex){result.Text="貼り付けできません: "+ex.Message;}};
        buttons.Controls.Add(close);buttons.Controls.Add(pasteButton);buttons.Controls.Add(copy);buttons.Controls.Add(all);buttons.Controls.Add(refresh);
        Controls.Add(text);Controls.Add(result);Controls.Add(buttons);Controls.Add(note);CancelButton=close;
        Shown+=(_,_)=>RefreshText();
    }
    void RefreshText(){int start=text.SelectionStart,length=text.SelectionLength;text.Text=read();text.Select(Math.Min(start,text.TextLength),Math.Min(length,Math.Max(0,text.TextLength-start)));result.Text="画面を読み取りました。";}
}
