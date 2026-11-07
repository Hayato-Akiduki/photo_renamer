using System;
using System.Runtime.InteropServices;

namespace PhotoRenamer
{
    /// <summary>Windows 標準の「フォルダーの選択」ダイアログ（エクスプローラーと同じ見た目）。</summary>
    public static class FolderPicker
    {
        public static string Show(IntPtr owner, string title, string initialFolder)
        {
            var dlg = (IFileOpenDialog)new FileOpenDialogRcw();
            try
            {
                dlg.GetOptions(out uint opts);
                dlg.SetOptions(opts | 0x20 /*PICKFOLDERS*/ | 0x40 /*FORCEFILESYSTEM*/);
                dlg.SetTitle(title);
                if (!string.IsNullOrEmpty(initialFolder) && System.IO.Directory.Exists(initialFolder))
                {
                    var iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref iid, out var item) == 0)
                        dlg.SetFolder(item);
                }
                if (dlg.Show(owner) != 0) return null;
                dlg.GetResult(out var result);
                result.GetDisplayName(0x80058000 /*SIGDN_FILESYSPATH*/, out string path);
                return path;
            }
            finally { Marshal.ReleaseComObject(dlg); }
        }

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogRcw { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint c, IntPtr specs);
            void SetFileTypeIndex(uint i);
            void GetFileTypeIndex(out uint i);
            void Advise(IntPtr pfde, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem psi);
            void GetCurrentSelection(out IShellItem psi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem psi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string ext);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem psi);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attribs);
            void Compare(IShellItem psi, uint hint, out int order);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);
    }
}
