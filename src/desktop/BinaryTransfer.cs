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
            if(!TryReadHeader(file,out int start,out _))throw new InvalidDataException("SAVEMヘッダーがありません。");
            address=start;payload=file.AsSpan(HeaderLength).ToArray();
        }
        if(payload.Length==0)throw new InvalidDataException("ファイルが空です。");
        if(address<0||address>0xfffff||payload.Length>0x100000-address)throw new ArgumentOutOfRangeException(nameof(address),"アドレスが範囲外です。");
        core.Write(address,payload);
        return (address,payload.Length);
    }
    internal static byte[] Save(Core core,int address,int length,bool withHeader) {
        if(length<=0||address<0||address>0xfffff||length>0x100000-address)throw new ArgumentOutOfRangeException(nameof(length),"アドレスまたは長さが範囲外です。");
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
        Text=saving?"BIN書出し (メモリ → ファイル)":"BIN書込み (ファイル → メモリ)";
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterParent;Font=new Font("Yu Gothic UI",10);ClientSize=new Size(500,saving?230:250);
        int y=14;
        Control Row(string title,Control c,int rowHeight=30) {
            var r=new FlowLayoutPanel{Location=new Point(14,y),Size=new Size(476,rowHeight),WrapContents=false};
            r.Controls.Add(new Label{Text=title,Width=100,TextAlign=ContentAlignment.MiddleLeft});r.Controls.Add(c);
            Controls.Add(r);y+=rowHeight+2;return r;
        }
        var fileRow=(FlowLayoutPanel)Row("ファイル",file);
        var browse=new Button{Text="参照",AutoSize=true};fileRow.Controls.Add(browse);
        browse.Click+=(_,_)=>Browse();
        address.Text=suggestedAddress.ToString("X5");
        Row("アドレス (16進)",address);
        if(saving) {
            length.Text="100";Row("長さ (16進)",length);
            header.Text="SAVEMヘッダー(16バイト)を付けて書き出す";header.Checked=true;
        } else {
            header.Text="SAVEMヘッダーを除き、ヘッダーの開始アドレスへ書き込む";header.Enabled=false;
            header.CheckedChanged+=(_,_)=>address.Enabled=!header.Checked;
        }
        header.Location=new Point(14,y);Controls.Add(header);y+=30;
        info.Location=new Point(14,y);Controls.Add(info);
        var ok=new Button{Text=saving?"書出し":"書込み",AutoSize=true,Location=new Point(320,ClientSize.Height-44)};
        var cancel=new Button{Text="キャンセル",AutoSize=true,Location=new Point(410,ClientSize.Height-44),DialogResult=DialogResult.Cancel};
        Controls.Add(ok);Controls.Add(cancel);AcceptButton=ok;CancelButton=cancel;
        ok.Click+=(_,_)=>{try{Run();}catch(Exception ex){MessageBox.Show(this,ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}};
        info.Text=saving?"指定した範囲のメモリをそのままファイルへ書き出します。":"ファイルの内容を指定アドレスから書き込みます。内蔵RAM・RAMカードの範囲のみ書き込めます。";
    }
    void Browse() {
        if(saving) {
            using var d=new SaveFileDialog{Filter="バイナリ (*.bin)|*.bin|すべてのファイル|*.*",FileName="MEMORY.BIN"};
            if(d.ShowDialog(this)==DialogResult.OK)file.Text=d.FileName;
        } else {
            using var d=new OpenFileDialog{Filter="バイナリ (*.bin;*.ram;*.rom)|*.bin;*.ram;*.rom|すべてのファイル|*.*"};
            if(d.ShowDialog(this)==DialogResult.OK){file.Text=d.FileName;Inspect();}
        }
    }
    // Describes the chosen file and offers its SAVEM load address when it has one.
    internal void Inspect() {
        loaded=null;
        if(!File.Exists(file.Text)){info.Text="ファイルが見つかりません。";return;}
        long size=new FileInfo(file.Text).Length;
        if(size>0x100000+HeaderLength){info.Text="1MBを超えるファイルは書き込めません。";return;}
        loaded=File.ReadAllBytes(file.Text);
        if(BinaryTransfer.TryReadHeader(loaded,out int start,out int length)) {
            header.Enabled=true;header.Checked=true;address.Text=start.ToString("X5");
            info.Text=$"{loaded.Length}バイト。SAVEMヘッダーを検出: 開始 {start:X5}、長さ {length:X}h ({length}バイト)。";
        } else {
            header.Enabled=false;header.Checked=false;
            info.Text=$"{loaded.Length}バイト ({loaded.Length:X}h)。ヘッダーなし: アドレス欄の位置から書き込みます。";
        }
    }
    const int HeaderLength=BinaryTransfer.HeaderLength;
    void Run() {
        var core=read()??throw new InvalidOperationException("ROMが読み込まれていません。");
        if(!DebugDialog.TryParseAddress(address.Text,out int at))throw new FormatException("アドレスは16進で入力してください。");
        if(saving) {
            if(!DebugDialog.TryParseAddress(length.Text,out int count))throw new FormatException("長さは16進で入力してください。");
            if(string.IsNullOrWhiteSpace(file.Text))throw new InvalidOperationException("書き出すファイルを指定してください。");
            AtomicFile.Write(file.Text,BinaryTransfer.Save(core,at,count,header.Checked));
            Done=at;
        } else {
            if(loaded is null)Inspect();
            if(loaded is null)throw new FileNotFoundException("読み込むファイルを指定してください。");
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
