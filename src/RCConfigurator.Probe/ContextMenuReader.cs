using System.ComponentModel;
using System.Runtime.InteropServices;
using RCConfigurator.Core;
using static RCConfigurator.Probe.NativeMethods;

namespace RCConfigurator.Probe;

internal sealed record ProbeOptions(
    TargetKind Kind,
    IReadOnlyList<string> Paths,
    bool ExtendedVerbs,
    bool ExpandSubmenus);

/// <summary>
/// Reads the classic right-click menu Windows builds for a target, the way a file manager
/// would before showing it. Nothing is displayed and no command is run.
/// </summary>
internal sealed class ContextMenuReader
{
    private const uint IdFirst = 1;
    private const uint IdLast = 0x7FFF;
    private const int MaxDepth = 4;
    private const int VerbBufferChars = 260;

    private readonly IContextMenu _menu;
    private readonly IContextMenu2? _menu2;
    private readonly IContextMenu3? _menu3;
    private readonly bool _expandSubmenus;

    private ContextMenuReader(object menu, bool expandSubmenus)
    {
        _menu = (IContextMenu)menu;
        _menu2 = menu as IContextMenu2;
        _menu3 = menu as IContextMenu3;
        _expandSubmenus = expandSubmenus;
    }

    public static MenuSnapshot Read(ProbeOptions options)
    {
        var pidls = new List<IntPtr>();
        HostedShellView? view = null;
        try
        {
            object menuObject;
            if (options.Kind == TargetKind.Items)
            {
                menuObject = GetItemsMenu(options.Paths, pidls);
            }
            else
            {
                var isDesktop = options.Kind == TargetKind.Desktop;
                try
                {
                    view = HostedShellView.Open(isDesktop ? DesktopPidl(pidls) : Parse(options.Paths[0], pidls), isDesktop);
                    menuObject = view.GetBackgroundMenu();
                }
                catch (Exception e)
                {
                    // The folder alone still reports most of the menu, just not the view's own items.
                    Console.Error.WriteLine($"Warning: the menu may be missing View, Sort by, Refresh and Paste. {e.Message}");
                    view?.Dispose();
                    view = null;
                    menuObject = isDesktop ? GetDesktopMenu() : GetBackgroundMenu(options.Paths[0], pidls);
                }
            }

            var hMenu = CreatePopupMenu();
            if (hMenu == 0) throw new Win32Exception();
            try
            {
                var flags = options.Kind == TargetKind.Items
                    ? CMF_NORMAL | CMF_CANRENAME | CMF_ITEMMENU
                    : CMF_NORMAL | CMF_NODEFAULT;
                if (options.ExtendedVerbs) flags |= CMF_EXTENDEDVERBS;

                var reader = new ContextMenuReader(menuObject, options.ExpandSubmenus);
                Check(reader._menu.QueryContextMenu(hMenu, 0, IdFirst, IdLast, flags), "build the menu");

                return new MenuSnapshot
                {
                    Kind = options.Kind,
                    Paths = options.Paths,
                    ExtendedVerbs = options.ExtendedVerbs,
                    Items = reader.ReadMenu(hMenu, depth: 0),
                };
            }
            finally
            {
                DestroyMenu(hMenu);
            }
        }
        finally
        {
            view?.Dispose();
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>The desktop is the root of the shell namespace, which is an empty item list.</summary>
    private static IntPtr DesktopPidl(List<IntPtr> pidls)
    {
        var pidl = Marshal.AllocCoTaskMem(sizeof(ushort));
        Marshal.WriteInt16(pidl, 0);
        pidls.Add(pidl);
        return pidl;
    }

    private static object GetItemsMenu(IReadOnlyList<string> paths, List<IntPtr> pidls)
    {
        IShellFolder? parent = null;
        var children = new IntPtr[paths.Count];
        for (var i = 0; i < paths.Count; i++)
        {
            var pidl = Parse(paths[i], pidls);
            // The child pointer points into pidl, so it stays valid until pidl is freed.
            Check(SHBindToParent(pidl, ref IID_IShellFolder, out var folder, out children[i]), $"open the parent of '{paths[i]}'");
            parent ??= folder;
        }

        Check(parent!.GetUIObjectOf(0, (uint)children.Length, children, ref IID_IContextMenu, 0, out var menu), "get the menu");
        return menu!;
    }

    private static object GetBackgroundMenu(string folderPath, List<IntPtr> pidls)
    {
        var pidl = Parse(folderPath, pidls);
        Check(SHGetDesktopFolder(out var desktop), "open the shell namespace");
        Check(desktop.BindToObject(pidl, 0, ref IID_IShellFolder, out var folder), $"open the folder '{folderPath}'");
        Check(((IShellFolder)folder!).CreateViewObject(0, ref IID_IContextMenu, out var menu), "get the background menu");
        return menu!;
    }

    private static object GetDesktopMenu()
    {
        Check(SHGetDesktopFolder(out var desktop), "open the shell namespace");
        Check(desktop.CreateViewObject(0, ref IID_IContextMenu, out var menu), "get the desktop menu");
        return menu!;
    }

    private static IntPtr Parse(string path, List<IntPtr> pidls)
    {
        Check(SHParseDisplayName(path, 0, out var pidl, 0, out _), $"find '{path}'");
        pidls.Add(pidl);
        return pidl;
    }

    private List<MenuEntry> ReadMenu(IntPtr hMenu, int depth)
    {
        var entries = new List<MenuEntry>();
        var count = GetMenuItemCount(hMenu);
        for (uint i = 0; i < count; i++)
        {
            var info = NewItemInfo(MIIM_FTYPE | MIIM_STATE | MIIM_ID | MIIM_SUBMENU);
            if (!GetMenuItemInfoW(hMenu, i, true, ref info)) continue;

            if ((info.fType & MFT_SEPARATOR) != 0)
            {
                entries.Add(new MenuEntry { IsSeparator = true });
                continue;
            }

            var rawText = ReadText(hMenu, i);
            var isSubmenu = info.hSubMenu != 0;
            entries.Add(new MenuEntry
            {
                Text = MenuText.Clean(rawText),
                RawText = rawText,
                Verb = isSubmenu ? null : ReadVerb(info.wID),
                IsDisabled = (info.fState & MFS_DISABLED) != 0,
                IsChecked = (info.fState & MFS_CHECKED) != 0,
                IsDefault = (info.fState & MFS_DEFAULT) != 0,
                IsSubmenu = isSubmenu,
                Children = isSubmenu && _expandSubmenus && depth < MaxDepth
                    ? ReadSubmenu(info.hSubMenu, i, depth)
                    : [],
            });
        }

        // Windows never draws a separator at either end of a menu or directly under another one.
        for (var i = entries.Count - 1; i > 0; i--)
        {
            if (entries[i].IsSeparator && entries[i - 1].IsSeparator) entries.RemoveAt(i);
        }

        if (entries.Count > 0 && entries[0].IsSeparator) entries.RemoveAt(0);
        if (entries.Count > 0 && entries[^1].IsSeparator) entries.RemoveAt(entries.Count - 1);

        return entries;
    }

    private List<MenuEntry> ReadSubmenu(IntPtr hSubMenu, uint indexInParent, int depth)
    {
        // Most submenus are filled in only when they are about to open, so tell the owner it is opening.
        var lParam = (IntPtr)(indexInParent & 0xFFFF);
        if (_menu3 is not null)
        {
            _menu3.HandleMenuMsg2(WM_INITMENUPOPUP, hSubMenu, lParam, 0);
        }
        else
        {
            _menu2?.HandleMenuMsg(WM_INITMENUPOPUP, hSubMenu, lParam);
        }

        return ReadMenu(hSubMenu, depth + 1);
    }

    private static string? ReadText(IntPtr hMenu, uint index)
    {
        var info = NewItemInfo(MIIM_STRING);
        if (!GetMenuItemInfoW(hMenu, index, true, ref info) || info.cch == 0) return null;

        var chars = info.cch + 1;
        var buffer = Marshal.AllocCoTaskMem((int)chars * sizeof(char));
        try
        {
            info.dwTypeData = buffer;
            info.cch = chars;
            return GetMenuItemInfoW(hMenu, index, true, ref info)
                ? Marshal.PtrToStringUni(buffer, (int)info.cch)
                : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    private string? ReadVerb(uint id)
    {
        if (id < IdFirst || id > IdLast) return null;

        var offset = (UIntPtr)(id - IdFirst);
        // One spare character guarantees a terminator even if the owner fills the buffer.
        var bytes = new byte[(VerbBufferChars + 1) * sizeof(char)];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var buffer = handle.AddrOfPinnedObject();
            if (_menu.GetCommandString(offset, GCS_VERBW, 0, buffer, VerbBufferChars) == 0)
            {
                var verb = Marshal.PtrToStringUni(buffer);
                if (!string.IsNullOrEmpty(verb))
                {
                    // Some shell extensions answer a Unicode request with an ANSI string,
                    // which reads back as two letters packed into each character.
                    return verb.Any(c => c > 0xFF) ? Marshal.PtrToStringAnsi(buffer) : verb;
                }
            }

            Array.Clear(bytes);
            if (_menu.GetCommandString(offset, GCS_VERBA, 0, buffer, VerbBufferChars) == 0)
            {
                var verb = Marshal.PtrToStringAnsi(buffer);
                if (!string.IsNullOrEmpty(verb)) return verb;
            }

            return null;
        }
        finally
        {
            handle.Free();
        }
    }

    private static MENUITEMINFOW NewItemInfo(uint mask) => new()
    {
        cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
        fMask = mask,
    };
}
