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
internal sealed class DisplayDialog : ThemedForm
{
    readonly RadioButton packed=new(){Text=L.T("標準（ドットを詰めて表示）","Standard (dots packed together)"),AutoSize=true};
    readonly RadioButton real=new(){Text=L.T("リアル（実機の液晶のようにドットの間をあける）","Real (dots apart, as on the real LCD)"),AutoSize=true};
    // A check box does not wrap its text, so the explanation is a label of its own.
    readonly CheckBox afterimage=new(){Text=L.T("残像（フレーム補完）","Afterimage (frame interpolation)"),AutoSize=true,Margin=new Padding(3,6,3,0)};
    readonly TrackBar contrast=new(){Minimum=0,Maximum=100,TickFrequency=10,SmallChange=1,LargeChange=10,Width=250};
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
    /// <summary>残像（フレーム補完）を切り替えたときに呼ぶ処理。</summary>
    internal Action<bool>? AfterimageChanged;
    /// <summary>ウィンドウの表示が選ばれたときに呼ぶ処理。</summary>
    internal Action<WindowView>? ViewChanged;
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="realLcd">今のリアル表示</param>
    /// <param name="lcdContrast">今のコントラスト（0〜100）</param>
    /// <param name="view">今のウィンドウの表示</param>
    /// <param name="lcdAfterimage">今の残像（フレーム補完）</param>
    internal DisplayDialog(bool realLcd,int lcdContrast,WindowView view,bool lcdAfterimage)
    {
        Text=L.T("画面設定","Display");FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;
        StartPosition=FormStartPosition.CenterScreen;Font=Fonts.Ui;ClientSize=new Size(520,10);
        var body=new FlowLayoutPanel{Dock=DockStyle.Top,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,Padding=new Padding(16,16,16,8)};
        FlowLayoutPanel Card(string title){var card=new CardPanel{Title=title,Margin=new Padding(0,0,0,10)};body.Controls.Add(card);return card.Stack(464);}
        var lcd=Card(L.T("液晶の表示","LCD"));
        lcd.Controls.Add(packed);lcd.Controls.Add(real);lcd.Controls.Add(afterimage);
        lcd.Controls.Add(new NoteLabel(L.T("1ドットずつ動くものをなめらかに滑らせ、ちらつきも抑えます。","Things moving a dot at a time slide smoothly, without flicker."),440){Margin=new Padding(22,0,0,0)});
        var dark=Card(L.T("液晶のコントラスト","LCD contrast"));
        var row=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(0),BackColor=Theme.Card};
        var reset=new FluentButton{Text=L.T("標準に戻す","Default"),AutoSize=true,Margin=new Padding(10,6,0,0)};
        row.Controls.Add(contrast);row.Controls.Add(value);row.Controls.Add(reset);dark.Controls.Add(row);
        dark.Controls.Add(new NoteLabel(L.T("ドットと表示シンボルの濃さが一緒に変わります（実機のつまみと同じ）。","The dots and legends darken together, as with the real dial."),440));
        var window=Card(L.T("ウィンドウの表示","Window"));
        foreach(var v in views)window.Controls.Add(v);
        window.Controls.Add(new NoteLabel(L.T("本体のキーのない所をダブルクリックしても切り替わります。右クリックでメニュー。","A double-click on the case off the keys switches too. Right-click for the menu."),440));
        var footer=new FooterBar();
        var close=footer.Add(L.T("閉じる","Close"),(_,_)=>Close());
        Controls.Add(body);Controls.Add(footer);CancelButton=close;
        Load+=(_,_)=>FitHeight(body,footer);
        Sync(realLcd,lcdContrast,view,lcdAfterimage);
        afterimage.CheckedChanged+=(_,_)=>{if(!syncing)AfterimageChanged?.Invoke(afterimage.Checked);};
        void Lcd(){value.Text=contrast.Value.ToString();if(!syncing)LcdChanged?.Invoke(real.Checked,contrast.Value);}
        packed.CheckedChanged+=(_,_)=>{if(packed.Checked)Lcd();};real.CheckedChanged+=(_,_)=>{if(real.Checked)Lcd();};
        contrast.ValueChanged+=(_,_)=>Lcd();
        reset.Click+=(_,_)=>contrast.Value=PocketSurface.DefaultContrast;
        for(int i=0;i<views.Length;i++){var v=(WindowView)i;views[i].CheckedChanged+=(s,_)=>{if(!syncing && ((RadioButton)s!).Checked)ViewChanged?.Invoke(v);};}
    }
    /// <summary>表示を今の値に合わせる（メインウィンドウ側で変わったとき）。変更は知らせない。</summary>
    internal void Sync(bool realLcd,int lcdContrast,WindowView view,bool lcdAfterimage) {
        syncing=true;
        try {
            (realLcd?real:packed).Checked=true;afterimage.Checked=lcdAfterimage;contrast.Value=Math.Clamp(lcdContrast,0,100);value.Text=contrast.Value.ToString();
            views[(int)view].Checked=true;
        } finally {syncing=false;}
    }
    // For the smoke test: the controls as a user would set them.
    internal void TestSet(bool realLcd,int lcdContrast){(realLcd?real:packed).Checked=true;contrast.Value=lcdContrast;}
    internal void TestView(WindowView view)=>views[(int)view].Checked=true;
    internal void TestAfterimage(bool on)=>afterimage.Checked=on;
}
