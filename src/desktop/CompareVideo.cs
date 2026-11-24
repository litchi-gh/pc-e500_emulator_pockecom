// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
using System.Drawing.Imaging;
namespace PokeCom;

// `PC-E500.exe --compare-video <rom> <game.bin> <ffmpeg.exe> <out.mp4> [seconds]`
// runs a machine-code game (loaded at BB000 and started with CALL &BB000) and
// films its LCD twice from the same machine at the same moments: above without
// the afterimage (frame interpolation), below with it. The time is the video's
// own, 1/60 s a frame, so both halves see exactly the same frames.
// 機械語のゲームを動かし、同じ本体の LCD を、上はフレーム補完なし、下はありで1コマずつ（1秒に60コマ）描いて ffmpeg に流す。
internal static class CompareVideo
{
    const int W=1920,H=1080,Fps=60,CpuHz=768000;
    internal static void Run(string rom,string bin,string ffmpeg,string output,double seconds) {
        Process? encoder=null;
        try {
            using var form=new MainForm(rom,testing:true);
            form.CreateControl();form.Show();Application.DoEvents();
            var core=form.TestCore;
            void Key(string key){int code=Keyboard.Codes[key];core.Key(code,true);core.Run(100000);core.Key(code,false);core.Run(100000);}
            // To BASIC, room for the game from BB000 up, the game loaded, and started.
            core.Run(2048000);Key("F1");core.Run(2048000);Key("BASIC");core.Run(1024000);Key("BASIC");core.Run(1024000);
            int size=0xBFC00-0xBB000;
            form.TestReserveMachineCode(size);
            if(MachineCodeArea.Read(core) is not {} area || area.Start>0xBB000)throw new InvalidOperationException("The machine-code area could not be reserved from BB000");
            string loaded=form.TestLoadFile(bin);
            form.TestText("CALL &BB000");
            // A few seconds for the title to come up.
            core.Run(CpuHz*3);
            // The LCD as the user has it set (real or plain, contrast).
            HardwareSettings s;
            try{s=HardwareSettings.Load(Path.Combine(AppFolders.Data(AppFolders.Program()),"state","settings.json"));}catch(Exception){s=HardwareSettings.Defaults;}
            const int PanelW=1840,PanelH=430;
            using var host=new Form{AutoScaleMode=AutoScaleMode.None,FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-6000,-6000),ClientSize=new Size(PanelW,PanelH*2)};
            PocketSurface Panel(bool afterimage,int y) {
                var p=new PocketSurface{Dock=DockStyle.None,Core=core,Bounds=new Rectangle(0,y,PanelW,PanelH),BackColor=Color.FromArgb(24,24,28)};
                p.SetModel(s.Appearance);p.SetLcd(s.LcdDotLines,s.LcdContrast);p.SetScreenOnly(true);p.SetAfterimage(afterimage);
                host.Controls.Add(p);return p;
            }
            var plain=Panel(false,0);var smooth=Panel(true,PanelH);
            host.Show();Application.DoEvents();
            // Set again once shown: the display scaling must not make the panels larger than the frame.
            host.ClientSize=new Size(PanelW,PanelH*2);plain.Bounds=new Rectangle(0,0,PanelW,PanelH);smooth.Bounds=new Rectangle(0,PanelH,PanelW,PanelH);
            if(plain.Size!=new Size(PanelW,PanelH))throw new InvalidOperationException($"Panel is {plain.Size}");
            encoder=Process.Start(new ProcessStartInfo(ffmpeg,$"-y -loglevel error -f rawvideo -pix_fmt bgra -s {W}x{H} -r {Fps} -i - -c:v libx264 -preset medium -crf 16 -pix_fmt yuv420p \"{output}\""){UseShellExecute=false,RedirectStandardInput=true,CreateNoWindow=true})!;
            var pipe=encoder.StandardInput.BaseStream;
            using var frame=new Bitmap(W,H,PixelFormat.Format32bppArgb);
            using var shot=new Bitmap(PanelW,PanelH,PixelFormat.Format32bppArgb);
            var bytes=new byte[W*H*4];
            using var right=new StringFormat{Alignment=StringAlignment.Far};using var label=new Font("Yu Gothic UI",34,FontStyle.Bold,GraphicsUnit.Pixel);using var small=new Font("Yu Gothic UI",22,GraphicsUnit.Pixel);
            int frames=(int)Math.Round(seconds*Fps);
            for(int n=0;n<frames;n++) {
                double t=(double)n/Fps;
                core.Run(CpuHz/Fps);core.Refresh();
                plain.UpdateLcd(t);smooth.UpdateLcd(t);
                using(var g=Graphics.FromImage(frame)) {
                    g.Clear(Color.FromArgb(24,24,28));g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    void Draw(PocketSurface p,int y,string text,Color tint) {
                        p.DrawToBitmap(shot,new Rectangle(0,0,PanelW,PanelH));
                        g.DrawImageUnscaled(shot,(W-PanelW)/2,y);
                        using var b=new SolidBrush(tint);g.DrawString(text,label,b,(W-PanelW)/2+8,y-48);
                    }
                    Draw(plain,62,L.T("フレーム補完なし","Without frame interpolation"),Color.FromArgb(230,230,235));
                    Draw(smooth,600,L.T("フレーム補完あり","With frame interpolation"),Color.FromArgb(120,190,255));
                    using var sub=new SolidBrush(Color.FromArgb(150,150,160));
                    g.DrawString("PC-E500 Emulator PokeCom",small,sub,new RectangleF(0,H-36,W-24,30),right);
                }
                var data=frame.LockBits(new Rectangle(0,0,W,H),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0,bytes,0,bytes.Length);frame.UnlockBits(data);
                pipe.Write(bytes,0,bytes.Length);
                if(n%30==0)Application.DoEvents();
            }
            pipe.Close();encoder.WaitForExit();
            int exit=encoder.ExitCode;encoder.Dispose();encoder=null;
            if(exit!=0)throw new InvalidOperationException("ffmpeg failed: "+exit);
            File.WriteAllText(output+".txt","OK\n"+loaded+"\n");
        } catch(Exception ex) {File.WriteAllText(output+".txt",ex.ToString());Environment.ExitCode=1;try{encoder?.Kill();}catch{}}
    }
}
