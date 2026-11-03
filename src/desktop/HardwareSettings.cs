// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text.Json;
namespace PokeCom;

// CardPath names the RAM card image file in the slot ("" for an empty slot).
// The file's size is the card's capacity.
/// <summary>ハードウェアと外観の設定（state/settings.json に保存する）。</summary>
/// <param name="InternalKB">内蔵 RAM の容量（KB）</param>
/// <param name="CardPath">RAM カードのイメージファイル（空ならカードなし）</param>
/// <param name="DiskEnabled">CE-140F を接続するか</param>
/// <param name="DiskReadOnly">CE-140F を書き込み禁止にするか</param>
/// <param name="DiskFolder">CE-140F として使うフォルダ</param>
/// <param name="Muted">消音するか</param>
/// <param name="Volume">音量（0〜100）</param>
/// <param name="LcdDotLines">LCD をリアル表示（ドットの間をあける）にするか（名前は以前の「ドット間の線」のまま）</param>
/// <param name="Appearance">外観（PC-E500 か PC-E650）</param>
/// <param name="Language">表示言語（ja か en）</param>
/// <param name="PrinterConnected">CE-126P を接続するか</param>
/// <param name="LcdContrast">LCD のコントラスト（0〜100、50 が既定）</param>
/// <param name="View">ウィンドウの表示（通常・本体だけ・画面だけ）</param>
/// <param name="ViewZoom">本体だけ・画面だけの表示の拡大率（0 なら通常の表示から決める）</param>
/// <param name="ViewLeft">本体だけ・画面だけの表示のウィンドウの左端（画面の座標）</param>
/// <param name="ViewTop">同じく上端</param>
/// <param name="CheckUpdates">起動時に新しい版を確認するか（1日に1回まで）</param>
/// <param name="UpdateCheckedAt">最後に新しい版を確認した時刻（UTC）</param>
/// <param name="SkippedUpdate">「この版は通知しない」にした版のタグ</param>
internal sealed record HardwareSettings(int InternalKB=32,string CardPath="",bool DiskEnabled=true,bool DiskReadOnly=false,string DiskFolder="",bool Muted=false,int Volume=50,bool LcdDotLines=false,string Appearance="PC-E500",string Language="",bool PrinterConnected=true,int LcdContrast=PocketSurface.DefaultContrast,
    WindowView View=WindowView.Normal,float ViewZoom=0,int ViewLeft=0,int ViewTop=0,
    bool CheckUpdates=true,DateTime UpdateCheckedAt=default,string SkippedUpdate="")
{
    /// <summary>選べる RAM の容量（KB）。</summary>
    internal static readonly int[] Sizes=[8,16,32,64,128,256];
    // The stock E500 carries one 62256 (32KB) and the E650/U6000 two of them (64KB,
    // B0000-BFFFF). Replacing each 32KB SRAM with a 128KB 628128 is the well-known
    // expansion mod, so an E650/U6000 reaches 128KB or 256KB (80000-BFFFF) instead.
    /// <summary>ROM の容量（機種）に合う内蔵 RAM の容量の一覧。</summary>
    internal static int[] InternalSizes(int romKB)=>romKB==384?[64,128,256]:Sizes;
    /// <summary>機種に合わない内蔵 RAM の容量を、その機種の標準の容量（E500 は 32KB、E650 は 64KB）にする。</summary>
    internal static int CoerceInternal(int romKB,int kb)=>InternalSizes(romKB).Contains(kb)?kb:romKB==384?64:32;
    // The PC-U6000 case is a PC-E650 with a different nameplate, so it is not
    // offered as its own appearance; stored settings naming it fall back to E650.
    /// <summary>選べる外観。</summary>
    internal static readonly string[] Appearances=["PC-E500","PC-E650"];
    /// <summary>設定ファイルを読む（なければ既定の設定）。古い値は直し、不正な値なら例外。</summary>
    /// <param name="path">設定ファイル</param>
    internal static HardwareSettings Load(string path) {
        if(!File.Exists(path))return new();
        var s=JsonSerializer.Deserialize<HardwareSettings>(File.ReadAllText(path))??throw new InvalidDataException(L.T("設定ファイルが不正です。","The settings file is invalid."));
        if(string.IsNullOrWhiteSpace(s.Appearance)||s.Appearance=="PC-U6000")s=s with{Appearance=s.Appearance=="PC-U6000"?"PC-E650":"PC-E500"};
        if(string.IsNullOrWhiteSpace(s.CardPath))s=s with{CardPath=""};
        if(!L.Codes.Contains(s.Language))s=s with{Language=L.Default};
        // A file from before the contrast setting takes the parameter's default; one out of range, the same.
        // コントラストのない古い設定ファイルは既定値になる。範囲外の値も既定値にする。
        if(s.LcdContrast<0||s.LcdContrast>100)s=s with{LcdContrast=PocketSurface.DefaultContrast};
        // A view this build does not know, or a zoom that is no number, starts normal.
        // 知らない表示や、数でない拡大率は、通常の表示から始める。
        if(!Enum.IsDefined(s.View)||!float.IsFinite(s.ViewZoom)||s.ViewZoom<0)s=s with{View=WindowView.Normal,ViewZoom=0};
        if(!Sizes.Contains(s.InternalKB)||s.Volume<0||s.Volume>100||!Appearances.Contains(s.Appearance))throw new InvalidDataException(L.T("外観、RAMまたは音量設定が不正です。","Invalid appearance, RAM or volume setting."));return s;
    }
    /// <summary>設定ファイルに書く。</summary>
    internal void Save(string path)=>AtomicFile.Write(path,JsonSerializer.SerializeToUtf8Bytes(this,new JsonSerializerOptions{WriteIndented=true}));
}

