// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
namespace PokeCom;

// Where the program and its data live. The zip keeps everything beside the
// executable, as it always has. An installed copy - the installer leaves a
// marker file beside the executable - may sit in Program Files, where a user
// cannot write, so its data lives per user in %LocalAppData%\PockeComE500,
// the same whether it was installed for one user or for all.
/// <summary>
/// プログラムとデータのフォルダ。zip 版はすべて exe の横、インストール版（exe の横に目印のファイルがある）は
/// データを利用者ごとの %LocalAppData%\PockeComE500 に置く（Program Files には書き込めないため）。
/// </summary>
internal static class AppFolders
{
    /// <summary>インストーラーが exe の横に置く目印のファイルと、インストール版のデータのフォルダ名。</summary>
    internal const string Marker="PockeComE500.installed",Name="PockeComE500";
    /// <summary>データのフォルダの中の、利用者が使うフォルダ。</summary>
    internal static readonly string[] Folders=["rom","ram","drive-x","state"];
    /// <summary>インストール版か（exe のフォルダに目印のファイルがあるか）。</summary>
    internal static bool Installed=>IsInstalled(AppContext.BaseDirectory);
    /// <summary>フォルダに目印のファイルがあるか。</summary>
    internal static bool IsInstalled(string programFolder)=>File.Exists(Path.Combine(programFolder,Marker));
    // A development build runs from src/desktop/bin, so it walks up to the
    // folder that holds rom/ - or the app/ folder that does.
    /// <summary>プログラムのフォルダ（manual がある）。インストール版は exe のフォルダ、それ以外は rom フォルダを持つフォルダか app フォルダ。</summary>
    internal static string Program() {
        string here=AppContext.BaseDirectory;
        // A release (the zip, or the app folder) has its rom or manual folder
        // beside the executable. Only a development build looks further up: a
        // zip copy walking up picked any rom folder above it for its data.
        // 配布物（zip、app フォルダ）は exe の横に rom か manual がある。上へ探しに行くのは開発中のビルドだけ
        // （zip 版が上へ探すと、関係のない上のフォルダの rom をデータの場所にしてしまった）。
        if(Installed || Directory.Exists(Path.Combine(here,"rom")) || Directory.Exists(Path.Combine(here,"manual")))return here;
        char s=Path.DirectorySeparatorChar;
        if(!here.Contains($"{s}src{s}desktop{s}bin{s}",StringComparison.OrdinalIgnoreCase))return here;
        for(var d=new DirectoryInfo(here);d is not null;d=d.Parent) {
            if(Directory.Exists(Path.Combine(d.FullName,"rom")))return d.FullName;
            if(Directory.Exists(Path.Combine(d.FullName,"app","rom")))return Path.Combine(d.FullName,"app");
        }
        return AppContext.BaseDirectory;
    }
    /// <summary>データのフォルダ（rom・ram・drive-x・state を持つ）。</summary>
    /// <param name="programFolder">プログラムのフォルダ</param>
    /// <param name="localAppData">%LocalAppData%（テストでは別の場所を渡す）</param>
    internal static string Data(string programFolder,string? localAppData=null)=>IsInstalled(programFolder)
        ?Path.Combine(localAppData??Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Name):programFolder;
    // An installed copy starts with an empty data folder: the folders are made
    // at once, so that the rom folder is there to put a ROM in.
    /// <summary>データのフォルダと、その中の rom・ram・drive-x・state を作る（なければ）。</summary>
    /// <returns>データのフォルダ</returns>
    internal static string Prepare(string data) {
        foreach(var folder in Folders)Directory.CreateDirectory(Path.Combine(data,folder));
        return data;
    }
    // For the Start menu's "data folder" shortcut and the installer's last page:
    // the folder of whoever runs it, even when installed for all users.
    /// <summary>データのフォルダ（または、その中の rom などのフォルダ）をエクスプローラーで開く。</summary>
    /// <param name="sub">開く中のフォルダ（空ならデータのフォルダ）</param>
    internal static void Open(string sub="") {
        string data=Prepare(Data(Program()));
        string folder=Folders.Contains(sub)?Path.Combine(data,sub):data;
        Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});
    }
}
