// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
namespace PokeCom;

// The kernel's joystick devices (/dev/input/js*), which a desktop session may
// read without any set-up. A pad whose buttons are the kernel's gamepad ones
// (BTN_SOUTH and on: Xbox, PlayStation, Switch and most others) is given the
// Xbox names; any other is numbered, as a DirectInput pad is on Windows.
// カーネルのジョイスティック（/dev/input/js*）を読む。ゲームパッドのボタンを持つもの（Xbox・PlayStation・Switch など）は
// Xbox の名前にし、ほかは Windows の DirectInput と同じく番号にする。
internal static partial class GamePad
{
    [DllImport("libc",SetLastError=true)] static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path,int flags);
    [DllImport("libc",SetLastError=true)] static extern int close(int fd);
    [DllImport("libc",SetLastError=true)] static extern nint read(int fd,byte[] buffer,nint count);
    [DllImport("libc",SetLastError=true)] static extern int ioctl(int fd,nuint request,byte[] data);
    const int NonBlocking=0x800;
    const nuint AxisCount=0x80016a11,ButtonCount=0x80016a12,Name=0x80806a13,AxisMap=0x80406a32,ButtonMap=0x84006a34;
    // Linux's own codes (linux/input-event-codes.h).
    const int South=0x130,HatX=0x10,HatY=0x11;
    /// <summary>読んでいるパッド：開いたファイルと、今のボタンと軸の値。</summary>
    sealed class OpenPad
    {
        internal int Fd;
        internal int[] Buttons=[],Axes=[];
        internal int[] ButtonCodes=[],AxisCodes=[];
        internal int Vendor;
    }
    static readonly Dictionary<string,OpenPad> pads=[];
    static readonly object gate=new();
    static string Path(int index)=>$"/dev/input/js{index}";
    static string Sys(int index,string name) {try{return File.ReadAllText($"/sys/class/input/js{index}/device/{name}").Trim();}catch(Exception){return "";}}
    static OpenPad? Open(int index) {
        int fd=open(Path(index),NonBlocking);
        if(fd<0)return null;
        var one=new byte[1];
        if(ioctl(fd,AxisCount,one)<0){close(fd);return null;}
        int axes=one[0];ioctl(fd,ButtonCount,one);int buttons=one[0];
        var axisMap=new byte[64];ioctl(fd,AxisMap,axisMap);
        var buttonMap=new byte[1024];ioctl(fd,ButtonMap,buttonMap);
        int.TryParse(Sys(index,"id/vendor"),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out int vendor);
        return new OpenPad{Fd=fd,Vendor=vendor,Buttons=new int[buttons],Axes=new int[axes],AxisCodes=[..axisMap.Take(axes).Select(b=>(int)b)],
            ButtonCodes=[..Enumerable.Range(0,Math.Min(buttons,512)).Select(i=>(int)BinaryPrimitives.ReadUInt16LittleEndian(buttonMap.AsSpan(i*2)))]};
    }
    static string NameOf(int index) {
        int fd=open(Path(index),NonBlocking);if(fd<0)return "";
        var name=new byte[128];int n=ioctl(fd,Name,name);close(fd);
        return n<0?"":Encoding.UTF8.GetString(name).TrimEnd('\0').Trim();
    }
    /// <summary>今つながっているコントローラーの一覧（ゲームパッドが先）。</summary>
    internal static List<PadDevice> Scan() {
        var list=new List<PadDevice>();
        if(!OperatingSystem.IsLinux())return list;
        try {
            for(int index=0;index<16;index++) {
                if(!File.Exists(Path(index)))continue;
                var pad=Open(index);if(pad is null)continue;
                close(pad.Fd);
                // A tablet or an absolute mouse (a virtual machine's) is a joystick to the kernel as well: only what has a joystick's or a gamepad's buttons is one.
                // タブレットや仮想マシンのマウスもジョイスティックとして見えるので、ジョイスティックかゲームパッドのボタンを持つものだけにする。
                if(!pad.ButtonCodes.Any(c=>c is >=0x120 and <0x13f))continue;
                string vendor=Sys(index,"id/vendor").ToUpperInvariant(),product=Sys(index,"id/product").ToUpperInvariant(),name=NameOf(index);
                if(name.Length==0)name=L.T("ゲームコントローラー","Game controller");
                bool gamepad=pad.ButtonCodes.Contains(South) && pad.AxisCodes.Length>=2;
                string baseId=gamepad?$"pad:{vendor}:{product}":$"joy:{vendor}:{product}",id=baseId;
                for(int n=2;list.Any(d=>d.Id==id);n++)id=$"{baseId}:{n}";
                if(gamepad){list.Add(new(id,name,PadKind.XInput,index));continue;}
                // X, Y, Z, R, U, V in the order the axes come, the hat apart.
                bool hat=pad.AxisCodes.Contains(HatX);
                var axes=new (int,int)?[6];int a=0;
                foreach(int code in pad.AxisCodes)if(code<HatX && a<6)axes[a++]=(-32767,32767);
                list.Add(new(id,name,PadKind.DirectInput,index,Math.Min(32,pad.Buttons.Length),hat,axes));
            }
        } catch(Exception) {}
        return [..list.OrderBy(d=>d.Kind)];
    }
    /// <summary>押されている操作を読む。</summary>
    /// <returns>読めたか（外されていれば false）</returns>
    internal static bool Read(PadDevice device,HashSet<string> pressed,PadAxes? axes=null) {
        pressed.Clear();axes?.Clear();
        if(!OperatingSystem.IsLinux())return false;
        lock(gate) {
            if(!pads.TryGetValue(device.Id,out var pad)){pad=Open(device.Index);if(pad is null)return false;pads[device.Id]=pad;}
            // js_event: time (4), value (2), type (1), number (1); the first reads give every control's state.
            var buffer=new byte[8*64];
            while(true) {
                nint got=read(pad.Fd,buffer,buffer.Length);
                if(got<0) {
                    const int EAGAIN=11;
                    if(Marshal.GetLastPInvokeError()==EAGAIN)break;
                    close(pad.Fd);pads.Remove(device.Id);return false;
                }
                if(got==0)break;
                for(int at=0;at+8<=got;at+=8) {
                    int value=BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(at+4)),type=buffer[at+6]&0x7f,number=buffer[at+7];
                    if(type==1 && number<pad.Buttons.Length)pad.Buttons[number]=value;
                    else if(type==2 && number<pad.Axes.Length)pad.Axes[number]=value;
                }
            }
            if(device.Kind==PadKind.XInput)ReadGamepad(pad,pressed,axes);else ReadNumbered(pad,pressed,axes);
            return true;
        }
    }
    static void ReadGamepad(OpenPad pad,HashSet<string> pressed,PadAxes? axes) {
        bool Down(int code){int i=Array.IndexOf(pad.ButtonCodes,code);return i>=0 && i<pad.Buttons.Length && pad.Buttons[i]!=0;}
        int Axis(int code,int none=0){int i=Array.IndexOf(pad.AxisCodes,code);return i>=0?pad.Axes[i]:none;}
        bool Has(int code)=>pad.AxisCodes.Contains(code);
        // The xpad driver (and the many that copy it) reports X and Y as BTN_NORTH and
        // BTN_WEST; Sony's, Nintendo's and others report where the button is.
        bool byPlace=pad.Vendor is 0x054c or 0x057e;
        foreach(var (code,id) in new[]{(0x130,"A"),(0x131,"B"),(0x133,byPlace?"Y":"X"),(0x134,byPlace?"X":"Y"),(0x136,"LB"),(0x137,"RB"),(0x138,"LT"),(0x139,"RT"),(0x13a,"Back"),(0x13b,"Start"),(0x13d,"LS"),(0x13e,"RS"),
            (0x220,"Up"),(0x221,"Down"),(0x222,"Left"),(0x223,"Right")})if(Down(code))pressed.Add(id);
        int hx=Axis(HatX),hy=Axis(HatY);
        if(hx<0)pressed.Add("Left");else if(hx>0)pressed.Add("Right");
        if(hy<0)pressed.Add("Up");else if(hy>0)pressed.Add("Down");
        // Triggers on ABS_Z and ABS_RZ where the right stick is RX and RY; on GAS and BRAKE for some Bluetooth pads, whose right stick is Z and RZ.
        bool rx=Has(3);
        double lt=rx?(Axis(2,-32767)+32767)/65534.0:Has(0x0a)?(Axis(0x0a,-32767)+32767)/65534.0:0;
        double rt=rx?(Axis(5,-32767)+32767)/65534.0:Has(0x09)?(Axis(0x09,-32767)+32767)/65534.0:0;
        if(lt*255>=TriggerThreshold)pressed.Add("LT");
        if(rt*255>=TriggerThreshold)pressed.Add("RT");
        int lx=Axis(0),ly=Axis(1),rxv=Axis(rx?3:2),ryv=Axis(rx?4:5);
        Stick((short)lx,(short)-ly,"LStick",pressed);Stick((short)rxv,(short)-ryv,"RStick",pressed);
        if(axes is null)return;
        axes.LX=lx/32768f;axes.LY=ly/32768f;axes.RX=rxv/32768f;axes.RY=ryv/32768f;axes.LT=(float)lt;axes.RT=(float)rt;
    }
    static void ReadNumbered(OpenPad pad,HashSet<string> pressed,PadAxes? axes) {
        for(int b=0;b<Math.Min(32,pad.Buttons.Length);b++)if(pad.Buttons[b]!=0)pressed.Add($"Button{b+1}");
        int n=0;
        for(int i=0;i<pad.Axes.Length;i++) {
            int code=pad.AxisCodes[i];double v=pad.Axes[i]/32767.0;
            if(code is HatX or HatY) {
                if(v<=-0.5)pressed.Add(code==HatX?"Left":"Up");else if(v>=0.5)pressed.Add(code==HatX?"Right":"Down");
                continue;
            }
            if(code>HatX || n>=6)continue;
            int at=n++;
            if(axes is not null)switch(at){case 0:axes.LX=(float)v;break;case 1:axes.LY=(float)v;break;case 2:axes.RX=(float)v;break;case 3:axes.RY=(float)v;break;}
            string minus=at switch{0=>"LStickLeft",1=>"LStickUp",_=>$"Axis{AxisNames[at]}-"},plus=at switch{0=>"LStickRight",1=>"LStickDown",_=>$"Axis{AxisNames[at]}+"};
            if(v<=-0.5)pressed.Add(minus);else if(v>=0.5)pressed.Add(plus);
        }
    }
}
