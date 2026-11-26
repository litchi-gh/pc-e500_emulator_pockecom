// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>コントローラーの種類（XInput か、Windows のゲームコントローラー＝DirectInput 系か）。</summary>
internal enum PadKind {XInput,DirectInput}

/// <summary>見つかったコントローラー1台。</summary>
/// <param name="Id">設定に残す名前（xinput:番号、joy:VID:PID）</param>
/// <param name="Name">表示する名前</param>
/// <param name="Kind">種類</param>
/// <param name="Index">XInput の番号（0〜3）か winmm のジョイスティック番号</param>
/// <param name="Buttons">ボタンの数（DirectInput 系）</param>
/// <param name="Pov">十字キー（POV）があるか</param>
/// <param name="Axes">軸（X・Y・Z・R・U・V の順の範囲。ない軸は null）</param>
internal sealed record PadDevice(string Id,string Name,PadKind Kind,int Index,int Buttons=0,bool Pov=false,(int Min,int Max)?[]? Axes=null);

/// <summary>コントローラーの操作の名前と、状態の読み方の共通の部分（探す・読むのは、システムごとの部分が行う）。</summary>
internal static partial class GamePad
{
    // ---- Control names: one set for both kinds where they mean the same thing.
    /// <summary>XInput のコントローラーの操作（表示の順）。</summary>
    internal static readonly string[] XInputControls=[
        "Up","Down","Left","Right","A","B","X","Y","LB","RB","LT","RT","Back","Start","LS","RS",
        "LStickUp","LStickDown","LStickLeft","LStickRight","RStickUp","RStickDown","RStickLeft","RStickRight"];
    static readonly string[] AxisNames=["X","Y","Z","R","U","V"];
    /// <summary>コントローラーにある操作（表示の順）。DirectInput 系は、十字キー・左スティック（X・Y 軸）・ほかの軸・ボタン。</summary>
    internal static string[] Controls(PadDevice? device) {
        if(device is null || device.Kind==PadKind.XInput)return XInputControls;
        var list=new List<string>();
        if(device.Pov)list.AddRange(["Up","Down","Left","Right"]);
        var axes=device.Axes??[];
        if(axes.Length>1 && axes[0] is not null && axes[1] is not null)list.AddRange(["LStickUp","LStickDown","LStickLeft","LStickRight"]);
        for(int i=2;i<axes.Length;i++)if(axes[i] is not null)list.AddRange([$"Axis{AxisNames[i]}-",$"Axis{AxisNames[i]}+"]);
        for(int b=1;b<=device.Buttons;b++)list.Add($"Button{b}");
        return list.ToArray();
    }
    /// <summary>操作の表示名。</summary>
    internal static string ControlName(string id)=>id switch {
        "Up"=>L.T("十字キー 上","D-pad up"),"Down"=>L.T("十字キー 下","D-pad down"),"Left"=>L.T("十字キー 左","D-pad left"),"Right"=>L.T("十字キー 右","D-pad right"),
        "LStickUp"=>L.T("左スティック 上","Left stick up"),"LStickDown"=>L.T("左スティック 下","Left stick down"),"LStickLeft"=>L.T("左スティック 左","Left stick left"),"LStickRight"=>L.T("左スティック 右","Left stick right"),
        "RStickUp"=>L.T("右スティック 上","Right stick up"),"RStickDown"=>L.T("右スティック 下","Right stick down"),"RStickLeft"=>L.T("右スティック 左","Right stick left"),"RStickRight"=>L.T("右スティック 右","Right stick right"),
        "LB"=>"LB","RB"=>"RB","LT"=>"LT","RT"=>"RT","Back"=>"Back (View)","Start"=>"Start (Menu)",
        "LS"=>L.T("左スティック押し込み","Left stick press"),"RS"=>L.T("右スティック押し込み","Right stick press"),
        _ when id.StartsWith("Button",StringComparison.Ordinal)=>L.T("ボタン ","Button ")+id[6..],
        // Z and R are drawn as the right stick, so they are named as its directions.
        "AxisR-"=>L.T("右スティック 上（R軸）","Right stick up (R axis)"),"AxisR+"=>L.T("右スティック 下（R軸）","Right stick down (R axis)"),
        "AxisZ-"=>L.T("右スティック 左（Z軸）","Right stick left (Z axis)"),"AxisZ+"=>L.T("右スティック 右（Z軸）","Right stick right (Z axis)"),
        _ when id.StartsWith("Axis",StringComparison.Ordinal)=>L.T("軸 ","Axis ")+id[4..],
        _=>id};

    /// <summary>スティックを倒したとみなす量（片側の半分）と、トリガーを引いたとみなす量。</summary>
    const int StickThreshold=16384,TriggerThreshold=100;
    /// <summary>設定のコントローラー（"" なら一覧の最初）を一覧から選ぶ。</summary>
    internal static PadDevice? Pick(List<PadDevice> found,string id)=>id.Length==0?found.FirstOrDefault():found.FirstOrDefault(d=>d.Id==id);

    static void Stick(short x,short y,string name,HashSet<string> pressed) {
        if(x<=-StickThreshold)pressed.Add(name+"Left");else if(x>=StickThreshold)pressed.Add(name+"Right");
        if(y>=StickThreshold)pressed.Add(name+"Up");else if(y<=-StickThreshold)pressed.Add(name+"Down");
    }
}

/// <summary>スティックの倒し具合（-1〜1、右・下が正）とトリガーの引き具合（0〜1）。</summary>
internal sealed class PadAxes
{
    internal float LX,LY,RX,RY,LT,RT;
    internal void Clear()=>LX=LY=RX=RY=LT=RT=0;
    internal void CopyTo(PadAxes to){to.LX=LX;to.LY=LY;to.RX=RX;to.RY=RY;to.LT=LT;to.RT=RT;}
    internal bool Same(PadAxes o)=>Math.Abs(LX-o.LX)+Math.Abs(LY-o.LY)+Math.Abs(RX-o.RX)+Math.Abs(RY-o.RY)+Math.Abs(LT-o.LT)+Math.Abs(RT-o.RT)<0.01f;
}
