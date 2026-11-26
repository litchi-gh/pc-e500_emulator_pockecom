// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>メモリの区画の種類（色分けに使う）。</summary>
internal enum RegionKind {Header,Variables,Program,Block,RamDisk,Free,Stack,Work,MachineCode,System}

/// <summary>メモリの区画1つ：先頭、終わり（含まない）、種類、名前。</summary>
internal sealed record Region(int Start,int End,RegionKind Kind,string Name)
{
    internal int Size=>End-Start;
}

// How a RAM slot is laid out, read from the machine: the slot header, the chain
// of named blocks with the free space one of them holds, and for the internal
// RAM the work areas, stacks and machine-code area the pointer table at BFCDEh
// gives, up to the system's own bytes from BFC00h (Analysis/MEMORY.md §2-§4).
// RAM のスロットの並びを本体から読む：スロットの見出し、名前付きのブロックの並びと空き、内蔵 RAM ではさらに
// BFCDEh からのポインタ表が示す作業域・スタック・機械語エリアと、BFC00h からのシステムの番地。
/// <summary>RAM の区画の並びを読む。</summary>
internal static partial class RamLayout
{
    const int TableAt=0xbfcde,TableCount=21,SystemAt=0xbfc00;
    static int Le3(ReadOnlySpan<byte> b)=>b[0]|b[1]<<8|b[2]<<16;
    /// <summary>内蔵 RAM の並び（読めない所は「不明」の1区画にする）。</summary>
    internal static List<Region> Internal(Core core) {
        int bottom=0xc0000-core.InternalKB*1024;
        var regions=new List<Region>();
        int chainEnd=Chain(core,bottom,SystemAt,regions);
        var table=core.Dump(TableAt,TableCount*3);
        var p=Enumerable.Range(0,TableCount).Select(i=>Le3(table.AsSpan(i*3))).ToArray();
        bool ordered=p[0]>=chainEnd && p[^1]<=SystemAt && p.Zip(p.Skip(1)).All(x=>x.First<=x.Second);
        if(!ordered){regions.Add(new(chainEnd,SystemAt,RegionKind.Header,L.T("不明","Unknown")));}
        else {
            if(p[0]>chainEnd)regions.Add(new(chainEnd,p[0],RegionKind.Header,L.T("ブロックの終わり","End of blocks")));
            for(int i=0;i<TableCount-1;i++) {
                if(p[i+1]<=p[i])continue;
                var (kind,name)=i switch{0=>(RegionKind.Stack,L.T("U スタック","U stack")),1=>(RegionKind.Stack,L.T("S スタック","S stack")),
                    16=>(RegionKind.Work,L.T("BASIC 作業域","BASIC work")),19=>(RegionKind.Work,L.T("IOCS 作業域","IOCS work")),_=>(RegionKind.Work,L.T("作業域","Work area"))};
                regions.Add(new(p[i],p[i+1],kind,name));
            }
            regions.Add(new(p[^1],SystemAt,RegionKind.MachineCode,L.T("機械語エリア","Machine code")));
        }
        regions.Add(new(SystemAt,0xc0000,RegionKind.System,L.T("システム","System")));
        // Empty ones (the machine-code area before any is taken) are not drawn at all.
        return regions.Where(r=>r.Size>0).ToList();
    }
    /// <summary>RAM カードの並び（初期化されていなければ1区画）。</summary>
    internal static List<Region> Card(Core core) {
        int bottom=0x40000,top=bottom+core.CardKB*1024;
        var regions=new List<Region>();
        var head=core.Dump(bottom,2);
        if(head[0]!=0x10 || head[1]!=0x12){regions.Add(new(bottom,top,RegionKind.Free,L.T("未初期化","Not set up")));return regions;}
        int end=Chain(core,bottom,top,regions);
        if(end<top)regions.Add(new(end,top,RegionKind.Free,L.T("使っていない","Unused")));
        return regions;
    }
    /// <summary>スロットの見出しとブロックの並びを regions に足し、並びの終わりを返す。</summary>
    static int Chain(Core core,int bottom,int limit,List<Region> regions) {
        regions.Add(new(bottom,bottom+0x18,RegionKind.Header,L.T("スロットの見出し","Slot header")));
        int at=bottom+0x18;
        for(int n=0;n<64 && at<limit-0x22;n++) {
            var h=core.Dump(at,0x1c);
            if(h[0]!=0xfb)break;
            int size=Le3(h.AsSpan(0x11)),used=Le3(h.AsSpan(0x16));
            if(size<used || used<0x1c || at+size>limit)break;
            string name=System.Text.Encoding.ASCII.GetString(h,1,8).TrimEnd();
            var (kind,shown)=name switch{"DATA"=>(RegionKind.Variables,L.T("変数","Variables")),"TEXT"=>(RegionKind.Program,L.T("プログラム","Program")),
                "RAMFILE"=>(RegionKind.RamDisk,L.T("RAM ディスク","RAM disk")),"FUNCKEY"=>(RegionKind.Block,L.T("PF キー","PF keys")),_=>(RegionKind.Block,name)};
            regions.Add(new(at,at+used,kind,shown));
            if(size>used)regions.Add(new(at+used,at+size,RegionKind.Free,L.T("空き","Free")));
            at+=size;
        }
        return at;
    }
    // The ROM's reservation moves everything from the top of the free space up
    // to the machine-code area down by the change, and the free space shrinks:
    // what the map will look like once a new size is applied.
    /// <summary>機械語エリアを size にしたあとの並び（空きの上から機械語エリアまでが下へずれる）。</summary>
    internal static List<Region> WithMachineCode(List<Region> regions,int size) {
        // None taken yet: an empty one just below the system bytes, to grow from.
        if(regions.All(r=>r.Kind!=RegionKind.MachineCode) && regions.FindIndex(r=>r.Kind==RegionKind.System) is int sys and >=0) {
            regions=[..regions];regions.Insert(sys,new(regions[sys].Start,regions[sys].Start,RegionKind.MachineCode,L.T("機械語エリア","Machine code")));
        }
        var ml=regions.FirstOrDefault(r=>r.Kind==RegionKind.MachineCode);
        var free=regions.Where(r=>r.Kind==RegionKind.Free).OrderByDescending(r=>r.Size).FirstOrDefault();
        if(ml is null || free is null)return regions;
        int delta=size-ml.Size;
        return regions.Select(r=>
            r==free?r with{End=r.End-delta}
            :r.Kind==RegionKind.MachineCode?r with{Start=r.Start-delta}
            :r.Start>=free.End && r.Kind!=RegionKind.System?r with{Start=r.Start-delta,End=r.End-delta}
            :r).Where(r=>r.Size>0).ToList();
    }
    /// <summary>区画の種類の名前（凡例に使う）。</summary>
    internal static string KindName(RegionKind kind)=>kind switch{
        RegionKind.Program=>L.T("プログラム","Program"),RegionKind.Variables=>L.T("変数","Variables"),RegionKind.Block=>L.T("PFキー・AER など","PF keys, AER..."),
        RegionKind.RamDisk=>L.T("RAM ディスク","RAM disk"),RegionKind.Free=>L.T("空き","Free"),RegionKind.Stack=>L.T("スタック","Stacks"),RegionKind.Work=>L.T("作業域","Work areas"),
        RegionKind.MachineCode=>L.T("機械語エリア","Machine code"),RegionKind.System=>L.T("システム","System"),_=>L.T("見出しなど","Headers")};
    /// <summary>大きさを読みやすく（1KB 以上は KB、小数1桁まで）。</summary>
    internal static string Size(int n)=>n>=1024?(n%1024==0?$"{n/1024}KB":$"{n/1024.0:0.0}KB"):$"{n}B";
}
