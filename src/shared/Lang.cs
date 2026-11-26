// SPDX-License-Identifier: GPL-2.0-or-later
using System.Globalization;
namespace PokeCom;

// UI language. Every user-visible string is written at its point of use as
// L.T(japanese, english); the setting picks which one is shown. The default
// follows the Windows display language the first time the program runs.
/// <summary>表示言語（日本語・英語）の切り替えと、文字列の選択。</summary>
internal static class L
{
    /// <summary>英語で表示するか。</summary>
    internal static bool En {get;private set;}
    /// <summary>今の言語のコード（ja か en）と、選べる言語。</summary>
    internal static string Code=>En?"en":"ja";
    internal static readonly string[] Codes=["ja","en"];
    /// <summary>Windows の表示言語から決める既定の言語。</summary>
    internal static string Default=>CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="ja"?"ja":"en";
    /// <summary>言語を設定する（en なら英語、それ以外は日本語）。</summary>
    internal static void Set(string code)=>En=code=="en";
    /// <summary>今の言語の文字列を選ぶ。</summary>
    /// <param name="ja">日本語</param>
    /// <param name="en">英語</param>
    internal static string T(string ja,string en)=>En?en:ja;
}
