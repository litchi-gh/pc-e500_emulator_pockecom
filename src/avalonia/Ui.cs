// SPDX-License-Identifier: GPL-2.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using ShapePath=Avalonia.Controls.Shapes.Path;
namespace PokeCom;

/// <summary>色と、ウィンドウの部品（Windows 版の Theme と同じ色・同じ並び）。</summary>
internal static class Ui
{
    internal static bool Dark=>Application.Current?.ActualThemeVariant==ThemeVariant.Dark;
    static Color Rgb(byte r,byte g,byte b)=>Color.FromRgb(r,g,b);
    internal static Color Window=>Dark?Rgb(32,32,32):Rgb(243,243,243);
    internal static Color Card=>Dark?Rgb(43,43,43):Rgb(251,251,251);
    internal static Color Input=>Dark?Rgb(28,28,28):Colors.White;
    internal static Color Text=>Dark?Colors.White:Rgb(27,27,27);
    internal static Color SubText=>Dark?Rgb(200,200,200):Rgb(96,96,96);
    internal static Color NoteText=>Dark?Rgb(153,204,255):Rgb(0,84,166);
    internal static Color Border=>Dark?Rgb(62,62,62):Rgb(214,214,214);
    internal static Color Accent=>Dark?Rgb(76,160,255):Rgb(0,103,192);
    internal static Color Hover=>Dark?Color.FromArgb(18,255,255,255):Color.FromArgb(14,0,0,0);
    internal static Color Warn=>Dark?Rgb(252,185,0):Rgb(157,93,0);
    internal static Color Mix(Color a,Color b,double t)=>Rgb((byte)(a.R+(b.R-a.R)*t),(byte)(a.G+(b.G-a.G)*t),(byte)(a.B+(b.B-a.B)*t));
    /// <summary>等幅の書体。</summary>
    internal static readonly FontFamily Mono=new("Consolas, Liberation Mono, DejaVu Sans Mono, Noto Sans Mono, monospace");

    /// <summary>アイコン1つ（24×24 の線の絵）。</summary>
    internal static Control Icon(string data,double size=16,Color? color=null)=>new Viewbox{Width=size,Height=size,Child=new Canvas{Width=24,Height=24,Children={
        new ShapePath{Data=Geometry.Parse(data),Stroke=Paint.Solid(color??Text),StrokeThickness=1.6,StrokeLineCap=PenLineCap.Round,StrokeJoin=PenLineJoin.Round}}}};
    internal static Button Button(string text,Action click,bool accent=false) {
        var b=new Button{Content=text,MinWidth=76,HorizontalContentAlignment=HorizontalAlignment.Center};
        if(accent)b.Classes.Add("accent");
        b.Click+=(_,_)=>click();return b;
    }
    internal static TextBlock Label(string text,double width=0)=>new(){Text=text,VerticalAlignment=VerticalAlignment.Center,Width=width>0?width:double.NaN};
    internal static TextBlock Note(string text,double width=0)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Paint.Solid(SubText),MaxWidth=width>0?width:double.PositiveInfinity,HorizontalAlignment=HorizontalAlignment.Left};
    internal static StackPanel Row(params Control[] items){var p=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};p.Children.AddRange(items);return p;}
    internal static StackPanel Stack(params Control[] items){var p=new StackPanel{Spacing=6};p.Children.AddRange(items);return p;}
    internal static TextBlock Heading(string text)=>new(){Text=text,FontWeight=FontWeight.SemiBold};
    /// <summary>題のついた面。</summary>
    internal static Border CardOf(string? title,Control content) {
        var dock=new DockPanel();
        if(title is not null){var t=Heading(title);t.Margin=new Thickness(0,0,0,8);DockPanel.SetDock(t,Dock.Top);dock.Children.Add(t);}
        dock.Children.Add(content);
        return new Border{Background=Paint.Solid(Card),BorderBrush=Paint.Solid(Border),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Padding=new Thickness(14,12),Child=dock};
    }
    /// <summary>ウィンドウの下の帯：左に left、右にボタン。</summary>
    internal static Border Footer(Control? left,params Button[] buttons) {
        var dock=new DockPanel();
        var right=Row(buttons);DockPanel.SetDock(right,Dock.Right);dock.Children.Add(right);
        dock.Children.Add(left??new Panel());
        return new Border{Background=Paint.Solid(Card),BorderBrush=Paint.Solid(Border),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(16,10),Child=dock};
    }
    internal static FilePickerFileType Type(string name,params string[] patterns)=>new(name){Patterns=[..patterns.SelectMany(p=>new[]{p,p.ToUpperInvariant()}).Distinct()]};
    internal static FilePickerFileType AllFiles=>new(L.T("すべてのファイル","All files")){Patterns=["*"]};
    /// <summary>開くファイルを選ぶ（やめたら null）。</summary>
    internal static async Task<string?> OpenFile(TopLevel owner,string? folder,params FilePickerFileType[] types) {
        var start=folder is null?null:await owner.StorageProvider.TryGetFolderFromPathAsync(folder);
        var files=await owner.StorageProvider.OpenFilePickerAsync(new(){FileTypeFilter=[..types,AllFiles],SuggestedStartLocation=start});
        return files.Count>0?files[0].TryGetLocalPath():null;
    }
    /// <summary>保存先を選ぶ（やめたら null）。</summary>
    internal static async Task<string?> SaveFile(TopLevel owner,string? folder,string name,string extension,params FilePickerFileType[] types) {
        var start=folder is null?null:await owner.StorageProvider.TryGetFolderFromPathAsync(folder);
        var file=await owner.StorageProvider.SaveFilePickerAsync(new(){FileTypeChoices=[..types,AllFiles],DefaultExtension=extension,SuggestedFileName=name,SuggestedStartLocation=start});
        return file?.TryGetLocalPath();
    }
    internal static async Task<string?> OpenFolder(TopLevel owner,string title) {
        var f=await owner.StorageProvider.OpenFolderPickerAsync(new(){Title=title});
        return f.Count>0?f[0].TryGetLocalPath():null;
    }
    /// <summary>フォルダやページを、システムの既定のもので開く。</summary>
    internal static void Launch(string target)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target){UseShellExecute=true});
}

