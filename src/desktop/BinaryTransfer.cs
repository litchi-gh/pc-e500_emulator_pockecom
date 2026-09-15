// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

// Reads a binary file straight into RAM at an address, or writes a range of
// memory out to one. A PC-E500 SAVE M file starts with a 16-byte header that
// names its own load address, so such a file can go back where it came from.
internal static class BinaryTransfer
{
    internal const int HeaderLength=16;
    // FF 00 06 01 10 <length:3> <start:3> ... as CE-140F SAVE M writes it.
    internal static bool TryReadHeader(byte[] file,out int start,out int length) {
        start=length=0;
        if(file.Length<HeaderLength || file[0]!=0xff || file[1]!=0 || file[2]!=6 || file[3]!=1 || file[4]!=0x10)return false;
        length=file[5]|file[6]<<8|file[7]<<16;start=file[8]|file[9]<<8|file[10]<<16;
        return length>0 && length==file.Length-HeaderLength && start<=0xfffff;
    }
    // Writes the file to the machine; returns where it went and how many bytes.
    internal static (int Address,int Length) Load(Core core,byte[] file,int address,bool useHeader) {
        byte[] payload=file;
        if(useHeader) {
            if(!TryReadHeader(file,out int start,out _))throw new InvalidDataException(L.T("SAVEMヘッダーがありません。","The file has no SAVEM header."));
            address=start;payload=file.AsSpan(HeaderLength).ToArray();
        }
        if(payload.Length==0)throw new InvalidDataException(L.T("ファイルが空です。","The file is empty."));
        if(address<0||address>0xfffff||payload.Length>0x100000-address)throw new ArgumentOutOfRangeException(nameof(address),L.T("アドレスが範囲外です。","Address out of range."));
        core.Write(address,payload);
        return (address,payload.Length);
    }
    internal static byte[] Save(Core core,int address,int length,bool withHeader) {
        if(length<=0||address<0||address>0xfffff||length>0x100000-address)throw new ArgumentOutOfRangeException(nameof(length),L.T("アドレスまたは長さが範囲外です。","Address or length out of range."));
        byte[] data=core.Dump(address,length);
        if(!withHeader)return data;
        byte[] file=new byte[HeaderLength+length];
        file[0]=0xff;file[1]=0;file[2]=6;file[3]=1;file[4]=0x10;
        file[5]=(byte)length;file[6]=(byte)(length>>8);file[7]=(byte)(length>>16);
        file[8]=(byte)address;file[9]=(byte)(address>>8);file[10]=(byte)(address>>16);
        file[11]=file[12]=file[13]=0xff;file[14]=0;file[15]=0x0f;
        data.CopyTo(file,HeaderLength);
        return file;
    }
}

internal sealed class BinaryTransferDialog : Form
{
    readonly bool saving;
    readonly Func<Core?> read;
    readonly TextBox file=new(){Width=360};
    readonly TextBox address=new(){Width=90,Font=new Font("Consolas",10)};
    readonly TextBox length=new(){Width=90,Font=new Font("Consolas",10)};
    readonly CheckBox header=new(){AutoSize=true};
    readonly Label info=new(){AutoSize=true,MaximumSize=new Size(470,0)};
    byte[]? loaded;
    // Set when the transfer succeeded: where it started, so the caller can show it.
    internal int? Done {get;private set;}
    internal BinaryTransferDialog(Func<Core?> read,bool saving,int suggestedAddress)
    {
        this.read=read;this.saving=saving;
        Text=saving?L.T("BIN書出し (メモリ → ファイル)","Save BIN (memory → file)"):L.T("BIN書込み (ファイル → メモリ)","Load BIN (file → memory)");
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterParent;Font=new Font("Yu Gothic UI",10);ClientSize=new Size(500,saving?230:250);
        int y=14;
        Control Row(string title,Control c,int rowHeight=30) {
            var r=new FlowLayoutPanel{Location=new Point(14,y),Size=new Size(476,rowHeight),WrapContents=false};
            r.Controls.Add(new Label{Text=title,Width=100,TextAlign=ContentAlignment.MiddleLeft});r.Controls.Add(c);
            Controls.Add(r);y+=rowHeight+2;return r;
        }
        var fileRow=(FlowLayoutPanel)Row(L.T("ファイル","File"),file);
        var browse=new Button{Text=L.T("参照","Browse"),AutoSize=true};fileRow.Controls.Add(browse);
        browse.Click+=(_,_)=>Browse();
        address.Text=suggestedAddress.ToString("X5");
        Row(L.T("アドレス (16進)","Address (hex)"),address);
        if(saving) {
            length.Text="100";Row(L.T("長さ (16進)","Length (hex)"),length);
            header.Text=L.T("SAVEMヘッダー(16バイト)を付けて書き出す","Write with a 16-byte SAVEM header");header.Checked=true;
        } else {
            header.Text=L.T("SAVEMヘッダーを除き、ヘッダーの開始アドレスへ書き込む","Strip the SAVEM header and load at its start address");header.Enabled=false;
            header.CheckedChanged+=(_,_)=>address.Enabled=!header.Checked;
        }
        header.Location=new Point(14,y);Controls.Add(header);y+=30;
        info.Location=new Point(14,y);Controls.Add(info);
        var ok=new Button{Text=saving?L.T("書出し","Save"):L.T("書込み","Load"),AutoSize=true,Location=new Point(320,ClientSize.Height-44)};
        var cancel=new Button{Text=L.T("キャンセル","Cancel"),AutoSize=true,Location=new Point(410,ClientSize.Height-44),DialogResult=DialogResult.Cancel};
        Controls.Add(ok);Controls.Add(cancel);AcceptButton=ok;CancelButton=cancel;
        ok.Click+=(_,_)=>{try{Run();}catch(Exception ex){MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}};
        info.Text=saving?L.T("指定した範囲のメモリをそのままファイルへ書き出します。","Writes the selected memory range to a file as it is."):L.T("ファイルの内容を指定アドレスから書き込みます。内蔵RAM・RAMカードの範囲のみ書き込めます。","Loads the file at the given address. Only internal RAM and the RAM card can be written.");
    }
    void Browse() {
        if(saving) {
            using var d=new SaveFileDialog{Filter=L.T("バイナリ (*.bin)|*.bin|すべてのファイル|*.*","Binary (*.bin)|*.bin|All files|*.*"),FileName="MEMORY.BIN"};
            if(d.ShowDialog(this)==DialogResult.OK)file.Text=d.FileName;
        } else {
            using var d=new OpenFileDialog{Filter=L.T("バイナリ (*.bin;*.ram;*.rom)|*.bin;*.ram;*.rom|すべてのファイル|*.*","Binary (*.bin;*.ram;*.rom)|*.bin;*.ram;*.rom|All files|*.*")};
            if(d.ShowDialog(this)==DialogResult.OK){file.Text=d.FileName;Inspect();}
        }
    }
    // Describes the chosen file and offers its SAVEM load address when it has one.
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
