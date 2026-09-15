// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text.Json;
namespace PokeCom;

// CardPath names the RAM card image file in the slot ("" for an empty slot).
// The file's size is the card's capacity.
internal sealed record HardwareSettings(int InternalKB=32,string CardPath="",bool DiskEnabled=true,bool DiskReadOnly=false,string DiskFolder="",bool Muted=false,int Volume=50,bool LcdDotLines=false,string Appearance="PC-E500",string Language="")
{
    internal static readonly int[] Sizes=[8,16,32,64,128,256];
    // The stock E500 carries one 62256 (32KB) and the E650/U6000 two of them (64KB,
    // B0000-BFFFF). Replacing each 32KB SRAM with a 128KB 628128 is the well-known
    // expansion mod, so an E650/U6000 reaches 128KB or 256KB (80000-BFFFF) instead.
    internal static int[] InternalSizes(int romKB)=>romKB==384?[64,128,256]:Sizes;
    internal static int CoerceInternal(int romKB,int kb)=>InternalSizes(romKB).Contains(kb)?kb:romKB==384?64:32;
    // The PC-U6000 case is a PC-E650 with a different nameplate, so it is not
    // offered as its own appearance; stored settings naming it fall back to E650.
    internal static readonly string[] Appearances=["PC-E500","PC-E650"];
    internal static HardwareSettings Load(string path) {
        if(!File.Exists(path))return new();
        var s=JsonSerializer.Deserialize<HardwareSettings>(File.ReadAllText(path))??throw new InvalidDataException(L.T("設定ファイルが不正です。","The settings file is invalid."));
        if(string.IsNullOrWhiteSpace(s.Appearance)||s.Appearance=="PC-U6000")s=s with{Appearance=s.Appearance=="PC-U6000"?"PC-E650":"PC-E500"};
        if(string.IsNullOrWhiteSpace(s.CardPath))s=s with{CardPath=""};
        if(!L.Codes.Contains(s.Language))s=s with{Language=L.Default};
        if(!Sizes.Contains(s.InternalKB)||s.Volume<0||s.Volume>100||!Appearances.Contains(s.Appearance))throw new InvalidDataException(L.T("外観、RAMまたは音量設定が不正です。","Invalid appearance, RAM or volume setting."));return s;
    }
    internal void Save(string path)=>AtomicFile.Write(path,JsonSerializer.SerializeToUtf8Bytes(this,new JsonSerializerOptions{WriteIndented=true}));
}

