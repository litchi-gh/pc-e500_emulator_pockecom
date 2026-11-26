// SPDX-License-Identifier: GPL-2.0-or-later
using System.Text.Json;
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

/// <summary>CE-126P の印字の送り先：USB のサーマルプリンタ（ESC/POS で直接）か、Windows のプリンタ。</summary>
/// <param name="Kind">種類（Usb か Windows）</param>
/// <param name="Id">USB なら機器のパス、Windows ならプリンタの名前</param>
/// <param name="Name">表示する名前</param>
internal sealed partial record PrintTarget(string Kind,string Id,string Name)
{
    internal const string Usb="usb",Windows="windows";
    /// <summary>一覧に出す名前。</summary>
    internal string Label=>Kind==Usb?L.T($"USB サーマルプリンタ: {Name}",$"USB thermal printer: {Name}"):L.T($"プリンタ: {Name}",$"Printer: {Name}");
    public override string ToString()=>Label;
}

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
/// <param name="PrintTo">CE-126P の印字を送るプリンタ（null なら送らない）</param>
/// <param name="RomFile">「ROMを開く」で最後に開いた ROM（空なら rom フォルダから選ぶ）</param>
/// <param name="AutoPowerOff">本体の自動電源 OFF（放っておくと約10分で切れる）を働かせるか</param>
/// <param name="LcdAfterimage">LCD の残像（点いた・消えたドットを数コマかけて変える、フレーム補完）を使うか</param>
/// <param name="LoadRun">「プログラム読込」で BAS を読み込んだあと、そのまま実行するか（ファイルを選ぶウィンドウのチェック）</param>
/// <param name="Theme">見た目のテーマ（System は Windows に合わせる、Light、Dark）</param>
/// <param name="TopMost">メインウィンドウを常にほかのウィンドウより手前に出すか</param>
internal sealed record HardwareSettings(int InternalKB=32,string CardPath="",bool DiskEnabled=true,bool DiskReadOnly=false,string DiskFolder="",bool Muted=false,int Volume=50,bool LcdDotLines=false,string Appearance="PC-E500",string Language="",bool PrinterConnected=true,int LcdContrast=LcdGlass.DefaultContrast,
    WindowView View=WindowView.Normal,float ViewZoom=0,int ViewLeft=0,int ViewTop=0,
    bool CheckUpdates=true,DateTime UpdateCheckedAt=default,string SkippedUpdate="",PrintTarget? PrintTo=null,string RomFile="",bool AutoPowerOff=true,bool LcdAfterimage=true,bool LoadRun=false,string Theme="System",bool TopMost=false)
{
    /// <summary>選べる RAM の容量（KB）。機種ごとの一覧と直し方は RamSizes。</summary>
    internal static int[] Sizes=>RamSizes.Sizes;
    internal static int[] InternalSizes(int standardKB)=>RamSizes.InternalSizes(standardKB);
    internal static int CoerceInternal(int standardKB,int kb)=>RamSizes.CoerceInternal(standardKB,kb);
    // The PC-U6000 case is a PC-E650 with a different nameplate, so it is not
    // offered as its own appearance; stored settings naming it fall back to E650.
    /// <summary>選べる外観。</summary>
    internal static readonly string[] Appearances=["PC-E500","PC-E650"];
    /// <summary>選べるテーマ：システムに合わせる・ライト・ダーク。</summary>
    internal static readonly string[] Themes=["System","Light","Dark"];
    /// <summary>設定ファイルを読む（なければ既定の設定）。古い値や受け付けない値は直し、JSON として読めなければ例外。</summary>
    /// <param name="path">設定ファイル</param>
    // With no file yet (the first start) the language is Windows' own, as with a stored one this build does not know.
    // 初めての起動（ファイルなし）では、表示言語を Windows の言語にする。
    internal static HardwareSettings Load(string path) {
        if(!File.Exists(path))return Defaults;
        var s=JsonSerializer.Deserialize<HardwareSettings>(File.ReadAllText(path))??throw new InvalidDataException(L.T("設定ファイルが不正です。","The settings file is invalid."));
        if(string.IsNullOrWhiteSpace(s.Appearance)||s.Appearance=="PC-U6000")s=s with{Appearance=s.Appearance=="PC-U6000"?"PC-E650":"PC-E500"};
        if(string.IsNullOrWhiteSpace(s.CardPath))s=s with{CardPath=""};
        if(string.IsNullOrWhiteSpace(s.RomFile))s=s with{RomFile=""};
        if(!L.Codes.Contains(s.Language))s=s with{Language=L.Default};
        // A file from before the contrast setting takes the parameter's default; one out of range, the same.
        // コントラストのない古い設定ファイルは既定値になる。範囲外の値も既定値にする。
        if(s.LcdContrast<0||s.LcdContrast>100)s=s with{LcdContrast=LcdGlass.DefaultContrast};
        // A check time in the future (a clock once set wrong) would hold off the daily check until then.
        // 未来の確認時刻（一度だけ時計がずれていたなど）は、その時まで毎日の確認を止めてしまうので捨てる。
        if(s.UpdateCheckedAt>DateTime.UtcNow.AddDays(1))s=s with{UpdateCheckedAt=default};
        // A view this build does not know, or a zoom that is no number, starts normal.
        // 知らない表示や、数でない拡大率は、通常の表示から始める。
        if(!Enum.IsDefined(s.View)||!float.IsFinite(s.ViewZoom)||s.ViewZoom<0)s=s with{View=WindowView.Normal,ViewZoom=0};
        // A value this build does not take (one a newer build wrote, or one edited by
        // hand) goes back to its default alone; the rest of the file is kept.
        // この版で受け付けない値（新しい版や手で書いたもの）は、その項目だけ既定値にする（ほかの項目は残す）。
        if(!Sizes.Contains(s.InternalKB))s=s with{InternalKB=32};
        if(s.Volume<0||s.Volume>100)s=s with{Volume=50};
        if(!Appearances.Contains(s.Appearance))s=s with{Appearance="PC-E500"};
        if(!Themes.Contains(s.Theme))s=s with{Theme="System"};
        return s;
    }
    // A settings file that cannot be read stopped the program at every start until
    // the user found and deleted it by hand. Its content is now set aside beside it
    // and the program starts on the defaults. A file that only could not be opened
    // (held by a sync client) still stops the start: starting on the defaults
    // would save them over settings that are fine.
    // 中身の壊れた設定ファイルは別名で残し、既定の設定で始める（開けないだけのファイルは上書きしないよう、今までどおりエラー）。
    /// <summary>設定ファイルを読む。中身が壊れていれば、ファイルを別名（.bad-日時）で残し、既定の設定にする。</summary>
    /// <param name="path">設定ファイル</param>
    /// <param name="problem">中身が壊れていたときの理由（なければ null）</param>
    /// <param name="kept">壊れたファイルを残した名前（残せなかった、または壊れていなければ null）</param>
    internal static HardwareSettings LoadOrReset(string path,out string? problem,out string? kept) {
        problem=kept=null;
        try {return Load(path);}
        catch(Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException) {
            problem=ex.Message;
            string aside=path+".bad-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try {File.Move(path,aside,true);kept=aside;} catch(Exception) {}
            return Defaults;
        }
    }
    /// <summary>既定の設定（表示言語は Windows の言語）。</summary>
    internal static HardwareSettings Defaults=>new(){Language=L.Default};
    // One options object: a new one each time rebuilt the serializer's metadata on every save.
    static readonly JsonSerializerOptions Indented=new(){WriteIndented=true};
    /// <summary>設定ファイルに書く。</summary>
    internal void Save(string path)=>AtomicFile.Write(path,JsonSerializer.SerializeToUtf8Bytes(this,Indented));
}
