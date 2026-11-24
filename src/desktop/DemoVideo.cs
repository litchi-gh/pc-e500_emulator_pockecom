// SPDX-License-Identifier: GPL-2.0-or-later
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.Json;
namespace PokeCom;

// `PC-E500.exe --demo-video <rom> <timeline.json> <ffmpeg.exe> <out.mp4>` renders
// the introduction video's picture: the emulator run through a fixed script, one
// frame at a time at 30 a second, each frame the window (or a window over it)
// on a plain ground with the scene's heading and the line being spoken as a
// subtitle, piped straight into ffmpeg. The timeline (from the voice clips)
// says when each scene and line starts and ends; the sound is added afterwards.
// Runs in testing mode, so the user's own files are never read or written.
// 紹介動画の映像を作る：決めた手順で本体を動かし、1コマずつ（1秒に30コマ）ウィンドウ・見出し・字幕を合成して ffmpeg に流す。
// 場面と台詞の時刻はタイムライン（音声から作る）に従う。音声はあとで合わせる。テストの扱いで動かし、利用者のファイルには触れない。
/// <summary>紹介動画の映像を書き出す。</summary>
internal static class DemoVideo
{
    sealed record Line(string Text,double Start,double End);
    sealed record Scene(string Id,string Heading,double Start,double End,Line[] Lines);
    sealed record Timeline(int Fps,double Total,Scene[] Scenes,string Version="");
    const int W=1920,H=1080;
    static readonly Rectangle Content=new(80,130,1760,760);
    internal static void Run(string rom,string timelinePath,string ffmpeg,string output) {
        var timeline=JsonSerializer.Deserialize<Timeline>(File.ReadAllText(timelinePath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        string scratch=Path.Combine(Path.GetTempPath(),"pce500-demo-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        Process? encoder=null;
        try {
            using var form=new MainForm(rom,testing:true);
            form.CreateControl();form.Show();Application.DoEvents();
            // The video introduces the coming release, which may not be the build's own number yet.
            version=timeline.Version;
            var core=form.TestCore;
            void Key(string key){int code=Keyboard.Codes[key];core.Key(code,true);core.Run(100000);core.Key(code,false);core.Run(100000);}
            // The machine as the video opens on it: BASIC, a program saved to the
            // drive (then cleared, to be loaded on screen), and 1+2 at the prompt.
            form.TestApplyHardware(form.TestSettings with{DiskEnabled=true,DiskReadOnly=false,DiskFolder=scratch});
            core.Run(2048000);Key("F1");core.Run(2048000);Key("BASIC");core.Run(1024000);
            Key("BASIC");core.Run(1024000);
            foreach(var line in new[]{"10 CLS","20 FOR I=0 TO 31:LOCATE I,1:PRINT \" PC-E500 \";:NEXT I","30 FOR I=31 TO 0 STEP -1:LOCATE I,1:PRINT \" PC-E500 \";:NEXT I","40 GOTO 20"})form.TestText(line);
            form.TestText("SAVE \"X:DEMO.BAS\"");core.Run(3072000);
            // The frame-interpolation scene's own program, saved the same way.
            form.TestText("NEW");
            foreach(var line in Dots)form.TestText(line);
            form.TestText("SAVE \"X:DOTS.BAS\"");core.Run(3072000);
            form.TestText("NEW");Key("BASIC");core.Run(1024000);
            form.TestText("CLS");form.TestText("1+2");form.TestRefresh();
            string demo=Path.Combine(scratch,"DEMO.BAS");
            if(!File.Exists(demo))throw new InvalidOperationException("DEMO.BAS was not saved");
            string dots=Path.Combine(scratch,"DOTS.BAS");
            if(!File.Exists(dots))throw new InvalidOperationException("DOTS.BAS was not saved");

            // After the setup above: applying the hardware settings writes the title again.
            if(version.Length>0)form.Text=System.Text.RegularExpressions.Regex.Replace(form.Text,@"v\d+\.\d+\.\d+","v"+version);
            encoder=Process.Start(new ProcessStartInfo(ffmpeg,$"-y -loglevel error -f rawvideo -pix_fmt bgra -s {W}x{H} -r {timeline.Fps} -i - -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p \"{output}\""){UseShellExecute=false,RedirectStandardInput=true,CreateNoWindow=true})!;
            var pipe=encoder.StandardInput.BaseStream;
            using var frame=new Bitmap(W,H,PixelFormat.Format32bppArgb);
            var bytes=new byte[W*H*4];
            int frames=(int)Math.Ceiling(timeline.Total*timeline.Fps);
            string? entered=null;
            Form? overlay=null;DebugDialog? debugger=null;MachineCodeDialog? ml=null;PadDialog? pad=null;
            (Form Host,PocketSurface Plain,PocketSurface Smooth)? compare=null;
            HardwareDialog? hardware=null;ScrollableControl? scroller=null;
            for(int n=0;n<frames;n++) {
                double t=(double)n/timeline.Fps;
                var scene=timeline.Scenes.LastOrDefault(s=>t>=s.Start)??timeline.Scenes[0];
                double local=t-scene.Start;
                // What each scene does to the machine, on entering it and as it runs.
                if(scene.Id!=entered) {
                    entered=scene.Id;
                    overlay?.Close();overlay=null;
                    compare?.Host.Dispose();compare=null;
                    switch(scene.Id) {
                    case "pad":
                        // A pad drawn as if plugged in; its buttons are pressed by the script below.
                        pad=form.TestShowPad();pad.TestShow(new PadDevice("xinput:0","Xbox コントローラー",PadKind.XInput,0));overlay=pad;break;
                    case "interp":
                        Key("ON/BRK");core.Run(1024000);form.TestLoadFile(dots,true);
                        compare=Compare(core,form.TestSettings);break;
                    case "keys":
                        // A line typed but not entered, for the held arrow keys to run the cursor along.
                        Key("ON/BRK");core.Run(1024000);form.TestText("CLS");form.TestTypeOnly("PRINT \"LONG PRESS\"");form.TestRunCycles(768000);break;
                    case "settings":
                        // The folder shown is the usual one, not the scratch folder (whose path has the PC's user name in it).
                        hardware=new HardwareDialog(form.TestSettings with{DiskFolder="drive-x"},core.RomKB,"ram"){StartPosition=FormStartPosition.CenterScreen};hardware.Show(form);overlay=hardware;
                        scroller=Scroller(hardware);break;
                    case "ml":
                        Key("ON/BRK");core.Run(1024000);form.TestText("CLS");form.TestRefresh();
                        ml=new MachineCodeDialog(RamLayout.Internal(core),0,0x6000){StartPosition=FormStartPosition.CenterScreen};ml.Show(form);overlay=ml;break;
                    case "debug":
                        debugger=form.TestDebugger();debugger.Memory.Jump(0xc0000-core.InternalKB*1024+0x10);debugger.Render();overlay=debugger;break;
                    }
                }
                switch(scene.Id) {
                case "keys":
                    // Left held, then right: the key stays down on the machine and the ROM repeats it.
                    if(local>=0.8 && local-1.0/timeline.Fps<0.8)form.TestRepeatKey(Keys.Left);
                    if(local>=2.8 && local-1.0/timeline.Fps<2.8)form.TestRelease(Keys.Left);
                    if(local>=3.4 && local-1.0/timeline.Fps<3.4)form.TestRepeatKey(Keys.Right);
                    if(local>=5.0 && local-1.0/timeline.Fps<5.0)form.TestRelease(Keys.Right);
                    break;
                case "settings":
                    // The theme at the top first, then down to always on top and the auto power off.
                    if(scroller is not null){int bottom=Math.Max(0,scroller.DisplayRectangle.Height-scroller.ClientSize.Height);scroller.AutoScrollPosition=new Point(0,(int)(bottom*Math.Clamp((local-3.0)/2.5,0,1)));}
                    break;
                case "load":
                    if(local>=0.9 && !loaded){form.TestLoadFile(demo,true);loaded=true;}
                    break;
                case "ml":
                    if(ml is not null){int size=Math.Clamp((int)((local-0.6)/2.4*0x10),0,0x10)*0x100;ml.TestSet(size);}
                    break;
                case "debug":
                    if(debugger is not null && n%6==0)debugger.Render();
                    break;
                case "pad":
                    if(pad is not null)PressPad(pad,local);
                    break;
                }
                // The keys scene runs through the form, so the held keys are fed in as the machine runs.
                if(scene.Id=="keys"){form.TestRunCycles(768000/timeline.Fps);form.TestRefresh();}
                else if(scene.Id is not ("ml" or "debug" or "settings")) {core.Run(768000/timeline.Fps);form.TestRefresh();}
                if(compare is {} c){c.Plain.UpdateLcd(t);c.Smooth.UpdateLcd(t);}
                Application.DoEvents();
                using(var g=Graphics.FromImage(frame))Compose(g,form,overlay,compare,scene,local,t);
                var data=frame.LockBits(new Rectangle(0,0,W,H),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0,bytes,0,bytes.Length);frame.UnlockBits(data);
                pipe.Write(bytes,0,bytes.Length);
            }
            overlay?.Close();compare?.Host.Dispose();
            pipe.Close();encoder.WaitForExit();
            if(encoder.ExitCode!=0)throw new InvalidOperationException("ffmpeg failed: "+encoder.ExitCode);
            File.WriteAllText(output+".txt","OK\n");
        } catch(Exception ex) {File.WriteAllText(output+".txt",ex.ToString());Environment.ExitCode=1;try{encoder?.Kill();}catch{}}
        finally {try{Directory.Delete(scratch,true);}catch{}}
    }
    static bool loaded;
    /// <summary>ウィンドウの中で、縦にスクロールする部分（なければ null）。</summary>
    static ScrollableControl? Scroller(Control root) {
        foreach(Control c in root.Controls) {
            if(c is ScrollableControl {AutoScroll:true} s && s.DisplayRectangle.Height>s.ClientSize.Height)return s;
            if(Scroller(c) is {} inner)return inner;
        }
        return null;
    }
    // The frame-interpolation scene: a ball rolling left a dot at a time and a
    // smaller one at half its speed, stepped slowly as a BASIC game moves things,
    // so the steps show without the afterimage and run on with it.
    // フレーム補完の場面：玉が1ドットずつ左へ転がり、小さい玉はその半分の速さ。BASIC のゲームのようにゆっくり1歩ずつ動かす。
    static readonly string[] Dots=[
        "10 CLS:X=230:Y=230",
        "20 GCURSOR(X,15):GPRINT \"001C3E7F7F7F3E1C00\"",
        "30 GCURSOR(Y,28):GPRINT \"00060F0F0600\":IF X AND 1 THEN 50",
        "40 Y=Y-1:IF Y<0 THEN Y=230",
        "50 X=X-1:IF X<0 THEN X=230",
        "60 FOR W=1 TO 50:NEXT W:GOTO 20"];
    /// <summary>コントローラーの場面の、ボタンを押す台本（時刻・押す操作・スティックとトリガー）。</summary>
    static readonly (double From,double To,string[] Ids,PadAxes? Axes)[] PadScript=[
        (0.5,1.0,["Up"],null),(1.2,1.7,["Right"],null),(1.9,2.4,["A"],null),(2.6,3.1,["B"],null),
        (3.3,4.2,["LStickLeft"],new PadAxes{LX=-0.9f}),(4.4,5.0,["Y"],null),(5.4,5.9,["Down"],null),(6.1,6.6,["X"],null)];
    static string padShown="";
    /// <summary>台本のその時刻に押している操作を、設定画面の絵と表に出す（変わったときだけ）。</summary>
    static void PressPad(PadDialog pad,double local) {
        local%=7.0;   // the script runs round again in a longer scene
        var step=PadScript.FirstOrDefault(s=>local>=s.From && local<s.To);
        string key=step.Ids is null?"":string.Join(",",step.Ids);
        if(key==padShown)return;
        padShown=key;
        if(step.Ids is null)pad.TestPress(new PadAxes());else pad.TestPress(step.Axes??new PadAxes(),step.Ids);
    }
    /// <summary>上は補完なし、下は補完ありの、同じ本体の LCD だけを映す2枚（画面の外のウィンドウに置く）。</summary>
    static (Form Host,PocketSurface Plain,PocketSurface Smooth) Compare(Core core,HardwareSettings s) {
        var host=new Form{AutoScaleMode=AutoScaleMode.None,FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-6000,-6000),ClientSize=new Size(PanelW,PanelH*2)};
        PocketSurface Panel(bool afterimage,int y) {
            var p=new PocketSurface{Dock=DockStyle.None,Core=core,Bounds=new Rectangle(0,y,PanelW,PanelH),BackColor=Color.FromArgb(24,24,28)};
            p.SetModel(s.Appearance);p.SetLcd(true,s.LcdContrast);p.SetScreenOnly(true);p.SetAfterimage(afterimage);
            host.Controls.Add(p);return p;
        }
        var plain=Panel(false,0);var smooth=Panel(true,PanelH);
        host.Show();Application.DoEvents();
        // Set again once shown: the display scaling must not make the panels larger than the frame.
        host.ClientSize=new Size(PanelW,PanelH*2);plain.Bounds=new Rectangle(0,0,PanelW,PanelH);smooth.Bounds=new Rectangle(0,PanelH,PanelW,PanelH);
        return (host,plain,smooth);
    }
    const int PanelW=1440,PanelH=330;
    /// <summary>紹介する版（空なら書かない）。</summary>
    static string version="";
    /// <summary>ウィンドウを絵にする（枠ごと）。</summary>
    static Bitmap Snap(Control window) {
        var b=new Bitmap(window.Width,window.Height);window.DrawToBitmap(b,new Rectangle(0,0,window.Width,window.Height));return b;
    }
    static readonly Font HeadingFont=new("Yu Gothic UI",34,FontStyle.Bold,GraphicsUnit.Pixel),SubtitleFont=new("Yu Gothic UI",40,FontStyle.Bold,GraphicsUnit.Pixel),
        TitleFont=new("Yu Gothic UI",86,FontStyle.Bold,GraphicsUnit.Pixel),TitleSub=new("Yu Gothic UI",40,FontStyle.Regular,GraphicsUnit.Pixel),CreditFont=new("Yu Gothic UI",30,FontStyle.Regular,GraphicsUnit.Pixel);
    static readonly Color Accent=Color.FromArgb(0,103,192);
    /// <summary>1コマを描く：地、ウィンドウ（と上に開いたウィンドウ）、目印、見出し、字幕。</summary>
    static void Compose(Graphics g,MainForm form,Form? overlay,(Form Host,PocketSurface Plain,PocketSurface Smooth)? compare,Scene scene,double local,double t) {
        g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        using(var ground=new LinearGradientBrush(new Rectangle(0,0,W,H),Color.FromArgb(240,244,249),Color.FromArgb(219,227,237),90f))g.FillRectangle(ground,0,0,W,H);
        using var main=Snap(form);
        var place=Fit(main.Size,Content);
        if(compare is {} c) {
            // The two LCDs one above the other on a dark card, each with what it shows.
            using(var card=Theme.Rounded(new RectangleF(Content.X,Content.Y,Content.Width,Content.Height),18))using(var dark=new SolidBrush(Color.FromArgb(24,24,28)))g.FillPath(dark,card);
            int x=(W-PanelW)/2,top=Content.Y+50,bottom=top+PanelH+50;
            using var shot=new Bitmap(PanelW,PanelH);
            foreach(var (panel,y,text,tint) in new[]{(c.Plain,top,"フレーム補完なし",Color.FromArgb(230,230,235)),(c.Smooth,bottom,"フレーム補完あり",Color.FromArgb(120,190,255))}) {
                panel.DrawToBitmap(shot,new Rectangle(0,0,PanelW,PanelH));
                g.DrawImageUnscaled(shot,x,y);
                using var b=new SolidBrush(tint);g.DrawString(text,HeadingFont,b,x+4,y-46);
            }
        } else Shadowed(g,main,place);
        if(overlay is not null) {
            using(var dim=new SolidBrush(Color.FromArgb(110,20,24,32)))g.FillRectangle(dim,place);
            using var top=Snap(overlay);
            var area=Fit(top.Size,Rectangle.Inflate(Content,-40,-10));
            Shadowed(g,top,area);
        }
        // Marks on what the scene is about.
        RectangleF? mark=scene.Id switch{
            "look" when local>=1.6=>Map(form,form.TestStatusBar,place,main.Size),
            "load" when local<3.5=>Map(form,FindByText(form,L.T("プログラム読込","Load program")),place,main.Size),
            "smooth" or "keys"=>MapRect(form,form.TestSurface,form.TestSurface.LcdArea(),place,main.Size),
            _=>null};
        if(mark is RectangleF m){using var path=Theme.Rounded(RectangleF.Inflate(m,8,8),10);using var pen=new Pen(Color.FromArgb(230,110,10),6);g.DrawPath(pen,path);}
        if(scene.Id is "title" or "outro") {
            using(var veil=new SolidBrush(Color.FromArgb(200,250,251,253)))g.FillRectangle(veil,0,0,W,H);
            var center=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
            if(scene.Id=="title") {
                using var accent=new SolidBrush(Accent);
                g.DrawString("PokeCom",TitleFont,accent,new RectangleF(0,250,W,140),center);
                using var text=new SolidBrush(Color.FromArgb(40,40,48));
                g.DrawString("PC-E500 エミュレータ",TitleSub,text,new RectangleF(0,390,W,70),center);
                g.DrawString(version.Length>0?$"v{version} の紹介":"新しい版の紹介",TitleSub,text,new RectangleF(0,460,W,70),center);
            } else {
                using var accent=new SolidBrush(Accent);
                g.DrawString(version.Length>0?$"PokeCom v{version} をぜひ使ってみてね":"PokeCom をぜひ使ってみてね",TitleFont,accent,new RectangleF(0,280,W,140),center);
                using var text=new SolidBrush(Color.FromArgb(60,60,70));
                g.DrawString("github.com/litchi-gh/pc-e500_emulator_pockecom",TitleSub,text,new RectangleF(0,430,W,70),center);
                g.DrawString("VOICEVOX:ずんだもん",CreditFont,text,new RectangleF(0,520,W,50),center);
            }
        } else if(scene.Heading.Length>0) {
            using(var bar=Theme.Rounded(new RectangleF(80,44,10,50),5))using(var brush=new SolidBrush(Accent))g.FillPath(brush,bar);
            using var text=new SolidBrush(Color.FromArgb(30,30,36));
            g.DrawString(scene.Heading,HeadingFont,text,new PointF(104,46));
        }
        // The line being spoken, on a dark band at the bottom.
        var line=scene.Lines.FirstOrDefault(l=>local>=l.Start-0.15 && local<l.End+0.25);
        if(line is not null) {
            var size=g.MeasureString(line.Text,SubtitleFont,1600);
            var band=new RectangleF((W-size.Width)/2-36,1035-size.Height-24,size.Width+72,size.Height+24);
            using(var path=Theme.Rounded(band,18))using(var brush=new SolidBrush(Color.FromArgb(200,20,24,32)))g.FillPath(brush,path);
            using var white=new SolidBrush(Color.White);
            g.DrawString(line.Text,SubtitleFont,white,new RectangleF(band.X+36,band.Y+12,size.Width+2,size.Height+4),new StringFormat{Alignment=StringAlignment.Center});
        }
    }
    static void Shadowed(Graphics g,Image image,Rectangle at) {
        for(int i=1;i<=6;i++){using var path=Theme.Rounded(RectangleF.Inflate(new RectangleF(at.X,at.Y+i*2,at.Width,at.Height),i*2,i*2),10);using var b=new SolidBrush(Color.FromArgb(10,0,0,0));g.FillPath(b,path);}
        g.DrawImage(image,at);
    }
    static Rectangle Fit(Size size,Rectangle box) {
        float s=Math.Min((float)box.Width/size.Width,(float)box.Height/size.Height);
        int w=(int)(size.Width*s),h=(int)(size.Height*s);
        return new Rectangle(box.X+(box.Width-w)/2,box.Y+(box.Height-h)/2,w,h);
    }
    /// <summary>コントロールの範囲を、コマの上の位置にする。</summary>
    static RectangleF? Map(Form form,Control? c,Rectangle place,Size snap)=>c is null?null:MapRect(form,c,c.ClientRectangle,place,snap);
    static RectangleF MapRect(Form form,Control c,RectangleF r,Rectangle place,Size snap) {
        var p=c.PointToScreen(new Point((int)r.X,(int)r.Y));
        float s=(float)place.Width/snap.Width;
        return new RectangleF(place.X+(p.X-form.Left)*s,place.Y+(p.Y-form.Top)*s,r.Width*s,r.Height*s);
    }
    static Control? FindByText(Control root,string text) {
        foreach(Control c in root.Controls){if(c.Text==text)return c;if(FindByText(c,text) is {} found)return found;}
        return null;
    }
}
