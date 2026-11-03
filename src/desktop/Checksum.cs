// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// Sums over a range of memory, as printed listings quote them so that a typed-in
// machine language program can be checked against the magazine.
/// <summary>メモリの範囲のチェックサム。</summary>
/// <param name="Length">バイト数</param>
/// <param name="Sum8">8ビットの和</param>
/// <param name="Sum16">16ビットの和</param>
/// <param name="Sum32">32ビットの和</param>
/// <param name="Xor8">全バイトの XOR</param>
internal readonly record struct Checksum(int Length,int Sum8,int Sum16,long Sum32,int Xor8)
{
    /// <summary>バイト列のチェックサムを計算する。</summary>
    internal static Checksum Of(ReadOnlySpan<byte> data) {
        long sum=0;int xor=0;
        foreach(byte b in data){sum+=b;xor^=b;}
        return new Checksum(data.Length,(int)(sum&0xff),(int)(sum&0xffff),sum&0xffffffff,xor);
    }
    // Reads [start,end] from the machine; both ends inclusive, as ranges are quoted.
    /// <summary>コアのメモリの範囲のチェックサムを計算する。</summary>
    /// <param name="core">コア</param>
    /// <param name="start">開始アドレス（含む）</param>
    /// <param name="end">終了アドレス（含む）</param>
    internal static Checksum Of(Core core,int start,int end) {
        if(start<0||start>0xfffff||end<start||end>0xfffff)throw new ArgumentOutOfRangeException(nameof(end),L.T("アドレスが範囲外です。終了アドレスは開始アドレス以上にしてください。","Address out of range. The end address must not be below the start."));
        return Of(core.Dump(start,end-start+1));
    }
}

/// <summary>チェックサムのウィンドウ（デバッガから開く）。</summary>
internal sealed class ChecksumDialog : Form
{
    static readonly string[] Titles=[L.T("範囲","Range"),L.T("バイト数","Bytes"),L.T("合計 (8bit)","Sum (8-bit)"),L.T("合計 (16bit)","Sum (16-bit)"),L.T("合計 (32bit)","Sum (32-bit)"),L.T("XOR (8bit)","XOR (8-bit)")];
    readonly Func<Core?> read;
    readonly TextBox start=new(){Width=90,Font=new Font("Consolas",10)};
    readonly TextBox end=new(){Width=90,Font=new Font("Consolas",10)};
    // Titles in the UI font and values in the hex font, so the columns line up
    // whatever the language.
    readonly TableLayoutPanel table=new(){Location=new Point(14,92),Size=new Size(532,150),ColumnCount=2,RowCount=6,BackColor=Color.White,BorderStyle=BorderStyle.FixedSingle,Padding=new Padding(6,4,6,4)};
    readonly Label[] values=new Label[6];
    readonly Label message=new(){Location=new Point(14,246),AutoSize=true,MaximumSize=new Size(440,0),ForeColor=Color.FromArgb(0,80,160)};
    /// <summary>最後に計算した結果。</summary>
    internal Checksum? Last {get;private set;}
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="read">今のコアを返す関数</param>
    /// <param name="suggestedStart">開始アドレスの初期値</param>
    internal ChecksumDialog(Func<Core?> read,int suggestedStart)
    {
        this.read=read;
        Text=L.T("チェックサム","Checksum");
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterParent;Font=new Font("Yu Gothic UI",10);ClientSize=new Size(560,300);
        var row=new FlowLayoutPanel{Location=new Point(14,14),Size=new Size(536,30),WrapContents=false};
        row.Controls.Add(new Label{Text=L.T("開始 (16進)","Start (hex)"),AutoSize=true,Margin=new Padding(0,6,4,0)});row.Controls.Add(start);
        row.Controls.Add(new Label{Text=L.T("終了 (16進・含む)","End (hex, inclusive)"),AutoSize=true,Margin=new Padding(12,6,4,0)});row.Controls.Add(end);
        Controls.Add(row);
        start.Text=suggestedStart.ToString("X5");end.Text=Math.Min(0xfffff,suggestedStart+0xff).ToString("X5");
        var buttons=new FlowLayoutPanel{Location=new Point(14,50),Size=new Size(536,34),WrapContents=false,Margin=new Padding(0)};
        var compute=new Button{Text=L.T("計算","Compute"),AutoSize=true,Margin=new Padding(0,0,6,0)};
        var copy=new Button{Text=L.T("コピー","Copy"),AutoSize=true,Margin=new Padding(0)};
        buttons.Controls.Add(compute);buttons.Controls.Add(copy);
        var close=new Button{Text=L.T("閉じる","Close"),AutoSize=true,Location=new Point(470,ClientSize.Height-44),DialogResult=DialogResult.Cancel};
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<Titles.Length;i++) {
            table.RowStyles.Add(new RowStyle(SizeType.Percent,100f/Titles.Length));
            table.Controls.Add(new Label{Text=Titles[i],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0)},0,i);
            values[i]=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Consolas",10),Margin=new Padding(0)};
            table.Controls.Add(values[i],1,i);
        }
        Controls.Add(buttons);Controls.Add(close);Controls.Add(table);Controls.Add(message);
        AcceptButton=compute;CancelButton=close;
        compute.Click+=(_,_)=>Compute();
        copy.Click+=(_,_)=>{if(Last is not null)Clipboard.SetText(TestResult);};
        message.Text=L.T("範囲を入力して「計算」を押してください（Enterでも計算します）。","Enter the range and press Compute (or Enter).");
    }
    // Fills the table with the sums, or clears it and says why nothing could be summed.
    /// <summary>入力した範囲を計算して表に出す（計算できなければ理由を出す）。</summary>
    internal void Compute() {
        Last=null;
        foreach(var v in values)v.Text="";
        var core=read();
        if(core is null){message.Text=L.T("ROMが読み込まれていません。","No ROM is loaded.");return;}
        if(!DebugDialog.TryParseAddress(start.Text,out int from)||!DebugDialog.TryParseAddress(end.Text,out int to)){message.Text=L.T("アドレスは16進で入力してください。","Enter both addresses in hex.");return;}
        Checksum sum;
        try {sum=Checksum.Of(core,from,to);}
        catch(Exception ex){message.Text=ex.Message;return;}
        Last=sum;message.Text="";
        values[0].Text=$"{from:X5}-{to:X5}  ({MemoryMap.Name(core,from)})";
        values[1].Text=$"{sum.Length:X}h ({sum.Length})";
        values[2].Text=$"{sum.Sum8:X2}h ({sum.Sum8})";
        values[3].Text=$"{sum.Sum16:X4}h ({sum.Sum16})";
        values[4].Text=$"{sum.Sum32:X8}h ({sum.Sum32})";
        values[5].Text=$"{sum.Xor8:X2}h ({sum.Xor8})";
    }
    internal void TestSet(string from,string to){start.Text=from;end.Text=to;}
    // The table as text, one "title  value" line each, for the clipboard and the tests.
    internal string TestResult=>string.Join(Environment.NewLine,Titles.Select((t,i)=>$"{t}	{values[i].Text}"));
}
