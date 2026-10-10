using System.Runtime.InteropServices;

namespace RCConfigurator.Probe;

// Method order in these interfaces is the COM vtable order and must not change.

[ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string displayName, IntPtr pchEaten, out IntPtr ppidl, IntPtr pdwAttributes);
    [PreserveSig] int EnumObjects(IntPtr hwnd, uint flags, out IntPtr ppenumIDList);
    [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
    [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
    [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
    [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
    [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
    [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr pName);
    [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr ppidlOut);
}

[ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
    // Declared only to keep the vtable layout. The probe reads menus and never runs a command.
    [PreserveSig] int InvokeCommand(IntPtr pici);
    [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
}

[ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu2
{
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
    [PreserveSig] int InvokeCommand(IntPtr pici);
    [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
    [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
}

[ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu3
{
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
    [PreserveSig] int InvokeCommand(IntPtr pici);
    [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
    [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
    [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, IntPtr plResult);
}

[ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellView
{
    [PreserveSig] int GetWindow(out IntPtr phwnd);
    [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enterMode);
    [PreserveSig] int TranslateAccelerator(IntPtr pmsg);
    [PreserveSig] int EnableModeless([MarshalAs(UnmanagedType.Bool)] bool enable);
    [PreserveSig] int UIActivate(uint state);
    [PreserveSig] int Refresh();
    [PreserveSig] int CreateViewWindow(IntPtr previous, IntPtr folderSettings, IntPtr shellBrowser, IntPtr viewRect, out IntPtr phwnd);
    [PreserveSig] int DestroyViewWindow();
    [PreserveSig] int GetCurrentInfo(IntPtr folderSettings);
    [PreserveSig] int AddPropertySheetPages(uint reserved, IntPtr callback, IntPtr lParam);
    [PreserveSig] int SaveViewState();
    [PreserveSig] int SelectItem(IntPtr pidlItem, uint flags);
    [PreserveSig] int GetItemObject(uint item, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
}

[ComImport, Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExplorerBrowser
{
    [PreserveSig] int Initialize(IntPtr hwndParent, ref RECT rect, ref FOLDERSETTINGS folderSettings);
    [PreserveSig] int Destroy();
    [PreserveSig] int SetRect(IntPtr phdwp, RECT rect);
    [PreserveSig] int SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string propertyBag);
    [PreserveSig] int SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string emptyText);
    [PreserveSig] int SetFolderSettings(ref FOLDERSETTINGS folderSettings);
    [PreserveSig] int Advise(IntPtr events, out uint cookie);
    [PreserveSig] int Unadvise(uint cookie);
    [PreserveSig] int SetOptions(uint flags);
    [PreserveSig] int GetOptions(out uint flags);
    [PreserveSig] int BrowseToIDList(IntPtr pidl, uint flags);
    [PreserveSig] int BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object target, uint flags);
    [PreserveSig] int FillFromObject([MarshalAs(UnmanagedType.IUnknown)] object source, uint flags);
    [PreserveSig] int RemoveAll();
    [PreserveSig] int GetCurrentView(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FOLDERSETTINGS
{
    public uint ViewMode;
    public uint fFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public int ptX;
    public int ptY;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MENUITEMINFOW
{
    public uint cbSize;
    public uint fMask;
    public uint fType;
    public uint fState;
    public uint wID;
    public IntPtr hSubMenu;
    public IntPtr hbmpChecked;
    public IntPtr hbmpUnchecked;
    public UIntPtr dwItemData;
    public IntPtr dwTypeData;
    public uint cch;
    public IntPtr hbmpItem;
}

internal static class NativeMethods
{
    public static Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    public static Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    public const uint CMF_NORMAL = 0x0;
    public const uint CMF_NODEFAULT = 0x20;
    public const uint CMF_CANRENAME = 0x10;
    public const uint CMF_ITEMMENU = 0x80;
    public const uint CMF_EXTENDEDVERBS = 0x100;

    public const uint GCS_VERBA = 0x0;
    public const uint GCS_VERBW = 0x4;

    public const uint MIIM_STATE = 0x1;
    public const uint MIIM_ID = 0x2;
    public const uint MIIM_SUBMENU = 0x4;
    public const uint MIIM_STRING = 0x40;
    public const uint MIIM_FTYPE = 0x100;

    public const uint MFT_SEPARATOR = 0x800;
    public const uint MFS_DISABLED = 0x3;
    public const uint MFS_CHECKED = 0x8;
    public const uint MFS_DEFAULT = 0x1000;

    public const uint WM_INITMENUPOPUP = 0x0117;

    public static Guid IID_IShellView = new("000214E3-0000-0000-C000-000000000046");
    public static readonly Guid CLSID_ExplorerBrowser = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");

    public const uint SVGIO_BACKGROUND = 0;
    public const uint FVM_DETAILS = 4;
    public const uint FWF_DESKTOP = 0x20;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CLIPCHILDREN = 0x02000000;
    public const uint PM_REMOVE = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessageW(out MSG msg, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref MSG msg);

    public static void Check(int hr, string action)
    {
        if (hr >= 0) return;
        var reason = Marshal.GetExceptionForHR(hr)?.Message ?? $"error 0x{hr:X8}";
        throw new InvalidOperationException($"Could not {action}: {reason}");
    }

    [DllImport("ole32.dll")]
    public static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll")]
    public static extern void OleUninitialize();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string name, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    public static extern int SHBindToParent(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv, out IntPtr ppidlLast);

    [DllImport("shell32.dll")]
    public static extern int SHGetDesktopFolder([MarshalAs(UnmanagedType.Interface)] out IShellFolder ppshf);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMenuItemInfoW(IntPtr hMenu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MENUITEMINFOW info);
}
