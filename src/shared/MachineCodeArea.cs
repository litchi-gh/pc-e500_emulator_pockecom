// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// The machine-code area is the user's part of RAM below the system, from the
// address kept at BFD1Ah up to BFBFFh; empty after a reset (BFD1Ah = BFC00h).
// The ROM sizes it with IOCS device 8 command 42h, which the public entry
// FFFD8h runs for BASIC with the pointer's address at BFE03h and the size at
// BFE06h, moving the work areas and stacks below it, and then goes to the main
// menu (Analysis/ASSEMBLER_MANUAL.md section 3.2). The size given is the size
// the area ends up (asked twice for 1000h, it stays 1000h; 0 gives it back), and
// the BASIC program and variables stay.
// 機械語エリアは BFD1Ah の番地から BFBFFh まで（リセット直後は BFC00h で大きさ 0）。ROM の IOCS（装置 8、命令 42h）で大きさを決める。
// 公開入口 FFFD8h が BFE03h のポインタの番地と BFE06h の大きさで実行し、作業域とスタックを下へずらしてメインメニューへ戻る。
// 指定するのは確保後の大きさ（1000h を2回なら 1000h のまま、0 で返す）。BASIC のプログラムと変数は残る。
/// <summary>機械語エリアの大きさを読む・決める。</summary>
internal static class MachineCodeArea
{
    const int Pointer=0xbfd1a,Top=0xbfc00;
    /// <summary>今の機械語エリア（大きさが分からなければ null。RAM が初期化されていないときなど）。</summary>
    internal static (int Start,int Size)? Read(Core core) {
        var b=core.Dump(Pointer,3);int start=b[0]|b[1]<<8|b[2]<<16;
        int bottom=0xc0000-core.InternalKB*1024;
        if(start<bottom || start>Top)return null;
        return (start,Top-start);
    }
    // POKE and CALL in decimal: every character of it is on the machine's keys.
    /// <summary>機械語エリアを size バイトにする BASIC の命令（ROM の確保の処理を呼ぶ）。</summary>
    internal static string Command(int size)=>$"POKE {0xbfe03},{Pointer&0xff},{(Pointer>>8)&0xff},{Pointer>>16},{size&0xff},{(size>>8)&0xff},{size>>16}:CALL {0xfffd8}";
}
