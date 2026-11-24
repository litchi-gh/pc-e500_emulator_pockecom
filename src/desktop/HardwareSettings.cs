// SPDX-License-Identifier: GPL-2.0-or-later
namespace PokeCom;

/// <summary>「設定」ウィンドウ（内蔵 RAM・RAM カード・CE-140F・音声・外観・言語）。LCD の表示は「画面」ウィンドウ。</summary>
internal sealed class HardwareDialog : ThemedForm
{
    readonly ComboBox internalRam=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=170};
    readonly ComboBox appearance=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=170};
    readonly ComboBox language=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=170};
    readonly ComboBox theme=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=220};
    static string[] ThemeNames=>[L.T("Windows に合わせる","As Windows"),L.T("ライト","Light"),L.T("ダーク","Dark")];
    static readonly string[] LanguageNames=["日本語","English"];
    readonly TextBox cardPath=new(){Width=380,ReadOnly=true};
    readonly CheckBox disk=new(){Text=L.T("CE-140Fを接続 (X:)","Connect CE-140F (X:)"),AutoSize=true};
    readonly CheckBox protect=new(){Text=L.T("ディスクを書込み禁止にする","Write-protect the disk"),AutoSize=true};
    readonly CheckBox muted=new(){Text=L.T("消音","Mute"),AutoSize=true};
    readonly CheckBox autoOff=new(){Text=L.T("自動電源OFF（実機と同じ）","Auto power off (as the real one)"),AutoSize=true};
    readonly CheckBox topMost=new(){Text=L.T("常に手前に表示","Always on top"),AutoSize=true};
    readonly CheckBox checkUpdates=new(){Text=L.T("起動時に新しい版を確認する（1日に1回）","Check for a new version at start-up (once a day)"),AutoSize=true};
    readonly NumericUpDown volume=new(){Minimum=0,Maximum=100,Width=85};
    readonly TextBox folder=new(){Width=470};
    // Called by the apply button. The dialog is modeless, so there is no
    // DialogResult for the caller to wait on; returning false reports a rejected
    // setting and keeps the window open on the values the user typed. It is
    // handed Over, to lay the dialog's fields on the settings as they are when
    // applied: anything kept elsewhere (the CE-126P's connection) stays as it is.
    /// <summary>「適用」で呼ぶ処理。引数は Over（今の設定にウィンドウの値を重ねる関数）。
    /// false を返すとウィンドウを閉じない。</summary>
    internal Func<Func<HardwareSettings,HardwareSettings>,bool>? Apply;
    // A blank card made over the one in the slot was undone by the next autosave,
    // which wrote the machine's card back over it: that file is refused.
    /// <summary>今、本体に装着しているカードのファイル（そのファイルには新しいカードを作らせない）。</summary>
    internal Func<string?>? InUse;
    readonly NoteLabel ramNote=new("",560);
    /// <summary>本体の ROM が変わったときに、選べる内蔵 RAM の容量と説明を、その機種に合わせ直す。</summary>
    /// <param name="standardKB">機種の標準の内蔵 RAM の容量（32 か 64）</param>
    /// <param name="internalKB">今の内蔵 RAM の容量</param>
    internal void SetRom(int standardKB,int internalKB) {
        int[] sizes=HardwareSettings.InternalSizes(standardKB);
        internalRam.Items.Clear();internalRam.Items.AddRange(sizes.Cast<object>().ToArray());
        internalRam.SelectedItem=sizes.Contains(internalKB)?internalKB:sizes[0];
        ramNote.Text=standardKB==64
            ?L.T("実機は 64KB。128・256KB は増設改造に合わせた値です。容量ごとに内容を残し、変えると再起動します。","The machine has 64KB; 128 and 256KB match the expansion mod. Kept per capacity; a change restarts.")
            :L.T("容量ごとに内容を残し、変えると再起動します。8・16KB では BASIC の空きが少なくなります。","Kept per capacity; a change restarts. 8 and 16KB leave little room for BASIC.");
    }
    /// <summary>今の設定に、このウィンドウが扱う項目の値だけを重ねた設定。ほかの項目（CE-126P の接続など）は current のまま。</summary>
    /// <param name="current">適用する時点の設定</param>
    internal HardwareSettings Over(HardwareSettings current)=>current with{
        InternalKB=(int)internalRam.SelectedItem!,CardPath=cardPath.Text.Trim(),DiskEnabled=disk.Checked,DiskReadOnly=protect.Checked,
        DiskFolder=folder.Text,Muted=muted.Checked,Volume=(int)volume.Value,CheckUpdates=checkUpdates.Checked,AutoPowerOff=autoOff.Checked,TopMost=topMost.Checked,
        Appearance=(string)appearance.SelectedItem!,Language=L.Codes[Math.Max(0,language.SelectedIndex)],Theme=Theme.Choices[Math.Max(0,theme.SelectedIndex)]};
    // cardFolder is where new card images are offered to be created and where
    // the attach dialog opens.
    /// <summary>ウィンドウを作る。</summary>
    /// <param name="settings">今の設定（初期値）</param>
    /// <param name="standardKB">機種の標準の内蔵 RAM の容量（選べる内蔵 RAM の容量が変わる）</param>
    /// <param name="cardFolder">新しい RAM カードを作る場所</param>
    internal HardwareDialog(HardwareSettings settings,int standardKB=32,string? cardFolder=null)
    {
        Text=L.T("ハードウェア・外観設定","Hardware and Appearance");ClientSize=new(660,780);MinimumSize=new Size(560,360);StartPosition=FormStartPosition.CenterScreen;Load+=(_,_)=>LockWidth();
        Font=Fonts.Ui;
        // The settings scroll, the buttons do not: they sit in the footer.
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16,16,16,8)};
        FlowLayoutPanel Card(string title){var card=new CardPanel{Title=title,Margin=new Padding(0,0,0,10)};panel.Controls.Add(card);return card.Stack(584);}
        appearance.Items.AddRange(HardwareSettings.Appearances);appearance.SelectedItem=settings.Appearance;
        language.Items.AddRange(LanguageNames);language.SelectedIndex=Math.Max(0,Array.IndexOf(L.Codes,L.Codes.Contains(settings.Language)?settings.Language:L.Default));
        SetRom(standardKB,settings.InternalKB);
        const int Label=150;
        var look=Card(L.T("表示","Display"));
        look.Controls.Add(Row(L.T("言語 / Language","Language / 言語"),Label,language));
        theme.Items.AddRange(ThemeNames);theme.SelectedIndex=Math.Max(0,Array.IndexOf(Theme.Choices,settings.Theme));
        look.Controls.Add(Row(L.T("テーマ","Theme"),Label,theme));
        look.Controls.Add(new NoteLabel(L.T("言語・テーマは、適用するとエミュレータを再起動します（状態は保存）。","A new language or theme restarts the emulator when applied (the state is saved)."),560));
        var machine=Card(L.T("本体","Machine"));
        machine.Controls.Add(Row(L.T("本体の外観","Appearance"),Label,appearance));machine.Controls.Add(Row(L.T("内蔵RAM (KB)","Internal RAM (KB)"),Label,internalRam));
        machine.Controls.Add(ramNote);
        cardPath.Text=settings.CardPath;
        var create=new FluentButton{Text=L.T("作成...","Create..."),AutoSize=true};var attach=new FluentButton{Text=L.T("装着...","Attach..."),AutoSize=true};var detach=new FluentButton{Text=L.T("取り外す","Detach"),AutoSize=true};
        var cardRow=Row(L.T("RAMカード","RAM card"),Label,cardPath);cardRow.Margin=new Padding(0,10,0,2);machine.Controls.Add(cardRow);
        var cardButtons=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(Label,0,0,0),BackColor=Theme.Card};
        foreach(var bt in new[]{create,attach,detach}){bt.Margin=new Padding(0,0,6,0);cardButtons.Controls.Add(bt);}
        machine.Controls.Add(cardButtons);
        var cardInfo=new NoteLabel("",560);
        void DescribeCard() {
            string p=cardPath.Text.Trim();
            string state=p.Length==0?L.T("未装着","not attached"):!File.Exists(p)?L.T("ファイルが見つかりません","file not found"):$"{new FileInfo(p).Length/1024}KB";
            cardInfo.Text=L.T($"今: {state}。イメージファイル（8〜256KB）を装着し、5 秒ごと・OFF 時・終了時に書き込みます。装着・取り外しは適用時に再起動します。",$"Now: {state}. An image file (8-256KB), written every 5 s, at power off and at exit. Attaching or detaching restarts when applied.");
        }
        cardPath.TextChanged+=(_,_)=>DescribeCard();DescribeCard();
        machine.Controls.Add(cardInfo);
        create.Click+=(_,_)=>{
            using var ask=new CardSizeDialog();
            if(ask.ShowDialog(this)!=DialogResult.OK)return;
            using var d=new SaveFileDialog{Filter=L.T("RAMカードイメージ (*.ram)|*.ram|すべてのファイル|*.*","RAM card image (*.ram)|*.ram|All files|*.*"),FileName=$"card-{ask.KB}KB.ram",InitialDirectory=cardFolder??""};
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            if(InUse?.Invoke() is string used && string.Equals(Path.GetFullPath(used),Path.GetFullPath(d.FileName),StringComparison.OrdinalIgnoreCase)) {
                MessageBox.Show(L.T("本体に装着しているカードのファイルには、新しいカードを作れません。別の名前にするか、先に取り外して適用してください。","A new card cannot be made over the card in the machine. Choose another name, or detach it and apply first."),L.T("RAMカード作成","Create RAM card"),MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return;
            }
            try {Core.CreateCardImage(d.FileName,ask.KB);cardPath.Text=d.FileName;}
            catch(Exception ex) {MessageBox.Show(ex.Message,L.T("RAMカード作成","Create RAM card"),MessageBoxButtons.OK,MessageBoxIcon.Error);}
        };
        attach.Click+=(_,_)=>{
            using var d=new OpenFileDialog{Filter=L.T("RAMカードイメージ (*.ram;*.bin)|*.ram;*.bin|すべてのファイル|*.*","RAM card image (*.ram;*.bin)|*.ram;*.bin|All files|*.*"),InitialDirectory=cardFolder??""};
            if(d.ShowDialog(this)==DialogResult.OK)cardPath.Text=d.FileName;
        };
        detach.Click+=(_,_)=>cardPath.Clear();
        disk.Checked=settings.DiskEnabled;protect.Checked=settings.DiskReadOnly;muted.Checked=settings.Muted;checkUpdates.Checked=settings.CheckUpdates;autoOff.Checked=settings.AutoPowerOff;topMost.Checked=settings.TopMost;volume.Value=settings.Volume;folder.Text=settings.DiskFolder;
        var drive=Card("CE-140F");
        drive.Controls.Add(disk);drive.Controls.Add(protect);
        var select=new FluentButton{Text=L.T("参照","Browse"),AutoSize=true};
        var folderRow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,BackColor=Theme.Card,Margin=new Padding(0,4,0,0)};
        folder.Margin=new Padding(0,3,6,3);folderRow.Controls.Add(folder);folderRow.Controls.Add(select);drive.Controls.Add(folderRow);
        select.Click+=(_,_)=>{using var d=new FolderBrowserDialog{Description=L.T("CE-140F専用の保存フォルダを選択","Select a folder dedicated to the CE-140F"),SelectedPath=folder.Text};if(d.ShowDialog(this)==DialogResult.OK)folder.Text=d.SelectedPath;};
        // The LCD's look is in the display settings (DisplayDialog).
        var other=Card(L.T("音・電源・その他","Sound, power and more"));
        var audio=Row(L.T("音量 (%)","Volume (%)"),Label,volume,muted);other.Controls.Add(audio);
        other.Controls.Add(autoOff);
        other.Controls.Add(new NoteLabel(L.T("キーを押さずに約10分で電源が切れます（外すと切れません）。","Switches off after about 10 minutes without a key (cleared, it stays on)."),540){Margin=new Padding(22,0,0,6)});
        other.Controls.Add(topMost);other.Controls.Add(checkUpdates);
        var footer=new FooterBar();
        var cancel=footer.Add(L.T("閉じる","Close"),(_,_)=>Close());
        var ok=footer.Add(L.T("適用","Apply"),(_,_)=>{if(Apply is null||Apply(Over))Close();});
        Controls.Add(panel);Controls.Add(footer);AcceptButton=ok;CancelButton=cancel;
    }
}