/// <summary>サブウィンドウの共通の部分：色・書体・Esc で閉じる。</summary>
internal class AppWindow : Window
{
    protected override Type StyleKeyOverride=>typeof(Window);
    internal AppWindow(string title,double width,double height=double.NaN) {
        Title=title;FontFamily=Paint.Ui;FontSize=13;Background=Paint.Solid(Ui.Window);Width=width;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        if(double.IsNaN(height)){SizeToContent=SizeToContent.Height;CanResize=false;}else Height=height;
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape && !e.Handled){e.Handled=true;Close();}};
        // Never taller than the screen it opens on.
        Opened+=(_,_)=>{if(Screens.ScreenFromWindow(this) is {} s)MaxHeight=s.WorkingArea.Height/s.Scaling-40;};
    }
    /// <summary>本文と下の帯を並べる。</summary>
    protected void Fill(Control body,Border footer,bool scroll=false) {
        var dock=new DockPanel();DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        body.Margin=new Thickness(16,14,16,12);
        dock.Children.Add(scroll?new ScrollViewer{Content=body}:body);
        Content=dock;
    }
}

/// <summary>メッセージと質問。</summary>
internal static class Ask
{
    static async Task<int> Show(Window owner,string title,string text,bool cancelLast,params string[] buttons) {
        int answer=cancelLast?buttons.Length-1:0;
        var window=new AppWindow(title,480){MinWidth=300};
        var made=buttons.Select((b,i)=>Ui.Button(b,()=>{answer=i;window.Close();},i==0)).ToArray();
        made[0].IsDefault=true;
        var dock=new DockPanel();var footer=Ui.Footer(null,made);DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer{MaxHeight=420,Content=new SelectableTextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(18,16)}});
        window.Content=dock;
        await window.ShowDialog(owner);
        return answer;
    }
    internal static Task Message(Window owner,string text,string title="PC-E500")=>Show(owner,title,text,false,"OK");
    internal static async Task<bool> YesNo(Window owner,string text,string title)=>await Show(owner,title,text,true,L.T("はい","Yes"),L.T("いいえ","No"))==0;
    internal static async Task<ProgramLoader.Answer> YesNoCancel(Window owner,string text,string title)=>
        (ProgramLoader.Answer)await Show(owner,title,text,true,L.T("はい","Yes"),L.T("いいえ","No"),L.T("キャンセル","Cancel"));
    /// <summary>文字を1つ聞く（やめたら null）。</summary>
    internal static async Task<string?> Name(Window owner,string title,string value) {
        string? answer=null;
        var window=new AppWindow(title,420);var box=new TextBox{Text=value};
        var ok=Ui.Button("OK",()=>{answer=box.Text?.Trim();window.Close();},true);ok.IsDefault=true;
        var dock=new DockPanel();var footer=Ui.Footer(null,ok,Ui.Button(L.T("キャンセル","Cancel"),window.Close));DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
        dock.Children.Add(new StackPanel{Margin=new Thickness(18,16),Spacing=6,Children={Ui.Label(L.T("名前","Name")),box}});
        window.Content=dock;window.Opened+=(_,_)=>{box.Focus();box.SelectAll();};
        await window.ShowDialog(owner);
        return answer is {Length:>0}?answer:null;
    }
}