internal sealed class HardwareDialog : Form
{
    readonly ComboBox internalRam=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=170};
    readonly ComboBox appearance=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=170};
    readonly ComboBox language=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=170};
    static readonly string[] LanguageNames=["日本語","English"];
    readonly TextBox cardPath=new(){Width=390,ReadOnly=true};
    readonly CheckBox disk=new(){Text=L.T("CE-140Fを接続 (X:)","Connect CE-140F (X:)"),AutoSize=true};
    readonly CheckBox protect=new(){Text=L.T("ディスクを書込み禁止にする","Write-protect the disk"),AutoSize=true};
    readonly CheckBox muted=new(){Text=L.T("消音","Mute"),AutoSize=true};
    readonly CheckBox lcdDotLines=new(){Text=L.T("LCDのドット間に線を表示（実機風）","Show lines between LCD dots (like the real unit)"),AutoSize=true};
    readonly NumericUpDown volume=new(){Minimum=0,Maximum=100,Width=85};
    readonly TextBox folder=new(){Width=410};
    // Called by the apply button. The dialog is modeless, so there is no
    // DialogResult for the caller to wait on; returning false reports a rejected
    // setting and keeps the window open on the values the user typed.
    internal Func<HardwareSettings,bool>? Apply;
    internal HardwareSettings Value=>new((int)internalRam.SelectedItem!,cardPath.Text.Trim(),disk.Checked,protect.Checked,folder.Text,muted.Checked,(int)volume.Value,lcdDotLines.Checked,(string)appearance.SelectedItem!,L.Codes[Math.Max(0,language.SelectedIndex)]);
    // cardFolder is where new card images are offered to be created and where
    // the attach dialog opens.
    internal HardwareDialog(HardwareSettings settings,int romKB=256,string? cardFolder=null)
    {
        int[] internalSizes=HardwareSettings.InternalSizes(romKB);
        Text=L.T("ハードウェア・外観設定","Hardware and Appearance");ClientSize=new(640,800);MinimumSize=new Size(520,320);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Yu Gothic UI",10);
        // The settings scroll, the buttons do not: the apply and close buttons sat
        // at the bottom of the scrolling panel and fell off the end of a short window.
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(20)};layout.Controls.Add(panel,0,0);
        appearance.Items.AddRange(HardwareSettings.Appearances);appearance.SelectedItem=settings.Appearance;
        language.Items.AddRange(LanguageNames);language.SelectedIndex=Math.Max(0,Array.IndexOf(L.Codes,L.Codes.Contains(settings.Language)?settings.Language:L.Default));
        internalRam.Items.AddRange(internalSizes.Cast<object>().ToArray());
        internalRam.SelectedItem=internalSizes.Contains(settings.InternalKB)?settings.InternalKB:internalSizes[0];
        Control Row(string title,Control c){var r=new FlowLayoutPanel{Width=575,Height=40};r.Controls.Add(new Label{Text=title,Width=170,TextAlign=ContentAlignment.MiddleLeft});r.Controls.Add(c);return r;}
        panel.Controls.Add(Row(L.T("言語 / Language","Language / 言語"),language));
        panel.Controls.Add(new Label{Text=L.T("言語を変えると、適用時にエミュレータを再起動します（状態は保存されます）。","Changing the language restarts the emulator when applied (the state is saved)."),AutoSize=true,MaximumSize=new Size(575,0),Margin=new Padding(0,0,0,12)});
        panel.Controls.Add(Row(L.T("本体の外観","Appearance"),appearance));panel.Controls.Add(Row(L.T("内蔵RAM (KB)","Internal RAM (KB)"),internalRam));
        panel.Controls.Add(new Label{Text=romKB==384
            ?L.T("384KB ROMの実機標準は64KB (B0000-BFFFF) です。62256を628128へ置き換えるメモリ増設改造に合わせて128KB・256KBも選べます。\n256KBでは80000-BFFFFの全域が内蔵RAMになります。容量ごとに内容を保持します。","A 384KB ROM machine ships with 64KB (B0000-BFFFF). 128KB and 256KB match the 62256 to 628128 memory expansion mod.\nAt 256KB the whole 80000-BFFFF range is internal RAM. Contents are kept per capacity.")
            :L.T("容量ごとに内容を保持します。変更時は保存して再起動します。\n8/16KBはBASICで使える容量が少なくなります。","Contents are kept per capacity. Changing it saves and restarts the machine.\n8/16KB leaves little room for BASIC."),AutoSize=true,MaximumSize=new Size(575,0),Margin=new Padding(0,0,0,12)});
        cardPath.Text=settings.CardPath;
        panel.Controls.Add(Row(L.T("RAMカード","RAM card"),cardPath));
        var cardButtons=new FlowLayoutPanel{Width=575,Height=38,Padding=new Padding(170,0,0,0)};
        var create=new Button{Text=L.T("作成...","Create..."),AutoSize=true};var attach=new Button{Text=L.T("装着...","Attach..."),AutoSize=true};var detach=new Button{Text=L.T("取り外す","Detach"),AutoSize=true};
        cardButtons.Controls.Add(create);cardButtons.Controls.Add(attach);cardButtons.Controls.Add(detach);
        panel.Controls.Add(cardButtons);
        var cardInfo=new Label{AutoSize=true,MaximumSize=new Size(575,0),Margin=new Padding(0,0,0,12)};
        void DescribeCard() {
            string p=cardPath.Text.Trim();
            string state=p.Length==0?L.T("未装着","not attached"):!File.Exists(p)?L.T("ファイルが見つかりません","file not found"):$"{new FileInfo(p).Length/1024}KB";
            cardInfo.Text=L.T($"現在: {state}\nRAMカードはイメージファイル（8/16/32/64/128/256KB）として装着します。内容は5秒ごと・OFF時・終了時にそのファイルへ書き込みます。装着・取り外しは適用時に再起動します。",$"Current: {state}\nA RAM card is an image file (8/16/32/64/128/256KB). Its contents are written to that file every 5 seconds, at power off and at exit. Attaching or detaching restarts the machine when applied.");
        }
        cardPath.TextChanged+=(_,_)=>DescribeCard();DescribeCard();
        panel.Controls.Add(cardInfo);
        create.Click+=(_,_)=>{
            using var ask=new CardSizeDialog();
            if(ask.ShowDialog(this)!=DialogResult.OK)return;
            using var d=new SaveFileDialog{Filter=L.T("RAMカードイメージ (*.ram)|*.ram|すべてのファイル|*.*","RAM card image (*.ram)|*.ram|All files|*.*"),FileName=$"card-{ask.KB}KB.ram",InitialDirectory=cardFolder??""};
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            try {Core.CreateCardImage(d.FileName,ask.KB);cardPath.Text=d.FileName;}
            catch(Exception ex) {MessageBox.Show(ex.Message,L.T("RAMカード作成","Create RAM card"),MessageBoxButtons.OK,MessageBoxIcon.Error);}
        };
        attach.Click+=(_,_)=>{
            using var d=new OpenFileDialog{Filter=L.T("RAMカードイメージ (*.ram;*.bin)|*.ram;*.bin|すべてのファイル|*.*","RAM card image (*.ram;*.bin)|*.ram;*.bin|All files|*.*"),InitialDirectory=cardFolder??""};
            if(d.ShowDialog(this)==DialogResult.OK)cardPath.Text=d.FileName;
        };
        detach.Click+=(_,_)=>cardPath.Clear();
        disk.Checked=settings.DiskEnabled;protect.Checked=settings.DiskReadOnly;muted.Checked=settings.Muted;volume.Value=settings.Volume;folder.Text=settings.DiskFolder;lcdDotLines.Checked=settings.LcdDotLines;
        panel.Controls.Add(disk);panel.Controls.Add(protect);
        var browse=new FlowLayoutPanel{Width=575,Height=38};browse.Controls.Add(folder);var select=new Button{Text=L.T("参照","Browse"),AutoSize=true};browse.Controls.Add(select);
        select.Click+=(_,_)=>{using var d=new FolderBrowserDialog{Description=L.T("CE-140F専用の保存フォルダを選択","Select a folder dedicated to the CE-140F"),SelectedPath=folder.Text};if(d.ShowDialog(this)==DialogResult.OK)folder.Text=d.SelectedPath;};panel.Controls.Add(browse);
        var audio=new FlowLayoutPanel{Width=575,Height=40};audio.Controls.Add(new Label{Text=L.T("音量 (%)","Volume (%)"),Width=100});audio.Controls.Add(volume);audio.Controls.Add(muted);panel.Controls.Add(audio);
        // A FlowLayoutPanel leaves its own bottom padding out of the scroll range,
        // so the last row needs the margin or it can never scroll fully into view.
        lcdDotLines.Margin=new Padding(3,3,3,24);panel.Controls.Add(lcdDotLines);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,6,20,10)};
        var ok=new Button{Text=L.T("適用","Apply"),AutoSize=true};var cancel=new Button{Text=L.T("閉じる","Close"),AutoSize=true,Margin=new Padding(8,0,0,0)};
        buttons.Controls.Add(ok);buttons.Controls.Add(cancel);layout.Controls.Add(buttons,0,1);AcceptButton=ok;CancelButton=cancel;
        ok.Click+=(_,_)=>{if(Apply is null||Apply(Value))Close();};cancel.Click+=(_,_)=>Close();
    }
}

