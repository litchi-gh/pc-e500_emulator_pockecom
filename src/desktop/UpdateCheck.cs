// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>新しい版のお知らせ（変更点、ダウンロードのページ、更新のしかた）。</summary>
internal sealed class UpdateDialog : ThemedForm
{
    /// <summary>「この版は通知しない」を押したか。</summary>
    internal bool Skip {get;private set;}
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="release">新しい版</param>
    /// <param name="installed">インストール版か（更新のしかたの案内が変わる）</param>
    internal UpdateDialog(Release release,bool installed=false)
    {
        Text=L.T("新しいバージョンのお知らせ","New version");FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterParent;Font=Fonts.Ui;ClientSize=new Size(620,480);
        var body=new Panel{Dock=DockStyle.Fill,Padding=new Padding(16,16,16,8)};
        var heading=new Label{Text=L.T($"新しいバージョン {release.Tag} が公開されています（今のバージョンは {UpdateCheck.Current}）。",$"Version {release.Tag} is out (this is {UpdateCheck.Current})."),Dock=DockStyle.Top,Height=30,Font=Fonts.UiBold};
        var card=new CardPanel{Title=L.T("変更点","Changes"),Dock=DockStyle.Fill};
        var notes=new TextBox{Text=release.Notes.Length>0?release.Notes:release.Name,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None};
        card.Controls.Add(notes);
        // The installed copy updates by running the new installer, the zip copy by unpacking over itself.
        string how=installed
            ?L.T("更新：エミュレータを終了して、ダウンロードしたインストーラー（-setup.exe）を実行します。ROM・RAM・設定はそのまま残ります。","To update: quit the emulator and run the downloaded installer (-setup.exe). ROMs, RAM and settings stay.")
            :L.T("更新：エミュレータを終了して、ダウンロードした zip を今の PC-E500.exe のフォルダに上書きで展開します。ROM・RAM・設定はそのまま残ります。","To update: quit the emulator and unpack the downloaded zip over the folder of PC-E500.exe. ROMs, RAM and settings stay.");
        var howLabel=new NoteLabel(how,588){Dock=DockStyle.Bottom,Padding=new Padding(0,8,0,0)};
        body.Controls.Add(card);body.Controls.Add(howLabel);body.Controls.Add(heading);
        var footer=new FooterBar();
        var close=footer.Add(L.T("閉じる","Close"),(_,_)=>Close(),DialogResult.Cancel);
        var skip=footer.Add(L.T("このバージョンは通知しない","Skip this version"));
        var open=footer.Add(L.T("ダウンロードのページを開く","Open the download page"));
        Controls.Add(body);Controls.Add(footer);
        open.Click+=(_,_)=>{try{UpdateCheck.Open(release.Url);Close();}catch(Exception ex){MessageBox.Show(ex.Message,"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Error);}};
        skip.Click+=(_,_)=>{Skip=true;Close();};
        AcceptButton=open;CancelButton=close;
        // The notes open unselected, with the download button ready.
        ActiveControl=open;Shown+=(_,_)=>notes.Select(0,0);
    }
}
