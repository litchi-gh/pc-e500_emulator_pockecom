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
