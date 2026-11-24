// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
namespace PokeCom;

// Modeless, so the trace has to follow the running machine instead of showing
// the snapshot that happened to exist when the window was opened.
/// <summary>CE-140F のトレース（コマンドとエラーの記録）を表示するウィンドウ。</summary>
internal sealed class DiskTraceDialog : ThemedForm
{
    readonly TextBox text=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=Fonts.Mono,BorderStyle=BorderStyle.None};
    readonly CheckBox live=new(){Text=L.T("自動更新","Auto refresh"),Checked=true,AutoSize=true};
    readonly System.Windows.Forms.Timer timer=new(){Interval=500};
    readonly Func<Core?> read;
    readonly Func<string> folder;
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="read">今のコアを返す関数</param>
    /// <param name="folder">CE-140F のフォルダを返す関数</param>
    /// <param name="guard">例外をメッセージで示しながら処理を実行する関数</param>
    internal DiskTraceDialog(Func<Core?> read,Func<string> folder,Action<Action> guard)
    {
        this.read=read;this.folder=folder;
        Text="CE-140F / X:";ClientSize=new Size(760,500);MinimumSize=new Size(460,300);
        StartPosition=FormStartPosition.CenterScreen;Font=Fonts.Ui;
        var body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(16,16,16,8)};
        var card=new CardPanel{Title=L.T("通信の記録","Exchange log"),Dock=DockStyle.Fill};
        card.Controls.Add(text);body.Controls.Add(card);
        var footer=new FooterBar();
        footer.Add(L.T("閉じる","Close"),(_,_)=>Close());
        footer.Add(L.T("更新","Refresh"),(_,_)=>Render());
        footer.Add(L.T("保存フォルダを開く","Open folder"),(_,_)=>guard(()=>{Directory.CreateDirectory(folder());Process.Start(new ProcessStartInfo(folder()){UseShellExecute=true});}));
        footer.AddLeft(live);
        Controls.Add(body);Controls.Add(footer);
        timer.Tick+=(_,_)=>{if(live.Checked)Render();};
        Shown+=(_,_)=>{Render();timer.Start();};
        FormClosed+=(_,_)=>timer.Stop();
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    /// <summary>トレースを読み直して表示する。</summary>
    internal void Render() {
        var core=read();
        LiveText.Update(text,L.T("保存先: ","Folder: ")+folder()+"\r\n\r\n"+(core is null?L.T("ROMが読み込まれていません。","No ROM is loaded."):core.DiskTrace().Replace("\n","\r\n")),follow:true);
    }
}
