// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia.Styling;
namespace PokeCom;

/// <summary>
/// The desktop's light or dark, read before the first window is built.
/// On Linux, Avalonia learns it from the desktop portal only some time after
/// start-up: until then it says Light, and the window's own colours, chosen
/// once, stayed light while the theme's text turned dark-mode white.
/// デスクトップのライト・ダークを、最初のウィンドウを作る前に読む。Linux では
/// Avalonia がポータルから知るのが起動の少しあとになり、それまではライトとして
/// 扱われる。そのため一度だけ決めるウィンドウの色はライトのまま、テーマの文字
/// だけがダークの白になっていた。
/// </summary>
internal static class SystemTheme
{
    /// <summary>Dark or Light; Default (Avalonia's own guess) when nothing tells.</summary>
    internal static ThemeVariant Read() {
        if(!OperatingSystem.IsLinux())return ThemeVariant.Default;
        // The portal, as Avalonia asks it, so every desktop that has one answers: 1 dark, 2 light, 0 no preference.
        // ポータル（Avalonia と同じ問い合わせ）：1 がダーク、2 がライト、0 は指定なし。
        string? portal=Run("gdbus","call","--session","--dest","org.freedesktop.portal.Desktop","--object-path","/org/freedesktop/portal/desktop",
            "--method","org.freedesktop.portal.Settings.ReadOne","org.freedesktop.appearance","color-scheme");
        if(portal is not null && Regex.Match(portal,@"uint32 (\d)") is {Success:true} m) {
            if(m.Groups[1].Value=="1")return ThemeVariant.Dark;
            if(m.Groups[1].Value=="2")return ThemeVariant.Light;
        }
        // GNOME's own setting, for a desktop without the portal.
        string? scheme=Run("gsettings","get","org.gnome.desktop.interface","color-scheme");
        if(scheme is not null) {
            if(scheme.Contains("dark",StringComparison.OrdinalIgnoreCase))return ThemeVariant.Dark;
            string? gtk=Run("gsettings","get","org.gnome.desktop.interface","gtk-theme");
            return gtk is not null && gtk.Contains("dark",StringComparison.OrdinalIgnoreCase)?ThemeVariant.Dark:ThemeVariant.Light;
        }
        return portal is not null?ThemeVariant.Light:ThemeVariant.Default;
    }

    /// <summary>A command's output, or null when it is missing, fails or takes over a second.</summary>
    static string? Run(string file,params string[] args) {
        try {
            var info=new ProcessStartInfo(file){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var a in args)info.ArgumentList.Add(a);
            using var p=Process.Start(info);
            if(p is null)return null;
            var output=p.StandardOutput.ReadToEndAsync();
            _=p.StandardError.ReadToEndAsync();
            if(!p.WaitForExit(1000)){try{p.Kill();}catch(Exception){}return null;}
            return p.ExitCode==0?output.Result:null;
        }
        catch(Exception){return null;}
    }
}
