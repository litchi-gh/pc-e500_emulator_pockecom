// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace PokeCom;

// Two of Windows' own ways in, so nothing has to be installed: XInput for the
// Xbox kind (and pads that copy it), and the joystick functions of winmm for
// every other game controller Windows knows (the DirectInput kind: PlayStation,
// Switch, the many generic USB pads). An XInput pad shows up in winmm as well;
// that copy is left out of the list, told by "IG_" in its HID device name.
/// <summary>コントローラーを探し、押されているボタン（操作の名前）を読む。</summary>
internal static partial class GamePad
{
    // ---- XInput
    [StructLayout(LayoutKind.Sequential)]
    struct XInputState {public uint Packet;public ushort Buttons;public byte LeftTrigger,RightTrigger;public short LX,LY,RX,RY;}
    [DllImport("xinput1_4.dll")] static extern int XInputGetState(int index,out XInputState state);
    static readonly (ushort Bit,string Id)[] XButtons=[
        (0x0001,"Up"),(0x0002,"Down"),(0x0004,"Left"),(0x0008,"Right"),(0x0010,"Start"),(0x0020,"Back"),(0x0040,"LS"),(0x0080,"RS"),
        (0x0100,"LB"),(0x0200,"RB"),(0x1000,"A"),(0x2000,"B"),(0x4000,"X"),(0x8000,"Y")];

    // ---- winmm joystick
    [StructLayout(LayoutKind.Sequential)]
    struct JoyInfoEx {public int Size,Flags,X,Y,Z,R,U,V,Buttons,ButtonNumber,Pov,Reserved1,Reserved2;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct JoyCaps {
        public ushort Mid,Pid;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Pname;
        public int Xmin,Xmax,Ymin,Ymax,Zmin,Zmax,NumButtons,PeriodMin,PeriodMax,Rmin,Rmax,Umin,Umax,Vmin,Vmax,Caps,MaxAxes,NumAxes,MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string RegKey;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string OemVxD;
    }
    [DllImport("winmm.dll")] static extern int joyGetNumDevs();
    [DllImport("winmm.dll")] static extern int joyGetPosEx(int id,ref JoyInfoEx info);
    [DllImport("winmm.dll",CharSet=CharSet.Unicode,EntryPoint="joyGetDevCapsW")] static extern int joyGetDevCaps(nint id,out JoyCaps caps,int size);
    [DllImport("winmm.dll")] static extern int joyConfigChanged(int flags);
    const int JOY_RETURNALL=0xFF,JOY_RETURNPOVCTS=0x200,JOYCAPS_HASZ=1,JOYCAPS_HASR=2,JOYCAPS_HASU=4,JOYCAPS_HASV=8,JOYCAPS_HASPOV=0x10,JOYCAPS_POVCTS=0x40;