// Asks for the capacity of a new RAM card image.
internal sealed class CardSizeDialog : Form
{
    readonly ComboBox size=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=120};
    internal int KB=>(int)size.SelectedItem!;
    internal CardSizeDialog()
    {
        Text=L.T("RAMカード作成","Create RAM card");FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterParent;Font=new Font("Yu Gothic UI",10);ClientSize=new Size(400,190);
        size.Items.AddRange(HardwareSettings.Sizes.Cast<object>().ToArray());size.SelectedItem=64;
        // Fixed positions: the note gets a box tall enough for two lines, and the
        // buttons sit in their own strip below it.
        Controls.Add(new Label{Text=L.T("容量 (KB)","Capacity (KB)"),Location=new Point(16,18),Size=new Size(90,size.Height),TextAlign=ContentAlignment.MiddleLeft});
        size.Location=new Point(110,16);Controls.Add(size);
        Controls.Add(new Label{Text=L.T("全バイト FF の空カードを作ります。装着後、最初のPF1で初期化されます。","Creates a blank card filled with FF. The first PF1 after attaching formats it."),Location=new Point(16,54),AutoSize=true,MaximumSize=new Size(368,0)});
        var ok=new Button{Text="OK",DialogResult=DialogResult.OK,AutoSize=true,MinimumSize=new Size(90,32),Location=new Point(190,140)};
        var cancel=new Button{Text=L.T("キャンセル","Cancel"),DialogResult=DialogResult.Cancel,AutoSize=true,MinimumSize=new Size(90,32),Location=new Point(292,140)};
        Controls.Add(ok);Controls.Add(cancel);AcceptButton=ok;CancelButton=cancel;
    }
}
