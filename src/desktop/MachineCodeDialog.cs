// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// The map shows where the area is now and, while another size is set, where
// everything will be once it is applied; the text is a line or two.
// 今の並びと、大きさを変えたときの並びを帯で見せる。説明は短く。
/// <summary>機械語エリアの大きさを決めるウィンドウ。</summary>
internal sealed class MachineCodeDialog : ThemedForm
{
    readonly NumericUpDown size=new(){Hexadecimal=true,Minimum=0,Increment=0x100,Width=110,Font=Fonts.Mono};
    readonly Label bytes=new(){AutoSize=true,Margin=new Padding(8,7,0,0)};
    readonly MemoryMapView now=new(){Emphasis=RegionKind.MachineCode,Legend=false},after=new(){Emphasis=RegionKind.MachineCode};
    readonly Label afterTitle=new(){AutoSize=true,Font=Fonts.UiBold,Margin=new Padding(0,10,0,4)};
    readonly List<Region> layout;
    readonly int current;
    /// <summary>「適用」で呼ぶ処理（新しい大きさ）。false ならウィンドウを閉じない。</summary>
    internal Func<int,bool>? Apply;
    /// <param name="layout">今の内蔵 RAM の並び</param>
    /// <param name="current">今の大きさ（バイト）</param>
    /// <param name="largest">選べるいちばん大きい値（今の大きさ＋空き）</param>
    internal MachineCodeDialog(List<Region> layout,int current,int largest)
    {
        this.layout=layout;this.current=current;
        Text=L.T("機械語エリア","Machine-code area");FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterParent;Font=Fonts.Ui;ClientSize=new Size(620,10);
        var body=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16,16,16,8)};
        var card=new CardPanel{Title=L.T("内蔵 RAM の並び","Internal RAM"),Width=588,Height=10,Margin=new Padding(0)};
        var inner=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Theme.Card};
        card.Controls.Add(inner);
        now.Width=after.Width=card.Width-card.Padding.Horizontal;now.Margin=after.Margin=new Padding(0);
        inner.Controls.Add(new Label{Text=L.T("今","Now"),AutoSize=true,Font=Fonts.UiBold,Margin=new Padding(0,0,0,4)});
        inner.Controls.Add(now);inner.Controls.Add(afterTitle);inner.Controls.Add(after);
        body.Controls.Add(card);
        var row=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(0,14,0,0)};
        row.Controls.Add(new Label{Text=L.T("大きさ（16進）","Size (hex)"),AutoSize=true,Margin=new Padding(0,7,8,0)});
        size.Maximum=Math.Max(current,largest);size.Value=current;row.Controls.Add(size);row.Controls.Add(bytes);
        body.Controls.Add(row);
        body.Controls.Add(new NoteLabel(L.T($"BFBFF から下に取ります。0 で返します（最大 {largest:X}h）。BASIC の RUN・PRO の表示で使います。プログラムと変数は残ります。",
            $"Taken below BFBFF; 0 gives it back (at most {largest:X}h). Use it at BASIC's RUN or PRO prompt; the program and variables stay."),588));
        var footer=new FooterBar();
        closeButton=footer.Add(L.T("閉じる","Close"),(_,_)=>Close());
        var ok=footer.Add(L.T("適用","Apply"),(_,_)=>{if(Apply is null || Apply((int)size.Value))Close();});
        AcceptButton=ok;
        Controls.Add(body);Controls.Add(footer);
        size.ValueChanged+=(_,_)=>UpdateView();
        Load+=(_,_)=>{
            now.SetRegions(layout);UpdateView();
            inner.PerformLayout();card.Height=card.Padding.Vertical+inner.Controls.Cast<Control>().Sum(c=>c.Height+c.Margin.Vertical)+4;
            ClientSize=new Size(ClientSize.Width,body.PreferredSize.Height+footer.PreferredSize.Height);
        };
        // Escape closes, as Close does.
        KeyPreview=true;KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape){e.Handled=true;Close();}};
    }
    /// <summary>大きさの表示と、適用後の帯を新しい大きさに合わせる。</summary>
    void UpdateView() {
        int value=(int)size.Value;
        bytes.Text=L.T($"= {value:N0} バイト（{RamLayout.Size(value)}）",$"= {value:N0} bytes ({RamLayout.Size(value)})");
        afterTitle.Text=value==current?L.T("適用後（変わりません）","After (no change)")
            :value==0?L.T("適用後（機械語エリアなし）","After (no machine-code area)")
            :L.T($"適用後（機械語エリア {0xbfc00-value:X5}〜BFBFF）",$"After (machine code {0xbfc00-value:X5}-BFBFF)");
        after.SetRegions(RamLayout.WithMachineCode(layout,value));
        now.Height=now.PreferredHeight;after.Height=after.PreferredHeight;
    }
    internal void TestSet(int value)=>size.Value=value;
    readonly FluentButton closeButton;
    /// <summary>テスト用：「閉じる」を押す。</summary>
    internal void TestClose()=>closeButton.PerformClick();
}
