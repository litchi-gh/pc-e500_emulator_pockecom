// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
namespace PokeCom;

/// <summary>公開されている版（GitHub のリリース）。</summary>
/// <param name="Version">版（v を除いた番号）</param>
/// <param name="Tag">リリースのタグ（v0.0.4 など）</param>
/// <param name="Name">リリースの名前</param>
/// <param name="Url">リリースのページ</param>
/// <param name="Notes">変更点（リリースの本文）</param>
internal sealed record Release(Version Version,string Tag,string Name,string Url,string Notes);

// Notification only: the emulator asks GitHub which release is the latest and
// says so when it is newer, and the user downloads and unpacks it. Nothing is
// downloaded or replaced here. The answer comes from the public API without an
// account; only the release page of this repository is ever opened.
/// <summary>新しい版の確認（GitHub の最新リリースを読むだけ。ダウンロードや置き換えはしない）。</summary>
internal static class UpdateCheck
{
    /// <summary>リリースの一覧のページと、最新リリースを返す API。</summary>
    internal const string Releases="https://github.com/litchi-gh/pc-e500_emulator_pockecom/releases";
    const string Latest="https://api.github.com/repos/litchi-gh/pc-e500_emulator_pockecom/releases/latest";
    /// <summary>今動いている版（PcE500.csproj の Version）。</summary>
    internal static Version Current=>Normalize(typeof(UpdateCheck).Assembly.GetName().Version??new Version(0,0,0));
    /// <summary>比べられるよう、版を 3 つの数（major.minor.build）にそろえる。</summary>
    static Version Normalize(Version v)=>new(v.Major,v.Minor,Math.Max(0,v.Build));
    /// <summary>API の答え（JSON）を読む。下書き・プレリリース・版として読めないタグなら null。</summary>
    /// <param name="json">releases/latest の答え</param>
    internal static Release? Parse(string json) {
        using var document=JsonDocument.Parse(json);var r=document.RootElement;
        if(r.ValueKind!=JsonValueKind.Object)return null;
        bool Flag(string name)=>r.TryGetProperty(name,out var v) && v.ValueKind==JsonValueKind.True;
        string Text(string name)=>r.TryGetProperty(name,out var v) && v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
        if(Flag("draft") || Flag("prerelease"))return null;
        string tag=Text("tag_name");
        if(!Version.TryParse(tag.TrimStart('v','V'),out var version))return null;
        // Only this repository's own pages are opened, whatever the answer says.
        string url=Text("html_url");
        // Compared once the address is normalised: a prefix test let
        // ".../releases/../../../other/repo" through to another repository's page.
        // 正規化したアドレスで比べる（前方一致だけでは "../" で別のリポジトリへ行けた）。
        url=Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.Scheme==Uri.UriSchemeHttps && uri.Host=="github.com" &&
            uri.AbsolutePath.StartsWith(new Uri(Releases).AbsolutePath+"/",StringComparison.Ordinal)?uri.AbsoluteUri:Releases;
        return new(Normalize(version),tag,Text("name"),url,Text("body").Replace("\r\n","\n").Replace("\n","\r\n"));
    }
    /// <summary>今の版より新しいか。</summary>
    internal static bool IsNewer(Release release)=>release.Version>Current;
    /// <summary>最新リリースを問い合わせる（10 秒まで）。</summary>
    /// <exception cref="HttpRequestException">つながらない、または答えがおかしい</exception>
    internal static async Task<Release?> FetchAsync() {
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(10)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PockeCom-PC-E500/"+Current);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return Parse(await http.GetStringAsync(Latest));
    }
    /// <summary>リリースのページをブラウザで開く。</summary>
    internal static void Open(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
}

/// <summary>新しい版のお知らせ（変更点、ダウンロードのページ、更新のしかた）。</summary>
internal sealed class UpdateDialog : Form
{
    /// <summary>「この版は通知しない」を押したか。</summary>
    internal bool Skip {get;private set;}
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="release">新しい版</param>
    /// <param name="installed">インストール版か（更新のしかたの案内が変わる）</param>
    internal UpdateDialog(Release release,bool installed=false)
    {
        Text=L.T("新しい版のお知らせ","New version");FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterParent;Font=Fonts.Ui;ClientSize=new Size(620,470);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(14)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
        layout.Controls.Add(new Label{Text=L.T($"新しい版 {release.Tag} が公開されています（今の版は {UpdateCheck.Current}）。",$"Version {release.Tag} is out (this is {UpdateCheck.Current})."),AutoSize=true,Font=Fonts.UiBold,Margin=new Padding(0,0,0,8)},0,0);
        var notes=new TextBox{Text=release.Notes.Length>0?release.Notes:release.Name,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BackColor=Color.White};
        layout.Controls.Add(notes,0,1);
        // The installed copy updates by running the new installer, the zip copy by unpacking over itself.
        string how=installed
            ?L.T("更新のしかた：エミュレータを終了し、ダウンロードしたインストーラー（-setup.exe）を実行してください。ROM・RAM・設定はデータフォルダ（%LocalAppData%\\PockeComE500）にあり、そのまま残ります。","To update: quit the emulator and run the downloaded installer (-setup.exe). ROMs, RAM and settings are in the data folder (%LocalAppData%\\PockeComE500) and stay as they are.")
            :L.T("更新のしかた：エミュレータを終了し、ダウンロードした zip の中身を、今の PC-E500.exe があるフォルダに上書きで展開してください。rom・ram・drive-x・state フォルダ（ROM・RAM・設定）は zip に入っていないので、そのまま残ります。","To update: quit the emulator and unpack the downloaded zip over the folder that holds PC-E500.exe now. The rom, ram, drive-x and state folders (ROMs, RAM and settings) are not in the zip and stay as they are.");
        layout.Controls.Add(new Label{Text=how,AutoSize=true,MaximumSize=new Size(590,0),Margin=new Padding(0,10,0,6)},0,2);
        var buttons=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        var close=new Button{Text=L.T("閉じる","Close"),AutoSize=true,DialogResult=DialogResult.Cancel};
        var skip=new Button{Text=L.T("この版は通知しない","Skip this version"),AutoSize=true};
        var open=new Button{Text=L.T("ダウンロードのページを開く","Open the download page"),AutoSize=true};
        buttons.Controls.Add(close);buttons.Controls.Add(skip);buttons.Controls.Add(open);layout.Controls.Add(buttons,0,3);
        open.Click+=(_,_)=>{try{UpdateCheck.Open(release.Url);Close();}catch(Exception ex){MessageBox.Show(ex.Message,"PC-E500",MessageBoxButtons.OK,MessageBoxIcon.Error);}};
        skip.Click+=(_,_)=>{Skip=true;Close();};
        AcceptButton=open;CancelButton=close;
        // The notes open unselected, with the download button ready.
        ActiveControl=open;Shown+=(_,_)=>notes.Select(0,0);
    }
}
