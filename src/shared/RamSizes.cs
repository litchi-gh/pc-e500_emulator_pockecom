// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>選べる RAM の容量。</summary>
internal static class RamSizes
{
    /// <summary>選べる RAM の容量（KB）。</summary>
    internal static readonly int[] Sizes=[8,16,32,64,128,256];
    // The stock E500 carries one 62256 (32KB) and the E650/U6000 two of them (64KB,
    // B0000-BFFFF). Replacing each 32KB SRAM with a 128KB 628128 is the well-known
    // expansion mod, so an E650/U6000 reaches 128KB or 256KB (80000-BFFFF) instead.
    /// <summary>機種（標準の内蔵 RAM の容量）に合う内蔵 RAM の容量の一覧。</summary>
    internal static int[] InternalSizes(int standardKB)=>standardKB==64?[64,128,256]:Sizes;
    /// <summary>機種に合わない内蔵 RAM の容量を、その機種の標準の容量（E500・E500S は 32KB、E650・U6000 は 64KB）にする。</summary>
    internal static int CoerceInternal(int standardKB,int kb)=>InternalSizes(standardKB).Contains(kb)?kb:standardKB;
}