/// <summary>アイコンの絵（24×24 の線）。</summary>
internal static class Icons
{
    internal const string OpenFolder="M3 19 V6 H9 L11 8 H20 V11 M3 19 L6 11 H22 L19 19 Z",Import="M3 12 H14 M10 8 L14 12 L10 16 M14 5 H20 V19 H14",Pause="M9 5 V19 M15 5 V19",Play="M8 5 L19 12 L8 19 Z",
        Save="M5 4 H16 L20 8 V20 H5 Z M8 4 V9 H15 V4 M8 20 V13 H17 V20",Load="M3 19 V6 H9 L11 8 H20 V19 Z M12 17 V11 M9.5 13.5 L12 11 L14.5 13.5",
        Settings="M12 9 A3 3 0 1 1 11.99 9 Z M12 3 V6 M12 18 V21 M3 12 H6 M18 12 H21 M5.6 5.6 L7.8 7.8 M16.2 16.2 L18.4 18.4 M5.6 18.4 L7.8 16.2 M16.2 7.8 L18.4 5.6",
        Display="M3 5 H21 V16 H3 Z M9 20 H15 M12 16 V20",Drive="M3 8 H21 V16 H3 Z M16 12 H18",Print="M7 9 V4 H17 V9 M7 17 H4 V9 H20 V17 H17 M7 14 H17 V20 H7 Z",
        Text="M4 18 L8.5 6 L13 18 M5.7 14 H11.3 M15 18 V11 M15 14.5 A2.5 3 0 1 1 15 14.6",Reset="M19 12 A7 7 0 1 1 16.5 6.6 M17 3 V7 H13",
        Bug="M12 7 C15 7 16 9 16 12 V14 C16 17 14 19 12 19 C10 19 8 17 8 14 V12 C8 9 9 7 12 7 Z M12 10 V19 M4 12 H8 M16 12 H20 M5 7 L8 9.5 M19 7 L16 9.5 M5 18 L8.3 16 M19 18 L15.7 16",
        Help="M9 9 C9 5 15.5 5 15.5 9 C15.5 12 12 11.5 12 15 M12 18.6 V18.7",Speed="M4 18 A8.5 8.5 0 1 1 20 18 M12 16 L16 9.5",
        Chip="M7 7 H17 V17 H7 Z M10 4 V7 M14 4 V7 M10 17 V20 M14 17 V20 M4 10 H7 M4 14 H7 M17 10 H20 M17 14 H20",Memory="M4 7 H20 V15 H4 Z M7 15 V18 M12 15 V18 M17 15 V18 M8 10 V12 M12 10 V12 M16 10 V12",
        Volume="M4 10 H7 L11 6 V18 L7 14 H4 Z M14.5 9 A4.5 4.5 0 0 1 14.5 15 M17 6.5 A8 8 0 0 1 17 17.5",Mute="M4 10 H7 L11 6 V18 L7 14 H4 Z M15 10 L20 15 M20 10 L15 15",
        Info="M12 3.5 A8.5 8.5 0 1 1 11.99 3.5 Z M12 11 V16.5 M12 7.6 V7.7",Code="M8.5 7 L3.5 12 L8.5 17 M15.5 7 L20.5 12 L15.5 17",Warning="M12 4 L21 20 H3 Z M12 10 V15 M12 17.4 V17.5",
        Download="M12 4 V15 M8 11 L12 15 L16 11 M4 20 H20",Folder="M3 19 V6 H9 L11 8 H21 V19 Z",Game="M8 8 H16 C20 8 22 17 19.5 17.5 C18 17.8 17 15 15 15 H9 C7 15 6 17.8 4.5 17.5 C2 17 4 8 8 8 Z M8.5 10 V13 M7 11.5 H10 M15.5 11.5 V11.6",
        View="M4 8 V4 H8 M16 4 H20 V8 M20 16 V20 H16 M8 20 H4 V16",Minimize="M5 12 H19",Close="M6 6 L18 18 M18 6 L6 18";
}

