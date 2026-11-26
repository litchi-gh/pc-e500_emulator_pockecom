// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

internal static partial class Keyboard
{
    /// <summary>文字にならない PC のキーと、本体のキーの対応。</summary>
    internal static readonly Dictionary<Keys,string> Special=new() {
        [Keys.Enter]="ENTER",[Keys.Back]="BS",[Keys.Delete]="DEL",[Keys.Insert]="INS",
        [Keys.Left]="LEFT",[Keys.Right]="RIGHT",[Keys.Up]="UP",[Keys.Down]="DOWN",
        [Keys.Escape]="ON/BRK",[Keys.F1]="F1",[Keys.F2]="F2",[Keys.F3]="F3",[Keys.F4]="F4",[Keys.F5]="F5",
        [Keys.F6]="MENU",[Keys.F7]="BASIC",[Keys.F8]="2ndF",[Keys.ControlKey]="CTRL"
    };
}