// Asks for the capacity of a new RAM card image.
/// <summary>新しい RAM カードの容量を選ぶウィンドウ。</summary>
internal sealed class CardSizeDialog : ThemedForm
{
    readonly ComboBox size=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=120};
    /// <summary>選んだ容量（KB）。</summary>
    internal int KB=>(int)size.SelectedItem!;
    internal CardSizeDialog()
    {
        Text=L.T("RAMカード作成","Create RAM card");FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.CenterParent;Font=Fonts.Ui;ClientSize=new Size(420,10);
        size.Items.AddRange(HardwareSettings.Sizes.Cast<object>().ToArray());size.SelectedItem=64;
        var body=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16,16,16,8)};
        var card=new CardPanel{Margin=new Padding(0)};var stack=card.Stack(364);body.Controls.Add(card);
        stack.Controls.Add(Row(L.T("容量 (KB)","Capacity (KB)"),100,size));
        stack.Controls.Add(new NoteLabel(L.T("全バイト FF の空カードを作ります。装着後、最初の PF1 で初期化されます。","A blank card of FF bytes. The first PF1 after attaching formats it."),364));
        var footer=new FooterBar();
        var cancel=footer.Add(L.T("キャンセル","Cancel"),null,DialogResult.Cancel);
        var ok=footer.Add("OK",null,DialogResult.OK);
        Controls.Add(body);Controls.Add(footer);AcceptButton=ok;CancelButton=cancel;
        Load+=(_,_)=>FitHeight(body,footer);
    }
}
