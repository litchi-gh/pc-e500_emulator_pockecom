// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;
namespace PokeCom;

// WinForms' OpenFileDialog cannot carry a control of its own, but the Windows
// dialog under it can (IFileDialogCustomize): "Load program" adds its "run after
// loading" check box there, next to the Open button, as Windows' own dialogs do.
// WinForms の OpenFileDialog には自分のコントロールを足せないが、下にある Windows のダイアログ（IFileDialogCustomize）には足せる。
/// <summary>チェックボックス付きの「ファイルを開く」ウィンドウ。</summary>
internal static class FileDialogs
{
    /// <summary>ファイルを1つ選ばせる。ウィンドウの下にチェックボックスを1つ出す。</summary>
    /// <param name="owner">持ち主のウィンドウ</param>
    /// <param name="filters">種類の一覧（表示名とパターン。パターンは ; で区切る）</param>
    /// <param name="folder">はじめに開くフォルダ（空なら Windows に任せる）</param>
    /// <param name="checkText">チェックボックスの文字</param>
    /// <param name="check">チェックの初めの状態。閉じたときの状態が返る</param>
    /// <returns>選んだファイル（やめたら null）</returns>
    internal static string? OpenWithCheck(IWin32Window owner,(string Name,string Pattern)[] filters,string folder,string checkText,ref bool check) {
        var dialog=(IFileDialog)new FileOpenDialogClass();
        try {
            dialog.SetOptions(FOS_FORCEFILESYSTEM|FOS_PATHMUSTEXIST|FOS_FILEMUSTEXIST|FOS_NOCHANGEDIR);
            var specs=filters.Select(f=>new FilterSpec{Name=f.Name,Spec=f.Pattern}).ToArray();
            dialog.SetFileTypes((uint)specs.Length,specs);dialog.SetFileTypeIndex(1);
            if(folder.Length>0 && Directory.Exists(folder)) {
                var iid=typeof(IShellItem).GUID;
                if(SHCreateItemFromParsingName(folder,IntPtr.Zero,ref iid,out var item)==0){dialog.SetFolder(item);Marshal.ReleaseComObject(item);}
            }
            var custom=(IFileDialogCustomize)dialog;
            custom.AddCheckButton(CheckId,checkText,check);
            int hr=dialog.Show(owner.Handle);
            if(hr!=0)return null;
            custom.GetCheckButtonState(CheckId,out check);
            dialog.GetResult(out var result);
            try {
                result.GetDisplayName(SIGDN_FILESYSPATH,out var name);
                try {return Marshal.PtrToStringUni(name);} finally {Marshal.FreeCoTaskMem(name);}
            } finally {Marshal.ReleaseComObject(result);}
        } finally {Marshal.ReleaseComObject(dialog);}
    }
    // Everything but showing the window, for the tests: the calls go through the
    // interfaces in their vtable order, and the check box reads back as it was set.
    /// <summary>テスト用：ウィンドウを出す前までを行い、チェックボックスの状態を読み返す。</summary>
    internal static bool TestPrepare(string folder,bool check) {
        var dialog=(IFileDialog)new FileOpenDialogClass();
        try {
            dialog.SetOptions(FOS_FORCEFILESYSTEM|FOS_PATHMUSTEXIST|FOS_FILEMUSTEXIST|FOS_NOCHANGEDIR);
            dialog.SetFileTypes(2,[new FilterSpec{Name="BASIC",Spec="*.bas"},new FilterSpec{Name="All",Spec="*.*"}]);dialog.SetFileTypeIndex(1);
            var iid=typeof(IShellItem).GUID;
            if(SHCreateItemFromParsingName(folder,IntPtr.Zero,ref iid,out var item)!=0)throw new IOException("No shell item for "+folder);
            item.GetDisplayName(SIGDN_FILESYSPATH,out var name);
            try {if(!string.Equals(Marshal.PtrToStringUni(name),Path.GetFullPath(folder).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new IOException("The shell item names another folder");}
            finally {Marshal.FreeCoTaskMem(name);}
            dialog.SetFolder(item);Marshal.ReleaseComObject(item);
            var custom=(IFileDialogCustomize)dialog;
            custom.AddCheckButton(CheckId,"check",check);
            custom.GetCheckButtonState(CheckId,out bool back);
            return back;
        } finally {Marshal.ReleaseComObject(dialog);}
    }
    const uint CheckId=1;
    const uint FOS_NOCHANGEDIR=0x8,FOS_FORCEFILESYSTEM=0x40,FOS_PATHMUSTEXIST=0x800,FOS_FILEMUSTEXIST=0x1000;
    const uint SIGDN_FILESYSPATH=0x80058000;

    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]
    static extern int SHCreateItemFromParsingName(string path,IntPtr bindContext,ref Guid iid,out IShellItem item);

    [ComImport,Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] class FileOpenDialogClass {}

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct FilterSpec {[MarshalAs(UnmanagedType.LPWStr)] public string Name;[MarshalAs(UnmanagedType.LPWStr)] public string Spec;}

    // The methods in their vtable order, up to the last one used.
    [ComImport,Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count,[MarshalAs(UnmanagedType.LPArray)] FilterSpec[] specs);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events,out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out IntPtr name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }
    [ComImport,Guid("e6fdd21a-163f-4975-9c8c-a69f1ba37034"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialogCustomize
    {
        void EnableOpenDropDown(uint id);
        void AddMenu(uint id,[MarshalAs(UnmanagedType.LPWStr)] string label);
        void AddPushButton(uint id,[MarshalAs(UnmanagedType.LPWStr)] string label);
        void AddComboBox(uint id);
        void AddRadioButtonList(uint id);
        void AddCheckButton(uint id,[MarshalAs(UnmanagedType.LPWStr)] string label,[MarshalAs(UnmanagedType.Bool)] bool check);
        void AddEditBox(uint id,[MarshalAs(UnmanagedType.LPWStr)] string text);
        void AddSeparator(uint id);
        void AddText(uint id,[MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetControlLabel(uint id,[MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetControlState(uint id,out uint state);
        void SetControlState(uint id,uint state);
        void GetEditBoxText(uint id,out IntPtr text);
        void SetEditBoxText(uint id,[MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetCheckButtonState(uint id,[MarshalAs(UnmanagedType.Bool)] out bool check);
    }
    [ComImport,Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr bindContext,ref Guid handler,ref Guid iid,out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint form,out IntPtr name);
    }
}
