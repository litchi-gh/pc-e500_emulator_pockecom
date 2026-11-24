// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>LCD 文字のコピー・貼り付けのウィンドウ。</summary>
internal sealed class ScreenTextDialog : ThemedForm
{
    readonly TextBox text=new(){Multiline=true,ReadOnly=true,AcceptsTab=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=Fonts.MonoLarge,BorderStyle=BorderStyle.None};
    readonly Label result=new(){AutoSize=true,Font=Fonts.Small,ForeColor=Theme.SubText};
    readonly Func<string> read;
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="read">LCD を文字列で返す関数</param>
    /// <param name="paste">文字列をキー入力として送り、結果のメッセージを返す関数</param>
    internal ScreenTextDialog(Func<string> read,Func<string,string> paste)
    {
        this.read=read;Text=L.T("LCD文字のコピー・貼り付け","LCD text copy and paste");ClientSize=new(760,400);MinimumSize=new Size(600,320);StartPosition=FormStartPosition.CenterParent;Load+=(_,_)=>LockWidth();
        Font=Fonts.Ui;
        var body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(16,12,16,8)};
        var note=new NoteLabel(L.T("LCD を 40 桁 × 4 行の文字で読み取ります。貼り付けは ASCII と改行をキー入力として送ります。","Reads the LCD as 40 × 4 characters. Paste types ASCII and line breaks as keys."),720){Dock=DockStyle.Top};
        var card=new CardPanel{Title=L.T("LCD の文字","LCD text"),Dock=DockStyle.Fill};
        var tools=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=false,BackColor=Theme.Card,Padding=new Padding(0,0,0,8)};
        FluentButton Tool(string label,Action click){var t=new FluentButton{Text=label,AutoSize=true,Margin=new Padding(0,0,6,0)};t.Click+=(_,_)=>click();tools.Controls.Add(t);return t;}
        Tool(L.T("画面を更新","Refresh"),RefreshText);
        Tool(L.T("全選択","Select all"),()=>{text.SelectAll();text.Focus();});
        Tool(L.T("選択範囲をコピー","Copy selection"),()=>{try {
            string value=text.SelectionLength>0?text.SelectedText:text.Text;
            if(value.Length==0){result.Text=L.T("コピーする文字がありません。","Nothing to copy.");return;}
            Clipboard.SetText(value,TextDataFormat.UnicodeText);result.Text=L.T($"{value.Length}文字をコピーしました。",$"Copied {value.Length} characters.");
        }catch(Exception ex){result.Text=L.T("コピーできません: ","Could not copy: ")+ex.Message;}});
        Tool(L.T("クリップボードから貼り付け","Paste from clipboard"),()=>{try {
            if(!Clipboard.ContainsText(TextDataFormat.UnicodeText)){result.Text=L.T("クリップボードに文字列がありません。","The clipboard has no text.");return;}
            result.Text=paste(Clipboard.GetText(TextDataFormat.UnicodeText));
        }catch(Exception ex){result.Text=L.T("貼り付けできません: ","Could not paste: ")+ex.Message;}});
        card.Controls.Add(text);card.Controls.Add(tools);
        body.Controls.Add(card);body.Controls.Add(note);
        var footer=new FooterBar();
        var close=footer.Add(L.T("閉じる","Close"),(_,_)=>Close());
        footer.AddLeft(result);
        Controls.Add(body);Controls.Add(footer);CancelButton=close;
        Shown+=(_,_)=>RefreshText();
    }
    /// <summary>LCD を読み直す（選択範囲はできるだけ保つ）。</summary>
    void RefreshText(){int start=text.SelectionStart,length=text.SelectionLength;text.Text=read();text.Select(Math.Min(start,text.TextLength),Math.Min(length,Math.Max(0,text.TextLength-start)));result.Text=L.T("画面を読み取りました。","Screen read.");}
}
