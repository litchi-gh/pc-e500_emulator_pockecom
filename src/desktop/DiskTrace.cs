// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
namespace PokeCom;

// Modeless, so the trace has to follow the running machine instead of showing
// the snapshot that happened to exist when the window was opened.
internal sealed class DiskTraceDialog : Form
{
    readonly TextBox text=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Font=new Font("Consolas",10),BackColor=Color.White};
    readonly CheckBox live=new(){Text="自動更新",Checked=true,AutoSize=true,Margin=new Padding(12,10,0,0)};
    readonly System.Windows.Forms.Timer timer=new(){Interval=500};
    readonly Func<Core?> read;
    readonly Func<string> folder;
    internal DiskTraceDialog(Func<Core?> read,Func<string> folder,Action<Action> guard)
    {
        this.read=read;this.folder=folder;
        Text="CE-140F / X:";ClientSize=new Size(760,470);MinimumSize=new Size(420,260);
        StartPosition=FormStartPosition.CenterScreen;Font=new Font("Yu Gothic UI",10);
        var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=46,WrapContents=false,Padding=new Padding(6,4,0,0)};
        var open=new Button{Text="保存フォルダを開く",AutoSize=true};
        var refresh=new Button{Text="更新",AutoSize=true,Margin=new Padding(6,0,0,0)};
        open.Click+=(_,_)=>guard(()=>{Directory.CreateDirectory(folder());Process.Start(new ProcessStartInfo(folder()){UseShellExecute=true});});
        refresh.Click+=(_,_)=>Render();
        bar.Controls.Add(open);bar.Controls.Add(refresh);bar.Controls.Add(live);
        Controls.Add(text);Controls.Add(bar);
        timer.Tick+=(_,_)=>{if(live.Checked)Render();};
        Shown+=(_,_)=>{Render();timer.Start();};
        FormClosed+=(_,_)=>timer.Stop();
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    internal void Render() {
        var core=read();
        LiveText.Update(text,"保存先: "+folder()+"\r\n\r\n"+(core is null?"ROMが読み込まれていません。":core.DiskTrace().Replace("\n","\r\n")));
    }
}
