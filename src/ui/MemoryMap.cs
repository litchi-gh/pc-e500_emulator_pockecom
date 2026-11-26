// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>メモリの領域の種類（色分けと、書き換えできるかの判断に使う）。</summary>
internal enum MemoryKind {Unmapped,Ram,Rom}

// What the bus has at an address, as the debugger colours and names it.
/// <summary>アドレスがどの領域か（ROM・内蔵 RAM・カード・未使用）を答える。</summary>
internal static class MemoryMap
{
    /// <summary>アドレスの領域の種類。</summary>
    internal static MemoryKind Kind(Core core,int a) {
        if(a>=0xc0000)return MemoryKind.Rom;
        if(a>=0x80000)return MemoryKind.Ram;
        if(a>=0x40000)return core.CardKB>0?MemoryKind.Ram:MemoryKind.Unmapped;
        if(a>=0x20000)return core.IsE650Family?MemoryKind.Rom:MemoryKind.Unmapped;
        return MemoryKind.Unmapped;
    }
    /// <summary>アドレスの領域の名前（表示用）。</summary>
    internal static string Name(Core core,int a) {
        if(a>=0xc0000)return L.T("内蔵ROM (CE2)","Internal ROM (CE2)");
        if(a>=0xc0000-core.InternalKB*1024)return L.T($"内蔵RAM {core.InternalKB}KB",$"Internal RAM {core.InternalKB}KB");
        if(a>=0x80000)return L.T($"内蔵RAM {core.InternalKB}KBのミラー",$"Mirror of internal RAM {core.InternalKB}KB");
        if(a>=0x40000)return core.CardKB>0?L.T($"RAMカード {core.CardKB}KB とそのミラー",$"RAM card {core.CardKB}KB and its mirrors"):L.T("カードスロット (未装着)","Card slot (empty)");
        if(a>=0x20000)return core.IsE650Family?L.T("拡張ROM (CE3)","Extension ROM (CE3)"):L.T("CE3 (未使用)","CE3 (unused)");
        if(a>=0x10000)return L.T("CE6 (未使用)","CE6 (unused)");
        if(a>=0x2000&&a<0x4000)return L.T("LCDコントローラの窓 (表示に影響しない生の値)","LCD controller window (raw bus, display unaffected)");
        return L.T("内部エリア","Internal area");
    }
}