/// <summary>「設定」ウィンドウ（内蔵 RAM・RAM カード・CE-140F・音声・外観・言語）。LCD の表示は「画面」ウィンドウ。</summary>
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
    readonly CheckBox checkUpdates=new(){Text=L.T("起動時に新しい版を確認する（1日に1回、GitHub に問い合わせます）","Check for a new version at start-up (once a day, asking GitHub)"),AutoSize=true};
    readonly NumericUpDown volume=new(){Minimum=0,Maximum=100,Width=85};
    readonly TextBox folder=new(){Width=410};
    // Called by the apply button. The dialog is modeless, so there is no
    // DialogResult for the caller to wait on; returning false reports a rejected
    // setting and keeps the window open on the values the user typed. It is
    // handed Over, to lay the dialog's fields on the settings as they are when
    // applied: anything kept elsewhere (the CE-126P's connection) stays as it is.
    /// <summary>「適用」で呼ぶ処理。引数は Over（今の設定にウィンドウの値を重ねる関数）。
    /// false を返すとウィンドウを閉じない。</summary>
    internal Func<Func<HardwareSettings,HardwareSettings>,bool>? Apply;
    /// <summary>今の設定に、このウィンドウが扱う項目の値だけを重ねた設定。ほかの項目（CE-126P の接続など）は current のまま。</summary>
    /// <param name="current">適用する時点の設定</param>
    internal HardwareSettings Over(HardwareSettings current)=>current with{
        InternalKB=(int)internalRam.SelectedItem!,CardPath=cardPath.Text.Trim(),DiskEnabled=disk.Checked,DiskReadOnly=protect.Checked,
        DiskFolder=folder.Text,Muted=muted.Checked,Volume=(int)volume.Value,CheckUpdates=checkUpdates.Checked,
        Appearance=(string)appearance.SelectedItem!,Language=L.Codes[Math.Max(0,language.SelectedIndex)]};
    // cardFolder is where new card images are offered to be created and where
    // the attach dialog opens.
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="settings">今の設定（初期値）</param>
    /// <param name="romKB">ROM の容量（選べる内蔵 RAM の容量が変わる）</param>
    /// <param name="cardFolder">新しい RAM カードを作る場所</param>
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
        disk.Checked=settings.DiskEnabled;protect.Checked=settings.DiskReadOnly;muted.Checked=settings.Muted;checkUpdates.Checked=settings.CheckUpdates;volume.Value=settings.Volume;folder.Text=settings.DiskFolder;
        panel.Controls.Add(disk);panel.Controls.Add(protect);
        var browse=new FlowLayoutPanel{Width=575,Height=38};browse.Controls.Add(folder);var select=new Button{Text=L.T("参照","Browse"),AutoSize=true};browse.Controls.Add(select);
        select.Click+=(_,_)=>{using var d=new FolderBrowserDialog{Description=L.T("CE-140F専用の保存フォルダを選択","Select a folder dedicated to the CE-140F"),SelectedPath=folder.Text};if(d.ShowDialog(this)==DialogResult.OK)folder.Text=d.SelectedPath;};panel.Controls.Add(browse);
        // A FlowLayoutPanel leaves its own bottom padding out of the scroll range,
        // so the last row needs the margin or it can never scroll fully into view.
        // The LCD's look is in the display settings (DisplayDialog).
        var audio=new FlowLayoutPanel{Width=575,Height=40};audio.Controls.Add(new Label{Text=L.T("音量 (%)","Volume (%)"),Width=100});audio.Controls.Add(volume);audio.Controls.Add(muted);panel.Controls.Add(audio);
        checkUpdates.Margin=new Padding(3,3,3,24);panel.Controls.Add(checkUpdates);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,6,20,10)};
        var ok=new Button{Text=L.T("適用","Apply"),AutoSize=true};var cancel=new Button{Text=L.T("閉じる","Close"),AutoSize=true,Margin=new Padding(8,0,0,0)};
        buttons.Controls.Add(ok);buttons.Controls.Add(cancel);layout.Controls.Add(buttons,0,1);AcceptButton=ok;CancelButton=cancel;
        ok.Click+=(_,_)=>{if(Apply is null||Apply(Over))Close();};cancel.Click+=(_,_)=>Close();
    }
}

// Asks for the capacity of a new RAM card image.
/// <summary>新しい RAM カードの容量を選ぶウィンドウ。</summary>
internal sealed class CardSizeDialog : Form
{
    readonly ComboBox size=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=120};
    /// <summary>選んだ容量（KB）。</summary>
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
