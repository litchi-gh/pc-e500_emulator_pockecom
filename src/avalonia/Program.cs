// SPDX-License-Identifier: GPL-2.0-or-later
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
namespace PokeCom;

/// <summary>Avalonia 版（Ubuntu 向け。Windows でも動く）の入口。</summary>
internal static class Program
{
    /// <summary>起動の引数（ROM、--shot ファイル、--script ファイル）。</summary>
    internal static string[] Args=[];
    [STAThread]
    static int Main(string[] args) {
        Args=args;
        return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
    }
}

/// <summary>アプリケーション：テーマを決め、メインウィンドウを開く。</summary>
internal sealed class App : Application
{
    public override void Initialize() {
        Styles.Add(new FluentTheme());
        // Menus and dialogs too: their own default has no Japanese face on Ubuntu.
        Resources["ContentControlThemeFontFamily"]=Paint.Ui;
        RequestedThemeVariant=MainWindow.SavedTheme() switch{"Light"=>ThemeVariant.Light,"Dark"=>ThemeVariant.Dark,_=>SystemTheme.Read()};
    }
    public override void OnFrameworkInitializationCompleted() {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.MainWindow=new MainWindow(Program.Args);
        base.OnFrameworkInitializationCompleted();
    }
}