    // ---- Raw Input, only to tell which HID devices are XInput ones
    [StructLayout(LayoutKind.Sequential)] struct RawInputDeviceList {public nint Device;public int Type;}
    [DllImport("user32.dll",SetLastError=true)] static extern int GetRawInputDeviceList([Out] RawInputDeviceList[]? list,ref int count,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="GetRawInputDeviceInfoW")] static extern int GetRawInputDeviceInfo(nint device,int command,[Out] char[]? data,ref int size);
    const int RIM_TYPEHID=2,RIDI_DEVICENAME=0x20000007;
    static readonly Regex Vid=new(@"VID[_&](?:000[12])?([0-9A-F]{4})",RegexOptions.IgnoreCase),Pid=new(@"PID[_&]([0-9A-F]{4})",RegexOptions.IgnoreCase);
    /// <summary>XInput で読める HID 機器の VID・PID（名前に IG_ がある機器）。</summary>
    static HashSet<(int,int)> XInputHids() {
        var found=new HashSet<(int,int)>();
        try {
            int size=Marshal.SizeOf<RawInputDeviceList>(),count=0;
            if(GetRawInputDeviceList(null,ref count,size)!=0 || count==0)return found;
            var list=new RawInputDeviceList[count];
            if(GetRawInputDeviceList(list,ref count,size)<0)return found;
            foreach(var d in list.Take(count)) {
                if(d.Type!=RIM_TYPEHID)continue;
                int chars=0;GetRawInputDeviceInfo(d.Device,RIDI_DEVICENAME,null,ref chars);
                if(chars<=0)continue;
                var name=new char[chars];
                if(GetRawInputDeviceInfo(d.Device,RIDI_DEVICENAME,name,ref chars)<=0)continue;
                string text=new string(name).TrimEnd('\0');
                if(!text.Contains("IG_",StringComparison.OrdinalIgnoreCase))continue;
                var v=Vid.Match(text);var p=Pid.Match(text);
                if(v.Success && p.Success)found.Add((Convert.ToInt32(v.Groups[1].Value,16),Convert.ToInt32(p.Groups[1].Value,16)));
            }
        } catch(Exception) {}
        return found;
    }
    // winmm names nearly every pad "Microsoft PC-joystick driver"; the product's
    // own name is kept per VID and PID under MediaProperties.
    /// <summary>コントローラーの製品名（レジストリの OEMName。なければ winmm の名前）。</summary>
    static string ProductName(JoyCaps caps) {
        string key=$@"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_{caps.Mid:X4}&PID_{caps.Pid:X4}";
        foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine}) {
            try {if(hive.OpenSubKey(key)?.GetValue("OEMName") is string n && n.Trim().Length>0)return n.Trim();}
            catch(Exception) {}
        }
        return string.IsNullOrWhiteSpace(caps.Pname)?L.T("ゲームコントローラー","Game controller"):caps.Pname.Trim();
    }

    /// <summary>今つながっているコントローラーの一覧（XInput が先）。</summary>
    internal static List<PadDevice> Scan() {
        var list=new List<PadDevice>();
        for(int i=0;i<4;i++) {
            try {if(XInputGetState(i,out _)==0)list.Add(new($"xinput:{i}",L.T($"Xbox 系コントローラー {i+1}（XInput）",$"Xbox-style controller {i+1} (XInput)"),PadKind.XInput,i));}
            catch(Exception) {break;}
        }
        try {
            // A pad plugged in after the first call is seen only once winmm is told to look again.
            joyConfigChanged(0);
            var xinput=XInputHids();
            int count=Math.Min(joyGetNumDevs(),16);
            for(int id=0;id<count;id++) {
                var info=new JoyInfoEx{Size=Marshal.SizeOf<JoyInfoEx>(),Flags=JOY_RETURNALL};
                if(joyGetPosEx(id,ref info)!=0)continue;
                if(joyGetDevCaps(id,out var caps,Marshal.SizeOf<JoyCaps>())!=0)continue;
                if(xinput.Contains((caps.Mid,caps.Pid)))continue;
                int c=caps.Caps;
                (int,int)?[] axes=[(caps.Xmin,caps.Xmax),(caps.Ymin,caps.Ymax),
                    (c&JOYCAPS_HASZ)!=0?(caps.Zmin,caps.Zmax):null,(c&JOYCAPS_HASR)!=0?(caps.Rmin,caps.Rmax):null,
                    (c&JOYCAPS_HASU)!=0?(caps.Umin,caps.Umax):null,(c&JOYCAPS_HASV)!=0?(caps.Vmin,caps.Vmax):null];
                // Two of one model share VID and PID; the second is told apart by a number.
                string baseId=$"joy:{caps.Mid:X4}:{caps.Pid:X4}",pid=baseId;
                for(int n=2;list.Any(d=>d.Id==pid);n++)pid=$"{baseId}:{n}";
                list.Add(new(pid,ProductName(caps)+L.T("（DirectInput）"," (DirectInput)"),PadKind.DirectInput,id,Math.Clamp(caps.NumButtons,0,32),(c&JOYCAPS_HASPOV)!=0,axes));
            }
        } catch(Exception) {}
        return list;
    }
    /// <summary>押されている操作を読む。</summary>
    /// <param name="device">コントローラー</param>
    /// <param name="pressed">押されている操作を入れる（先に空にする）</param>
    /// <param name="axes">スティックとトリガーの量を入れる（要らなければ null）</param>
    /// <returns>読めたか（外されていれば false）</returns>
    internal static bool Read(PadDevice device,HashSet<string> pressed,PadAxes? axes=null) {
        pressed.Clear();axes?.Clear();
        try {
            if(device.Kind==PadKind.XInput) {
                if(XInputGetState(device.Index,out var s)!=0)return false;
                foreach(var (bit,id) in XButtons)if((s.Buttons&bit)!=0)pressed.Add(id);
                if(s.LeftTrigger>=TriggerThreshold)pressed.Add("LT");
                if(s.RightTrigger>=TriggerThreshold)pressed.Add("RT");
                Stick(s.LX,s.LY,"LStick",pressed);Stick(s.RX,s.RY,"RStick",pressed);
                if(axes is not null) {
                    axes.LX=s.LX/32768f;axes.LY=-s.LY/32768f;axes.RX=s.RX/32768f;axes.RY=-s.RY/32768f;
                    axes.LT=s.LeftTrigger/255f;axes.RT=s.RightTrigger/255f;
                }
                return true;
            }
            var info=new JoyInfoEx{Size=Marshal.SizeOf<JoyInfoEx>(),Flags=JOY_RETURNALL|(device.Pov?JOY_RETURNPOVCTS:0)};
            if(joyGetPosEx(device.Index,ref info)!=0)return false;
            for(int b=0;b<device.Buttons;b++)if((info.Buttons&(1<<b))!=0)pressed.Add($"Button{b+1}");
            // The POV is in hundredths of a degree clockwise from up; FFFFh is centred.
            if(device.Pov && (info.Pov&0xFFFF)!=0xFFFF) {
                int a=info.Pov&0xFFFF;
                if(a>=31500 || a<=4500)pressed.Add("Up");
                if(a>=4500 && a<=13500)pressed.Add("Right");
                if(a>=13500 && a<=22500)pressed.Add("Down");
                if(a>=22500 && a<=31500)pressed.Add("Left");
            }
            var ranges=device.Axes??[];
            int[] values=[info.X,info.Y,info.Z,info.R,info.U,info.V];
            for(int i=0;i<ranges.Length && i<6;i++) {
                if(ranges[i] is not (int min,int max) || max<=min)continue;
                // -1 to 1 across the range; half way out counts as pressed.
                double v=(values[i]-min)*2.0/(max-min)-1;
                // X and Y are the left stick; Z and R the right one on most pads.
                if(axes is not null)switch(i){case 0:axes.LX=(float)v;break;case 1:axes.LY=(float)v;break;case 2:axes.RX=(float)v;break;case 3:axes.RY=(float)v;break;}
                string minus=i switch{0=>"LStickLeft",1=>"LStickUp",_=>$"Axis{AxisNames[i]}-"},plus=i switch{0=>"LStickRight",1=>"LStickDown",_=>$"Axis{AxisNames[i]}+"};
                if(v<=-0.5)pressed.Add(minus);else if(v>=0.5)pressed.Add(plus);
            }
            return true;
        } catch(Exception) {return false;}
    }
}
