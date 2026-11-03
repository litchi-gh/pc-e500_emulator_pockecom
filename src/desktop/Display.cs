// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// What the main window shows: everything, the case alone with no window frame
// around it, or the glass alone. A double-click on the case steps through them.
/// <summary>メインウィンドウの表示（ダブルクリックで順に切り替わる）。</summary>
internal enum WindowView
{
    /// <summary>タイトル・ボタン・案内・ステータスを含む、いつもの表示。</summary>
    Normal,
    /// <summary>ウィンドウの枠や帯のない、本体の絵だけ。</summary>
    Body,
    /// <summary>本体の絵もない、LCD の部分だけ。</summary>
    Screen
}

// Modeless, and every change shows at once on the machine; the main window
// keeps the values and writes them out when this closes.
/// <summary>「画面」ウィンドウ（LCD のリアル表示・コントラスト・ウィンドウの表示）。変更はすぐに反映する。</summary>
internal sealed class DisplayDialog : Form
{
    readonly RadioButton packed=new(){Text=L.T("標準（ドットを詰めて表示）","Standard (dots packed together)"),AutoSize=true};
    readonly RadioButton real=new(){Text=L.T("リアル（実機の液晶のようにドットの間をあける）","Real (dots apart, as on the real LCD)"),AutoSize=true};
    readonly TrackBar contrast=new(){Minimum=0,Maximum=100,TickFrequency=10,SmallChange=1,LargeChange=10,Width=330};
    readonly Label value=new(){AutoSize=true,Margin=new Padding(6,12,0,0)};
    readonly RadioButton[] views=[
        new(){Text=L.T("通常（タイトル・ボタンを表示）","Normal (title and buttons)"),AutoSize=true},
        new(){Text=L.T("本体だけ","Case only"),AutoSize=true},
        new(){Text=L.T("画面だけ","Screen only"),AutoSize=true}
    ];
    // Set while the controls are being filled in, so that doing so reports nothing back.
    bool syncing;
    /// <summary>LCD の表示が変わったときに呼ぶ処理（リアル表示か、コントラスト）。</summary>
    internal Action<bool,int>? LcdChanged;
    /// <summary>ウィンドウの表示が選ばれたときに呼ぶ処理。</summary>
    internal Action<WindowView>? ViewChanged;
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="realLcd">今のリアル表示</param>
    /// <param name="lcdContrast">今のコントラスト（0〜100）</param>
    /// <param name="view">今のウィンドウの表示</param>
    internal DisplayDialog(bool realLcd,int lcdContrast,WindowView view)
    {
        Text=L.T("画面設定","Display");FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;
        StartPosition=FormStartPosition.CenterScreen;Font=new Font("Yu Gothic UI",10);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        var panel=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Padding=new Padding(16)};Controls.Add(panel);
        Label Heading(string text)=>new(){Text=text,AutoSize=true,Font=new Font(Font,FontStyle.Bold),Margin=new Padding(0,8,0,4)};
        panel.Controls.Add(Heading(L.T("液晶の表示","LCD")));panel.Controls.Add(packed);panel.Controls.Add(real);
        panel.Controls.Add(Heading(L.T("液晶のコントラスト","LCD contrast")));
        var row=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(0)};
        var reset=new Button{Text=L.T("標準に戻す","Default"),AutoSize=true,Margin=new Padding(10,6,0,0)};
        row.Controls.Add(contrast);row.Controls.Add(value);row.Controls.Add(reset);panel.Controls.Add(row);
        panel.Controls.Add(new Label{Text=L.T("ドットと表示シンボルの濃さが一緒に変わります。下げると薄くなり、消えている表示シンボルは見えなくなります。上げると濃くなり、消えているドットやシンボルも見えてきて、いちばん上では全体が黒くなります（実機と同じ）。","The dots and the legends darken together. Lower is fainter, and the legends that are off disappear; higher is darker, the dots and legends that are off begin to show, and at the top the whole glass is black, as on the real unit."),AutoSize=true,MaximumSize=new Size(460,0)});
        panel.Controls.Add(Heading(L.T("ウィンドウの表示","Window")));
        var group=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,AutoSize=true,Margin=new Padding(0)};
        foreach(var v in views)group.Controls.Add(v);panel.Controls.Add(group);
        panel.Controls.Add(new Label{Text=L.T("本体のキーのない所をダブルクリックしても、順に切り替わります（画面だけのときは通常に戻ります）。右クリックでメニューを出せます。","A double-click on the case away from the keys steps through these too (from screen only, back to normal). Right-click for the menu."),AutoSize=true,MaximumSize=new Size(460,0)});
        var close=new Button{Text=L.T("閉じる","Close"),AutoSize=true,Margin=new Padding(0,14,0,0)};close.Click+=(_,_)=>Close();
        panel.Controls.Add(close);CancelButton=close;
        Sync(realLcd,lcdContrast,view);
        void Lcd(){value.Text=contrast.Value.ToString();if(!syncing)LcdChanged?.Invoke(real.Checked,contrast.Value);}
        packed.CheckedChanged+=(_,_)=>{if(packed.Checked)Lcd();};real.CheckedChanged+=(_,_)=>{if(real.Checked)Lcd();};
        contrast.ValueChanged+=(_,_)=>Lcd();
        reset.Click+=(_,_)=>contrast.Value=PocketSurface.DefaultContrast;
        for(int i=0;i<views.Length;i++){var v=(WindowView)i;views[i].CheckedChanged+=(s,_)=>{if(!syncing && ((RadioButton)s!).Checked)ViewChanged?.Invoke(v);};}
    }
    /// <summary>表示を今の値に合わせる（メインウィンドウ側で変わったとき）。変更は知らせない。</summary>
    internal void Sync(bool realLcd,int lcdContrast,WindowView view) {
        syncing=true;
        try {
            (realLcd?real:packed).Checked=true;contrast.Value=Math.Clamp(lcdContrast,0,100);value.Text=contrast.Value.ToString();
            views[(int)view].Checked=true;
        } finally {syncing=false;}
    }
    // For the smoke test: the controls as a user would set them.
    internal void TestSet(bool realLcd,int lcdContrast){(realLcd?real:packed).Checked=true;contrast.Value=lcdContrast;}
    internal void TestView(WindowView view)=>views[(int)view].Checked=true;
}
