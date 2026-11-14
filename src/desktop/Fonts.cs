// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// The fonts every window uses, made once for the life of the program. Each
// window used to make its own each time it opened and never disposed of them,
// so the GDI font handles grew with every settings or display window opened.
// A control does not own a font it is given, so sharing them is safe.
/// <summary>各ウィンドウが使うフォント（プログラムの間ずっと1つずつ。開くたびに作って解放しないのをやめた）。</summary>
internal static class Fonts
{
    /// <summary>ふつうの文字、その太字、小さい文字（ステータスバーなど）。</summary>
    internal static readonly Font Ui=new("Yu Gothic UI",10),UiBold=new(Ui,FontStyle.Bold),Small=new("Yu Gothic UI",9);
    /// <summary>等幅の文字（番地・16進・トレース）と、その大きいもの（画面の文字）。</summary>
    internal static readonly Font Mono=new("Consolas",10),MonoLarge=new("Consolas",14);
}
