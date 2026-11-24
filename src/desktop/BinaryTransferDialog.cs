// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>BIN の読込・保存のウィンドウ。</summary>
internal sealed class BinaryTransferDialog : ThemedForm
{
    readonly bool saving;
    readonly Func<Core?> read;
    readonly TextBox file=new(){Width=300};
    readonly TextBox address=new(){Width=90,Font=Fonts.Mono};
    readonly TextBox length=new(){Width=90,Font=Fonts.Mono};
    readonly CheckBox header=new(){AutoSize=true};
    readonly NoteLabel info=new("",500);
    byte[]? loaded;
    // Set when the transfer succeeded: where it started, so the caller can show it.
    /// <summary>成功したときの先頭アドレス（呼び出し元がその位置を表示する）。</summary>
    internal int? Done {get;private set;}
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="read">今のコアを返す関数</param>
    /// <param name="saving">保存なら true、読込なら false</param>
    /// <param name="suggestedAddress">アドレス欄の初期値</param>
    internal BinaryTransferDialog(Func<Core?> read,bool saving,int suggestedAddress)
    {
        this.read=read;this.saving=saving;
        Text=saving?L.T("BIN書出し (メモリ → ファイル)","Save BIN (memory → file)"):L.T("BIN書込み (ファイル → メモリ)","Load BIN (file → memory)");
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterParent;Font=Fonts.Ui;ClientSize=new Size(572,10);
        var body=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16,16,16,8)};
        var card=new CardPanel{Margin=new Padding(0)};var stack=card.Stack(516);body.Controls.Add(card);
        var browse=new FluentButton{Text=L.T("参照","Browse"),AutoSize=true};
        browse.Click+=(_,_)=>Browse();
        stack.Controls.Add(Row(L.T("ファイル","File"),110,file,browse));
        // The bytes held belong to the path they came from: a path typed or pasted afterwards is read afresh.
        file.TextChanged+=(_,_)=>loaded=null;
        address.Text=suggestedAddress.ToString("X5");
        stack.Controls.Add(Row(L.T("アドレス (16進)","Address (hex)"),110,address));
        if(saving) {
            length.Text="100";stack.Controls.Add(Row(L.T("長さ (16進)","Length (hex)"),110,length));
            header.Text=L.T("SAVEMヘッダー(16バイト)を付けて書き出す","Write with a 16-byte SAVEM header");header.Checked=true;
        } else {
            header.Text=L.T("SAVEMヘッダーを除き、ヘッダーの開始アドレスへ書き込む","Strip the SAVEM header and load at its start address");header.Enabled=false;
            header.CheckedChanged+=(_,_)=>address.Enabled=!header.Checked;
        }
        header.Margin=new Padding(0,6,0,2);stack.Controls.Add(header);stack.Controls.Add(info);
        var footer=new FooterBar();
        var cancel=footer.Add(L.T("キャンセル","Cancel"),null,DialogResult.Cancel);
        var ok=footer.Add(saving?L.T("書出し","Save"):L.T("書込み","Load"));
        Controls.Add(body);Controls.Add(footer);AcceptButton=ok;CancelButton=cancel;
        Load+=(_,_)=>FitHeight(body,footer);
        ok.Click+=(_,_)=>{try{Run();}catch(Exception ex){MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}};
        info.Text=saving?L.T("指定した範囲のメモリをそのままファイルへ書き出します。","Writes the selected memory range to a file as it is."):L.T("ファイルの内容を指定アドレスから書き込みます。内蔵RAM・RAMカードの範囲のみ書き込めます。","Loads the file at the given address. Only internal RAM and the RAM card can be written.");
    }
    /// <summary>ファイルを選ぶ。</summary>
    void Browse() {
        if(saving) {
            using var d=new SaveFileDialog{Filter=L.T("バイナリ (*.bin)|*.bin|すべてのファイル|*.*","Binary (*.bin)|*.bin|All files|*.*"),FileName="MEMORY.BIN"};
            if(d.ShowDialog(this)==DialogResult.OK)file.Text=d.FileName;
        } else {
            using var d=new OpenFileDialog{Filter=L.T("バイナリ (*.bin;*.ram;*.rom)|*.bin;*.ram;*.rom|すべてのファイル|*.*","Binary (*.bin;*.ram;*.rom)|*.bin;*.ram;*.rom|All files|*.*")};
            if(d.ShowDialog(this)==DialogResult.OK) {
                file.Text=d.FileName;
                // A file that cannot be read says so here, not as an unhandled exception.
                try {Inspect();} catch(Exception ex) {loaded=null;info.Text=L.T("読み込めません: ","Could not read the file: ")+ex.Message;}
            }
        }
    }
    // Describes the chosen file and offers its SAVEM load address when it has one.
    /// <summary>選んだファイルの大きさと SAVEM ヘッダーを調べて表示する。</summary>
    internal void Inspect() {
        loaded=null;
        if(!File.Exists(file.Text)){info.Text=L.T("ファイルが見つかりません。","File not found.");return;}
        long size=new FileInfo(file.Text).Length;
        if(size>0x100000+HeaderLength){info.Text=L.T("1MBを超えるファイルは書き込めません。","Files larger than 1MB cannot be loaded.");return;}
        loaded=File.ReadAllBytes(file.Text);
        if(BinaryTransfer.TryReadHeader(loaded,out int start,out int length)) {
            header.Enabled=true;header.Checked=true;address.Text=start.ToString("X5");
            info.Text=L.T($"{loaded.Length}バイト。SAVEMヘッダーを検出: 開始 {start:X5}、長さ {length:X}h ({length}バイト)。",$"{loaded.Length} bytes. SAVEM header found: start {start:X5}, length {length:X}h ({length} bytes).");
        } else {
            header.Enabled=false;header.Checked=false;
            info.Text=L.T($"{loaded.Length}バイト ({loaded.Length:X}h)。ヘッダーなし: アドレス欄の位置から書き込みます。",$"{loaded.Length} bytes ({loaded.Length:X}h). No header: loads at the address given above.");
        }
    }
    const int HeaderLength=BinaryTransfer.HeaderLength;
    /// <summary>読込・保存を実行し、成功したらウィンドウを閉じる。</summary>
    void Run() {
        var core=read()??throw new InvalidOperationException(L.T("ROMが読み込まれていません。","No ROM is loaded."));
        if(!DebugDialog.TryParseAddress(address.Text,out int at))throw new FormatException(L.T("アドレスは16進で入力してください。","Enter the address in hex."));
        if(saving) {
            if(!DebugDialog.TryParseAddress(length.Text,out int count))throw new FormatException(L.T("長さは16進で入力してください。","Enter the length in hex."));
            if(string.IsNullOrWhiteSpace(file.Text))throw new InvalidOperationException(L.T("書き出すファイルを指定してください。","Choose the file to save to."));
            AtomicFile.Write(file.Text,BinaryTransfer.Save(core,at,count,header.Checked));
            Done=at;
        } else {
            if(loaded is null)Inspect();
            if(loaded is null)throw new FileNotFoundException(L.T("読み込むファイルを指定してください。","Choose the file to load."));
            var result=BinaryTransfer.Load(core,loaded,at,header.Checked);
            Done=result.Address;
        }
        DialogResult=DialogResult.OK;Close();
    }
    internal void TestSet(string path,string at,bool useHeader,string? count=null) {
        file.Text=path;if(!saving)Inspect();
        address.Text=at;header.Checked=useHeader&&header.Enabled;if(count is not null)length.Text=count;
    }
    internal void TestRun()=>Run();
}
