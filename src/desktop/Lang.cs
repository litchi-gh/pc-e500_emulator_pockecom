// SPDX-License-Identifier: GPL-2.0-or-later
using System.Globalization;
namespace PokeCom;

// UI language. Every user-visible string is written at its point of use as
// L.T(japanese, english); the setting picks which one is shown. The default
// follows the Windows display language the first time the program runs.
internal static class L
{
    internal static bool En {get;private set;}
    internal static string Code=>En?"en":"ja";
    internal static readonly string[] Codes=["ja","en"];
    internal static string Default=>CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="ja"?"ja":"en";
    internal static void Set(string code)=>En=code=="en";
    internal static string T(string ja,string en)=>En?en:ja;
}