/// <summary>下のステータスバー：左に状態、横にメッセージ、右に各部の欄（押せる欄もある）。</summary>
internal sealed class StatusStrip : Border
{
    /// <summary>欄1つ：アイコン（空なら点）、文字、点の色、押したときの処理、ヒント、警告か、短い文字。</summary>
    internal sealed record Part(string Glyph,string Text,Color? Dot=null,Action? Click=null,string? Tip=null,bool Warn=false,string? Short=null);
    readonly DockPanel dock=new(){LastChildFill=true};
    readonly StackPanel right=new(){Orientation=Orientation.Horizontal};
    readonly Panel left=new(),middle=new();
    IReadOnlyList<Part> parts=[];
    /// <summary>短くする欄の順（右の欄の番号）。</summary>
    internal int[] ShortOrder=[];
    internal string Text {get;private set;}="";
    internal StatusStrip() {
        Height=30;BorderThickness=new Thickness(0,1,0,0);Background=Paint.Solid(Ui.Card);BorderBrush=Paint.Solid(Ui.Border);
        DockPanel.SetDock(left,Dock.Left);DockPanel.SetDock(right,Dock.Right);
        dock.Children.AddRange([left,right,middle]);Child=dock;ClipToBounds=true;
        SizeChanged+=(_,e)=>{if(e.WidthChanged)Fit();};
    }
    Control Field(Part p,bool compact) {
        var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,Margin=new Thickness(9,0),VerticalAlignment=VerticalAlignment.Center};
        var ink=p.Warn?Ui.Warn:Ui.SubText;
        row.Children.Add(p.Glyph.Length==0?new Ellipse{Width=8,Height=8,Fill=Paint.Solid(p.Dot??Ui.SubText),VerticalAlignment=VerticalAlignment.Center}:Ui.Icon(p.Glyph,14,ink));
        row.Children.Add(new TextBlock{Text=compact?p.Short??p.Text:p.Text,FontSize=12,Foreground=Paint.Solid(p.Warn?Ui.Warn:Ui.Text),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});
        var box=new Border{Child=row,Background=Brushes.Transparent,Margin=new Thickness(2,3),CornerRadius=new CornerRadius(4)};
        if(p.Click is {} click) {
            box.Cursor=new Cursor(StandardCursorType.Hand);
            box.PointerEntered+=(_,_)=>box.Background=Paint.Solid(Ui.Hover);box.PointerExited+=(_,_)=>box.Background=Brushes.Transparent;
            box.PointerReleased+=(_,e)=>{if(e.InitialPressMouseButton==MouseButton.Left)click();};
        }
        string? tip=compact && p.Short is not null?p.Text+(p.Tip is null?"":"\n"+p.Tip):p.Tip;
        if(tip is not null)ToolTip.SetTip(box,tip);
        return box;
    }
    static Border Divider()=>new(){Width=1,Background=Paint.Solid(Ui.Border),Margin=new Thickness(0,7)};
    Part? state,message;
    internal void SetParts(Part newState,Part? newMessage,IReadOnlyList<Part> fields) {
        state=newState;message=newMessage;parts=fields;
        Text=string.Join("   ",new[]{state,message}.Concat(parts).Where(p=>p is not null).Select(p=>p!.Text));
        Background=Paint.Solid(Ui.Card);BorderBrush=Paint.Solid(Ui.Border);
        left.Children.Clear();left.Children.Add(Ui.Row(Field(state,false),Divider()));
        middle.Children.Clear();if(message is not null)middle.Children.Add(Field(message,false));
        Fit();
    }
    // A message of a few seconds has the bar to itself; otherwise the fields that
    // do not fit go short one by one, and last of all the leftmost go.
    void Fit() {
        right.Children.Clear();
        if(state is null || message is {Warn:false})return;
        var compact=new HashSet<int>();double room=Bounds.Width-150-(message is null?0:120);
        double WidthOf(int i){var c=Field(parts[i],compact.Contains(i));c.Measure(Size.Infinity);return c.DesiredSize.Width+1;}
        var widths=Enumerable.Range(0,parts.Count).Select(WidthOf).ToArray();
        foreach(int i in ShortOrder.Concat(Enumerable.Range(0,parts.Count))) {
            if(widths.Sum()<=room || i>=parts.Count)break;
            compact.Add(i);widths[i]=WidthOf(i);
        }
        int first=0;while(first<parts.Count && widths.Skip(first).Sum()>room)first++;
        for(int i=first;i<parts.Count;i++){right.Children.Add(Divider());right.Children.Add(Field(parts[i],compact.Contains(i)));}
    }
}
