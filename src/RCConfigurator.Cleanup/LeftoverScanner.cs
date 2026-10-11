using Microsoft.Win32;
using RCConfigurator.Core;

namespace RCConfigurator.Cleanup;

public enum LeftoverKind
{
    /// <summary>A whole file type whose every action runs a program that is gone.</summary>
    FileType,

    /// <summary>One menu item that runs a program that is gone.</summary>
    MenuItem,

    /// <summary>A menu extension whose DLL is gone.</summary>
    MenuHandler,

    /// <summary>A file extension that still points at a file type being removed.</summary>
    FileTypeLink,
}

/// <summary>One thing to delete: a whole key, or a single value in a key.</summary>
/// <param name="KeyPath">The full path of the key, starting with its hive.</param>
/// <param name="ValueName">Null to delete the key and everything under it; otherwise the value to delete, "" being the default value.</param>
public sealed record CleanupAction(string KeyPath, string? ValueName = null);

/// <summary>A registry entry left behind by a program that is no longer installed.</summary>
/// <param name="Program">The full path of the missing file the entry depends on.</param>
/// <param name="Label">What the entry is, for showing to the user.</param>
public sealed record Leftover(string Program, LeftoverKind Kind, string Label, CleanupAction Action);

/// <summary>
/// Finds menu entries and file types whose program is gone. An entry counts as dead only when it
/// names a full path on a drive that is present and no file is there, so nothing is flagged on a
/// guess. Nothing is changed here.
/// </summary>
public static class LeftoverScanner
{
    public const string MachineClasses = @"HKEY_LOCAL_MACHINE\SOFTWARE\Classes";
    public const string UserClasses = @"HKEY_CURRENT_USER\Software\Classes";
    public const string UserFileExtensions = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

    /// <summary>Where Windows keeps file types and their menus, for this user and for the whole PC.</summary>
    public static readonly IReadOnlyList<string> RealClassesRoots = [MachineClasses, UserClasses];

    // Classes that hold other classes: their subkeys are inspected instead.
    private static readonly string[] Containers = ["Applications", "SystemFileAssociations"];

    // Classes Windows itself relies on. Dead items on them are removed one by one; the class never is.
    private static readonly HashSet<string> NeverRemovedWhole = new(StringComparer.OrdinalIgnoreCase)
    {
        "*", "AllFilesystemObjects", "Directory", "Folder", "Drive", "DesktopBackground", "LibraryFolder",
        "UserLibraryFolder", "Unknown", "CLSID", "Interface", "TypeLib", "AppID", "Network", "Printers",
        "SearchFolder", "CompressedFolder", "exefile", "batfile", "cmdfile", "comfile", "lnkfile", "piffile",
        "regfile", "inffile", "dllfile", "sysfile", "txtfile", "http", "https", "ftp", "file", "mailto",
    };

    /// <param name="classesRoots">Full paths of the keys to look through, each laid out like HKEY_CLASSES_ROOT.</param>
    /// <param name="fileExtensionsPath">The per-user list of file extensions and their Open-with choices, or null to skip it.</param>
    /// <param name="handlerFile">Finds the DLL of a menu extension from its class id. Null uses the real registry.</param>
    public static IReadOnlyList<Leftover> Scan(
        IReadOnlyList<string> classesRoots,
        string? fileExtensionsPath,
        Func<string, string?>? handlerFile = null)
    {
        handlerFile ??= RegisteredHandlerFile;
        var found = new List<Leftover>();
        var deadTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootPath in classesRoots)
        {
            using var root = RegistryPaths.Open(rootPath);
            if (root is null) continue;

            foreach (var name in root.GetSubKeyNames().Where(n => !n.StartsWith('.')))
            {
                if (Containers.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    using var container = Open(root, name);
                    foreach (var inner in container?.GetSubKeyNames() ?? [])
                    {
                        // An "Applications" entry is one program's Open-with record and can go
                        // whole; a "SystemFileAssociations" entry is shared between programs.
                        var removable = name.Equals("Applications", StringComparison.OrdinalIgnoreCase);
                        Inspect(root, rootPath, $@"{name}\{inner}", removable, handlerFile, found, deadTypes: null);
                    }
                }
                else
                {
                    Inspect(root, rootPath, name, !NeverRemovedWhole.Contains(name), handlerFile, found, deadTypes);
                }
            }
        }

        // File extensions that still point at a file type being removed.
        foreach (var rootPath in classesRoots)
        {
            using var root = RegistryPaths.Open(rootPath);
            foreach (var extension in root?.GetSubKeyNames().Where(n => n.StartsWith('.')) ?? [])
            {
                using var key = Open(root!, extension);
                if (key?.GetValue("") is string type && deadTypes.TryGetValue(type, out var program))
                {
                    found.Add(new Leftover(program, LeftoverKind.FileTypeLink, $"{extension} files open with {type}", new CleanupAction($@"{rootPath}\{extension}", "")));
                }

                AddOpenWithLinks(key, $@"{rootPath}\{extension}", extension, deadTypes, found);
            }
        }

        if (fileExtensionsPath is not null)
        {
            using var extensions = RegistryPaths.Open(fileExtensionsPath);
            foreach (var extension in extensions?.GetSubKeyNames() ?? [])
            {
                using var key = Open(extensions!, extension);
                AddOpenWithLinks(key, $@"{fileExtensionsPath}\{extension}", extension, deadTypes, found);
            }
        }

        return found;
    }

    private static void Inspect(
        RegistryKey root,
        string rootPath,
        string classPath,
        bool removableWhole,
        Func<string, string?> handlerFile,
        List<Leftover> found,
        Dictionary<string, string>? deadTypes)
    {
        using var @class = Open(root, classPath);
        if (@class is null) return;

        var verbs = new List<(string Name, string? Program, FileState State)>();
        using (var shell = Open(@class, "shell"))
        {
            foreach (var verb in shell?.GetSubKeyNames() ?? [])
            {
                using var command = Open(shell!, $@"{verb}\command");
                var program = CommandTarget.ProgramOf(command?.GetValue("") as string);
                verbs.Add((verb, program, program is null ? FileState.Unknown : Files.StateOf(program)));
            }
        }

        var dead = verbs.Where(v => v.State == FileState.Missing).ToList();

        // The whole type goes only if every action it has is dead and it holds nothing else.
        var holdsOnlyActions = @class.GetSubKeyNames().All(s =>
            s.Equals("shell", StringComparison.OrdinalIgnoreCase) || s.Equals("DefaultIcon", StringComparison.OrdinalIgnoreCase));
        if (removableWhole && verbs.Count > 0 && dead.Count == verbs.Count && holdsOnlyActions)
        {
            var program = dead.GroupBy(v => v.Program!, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).First().Key;
            found.Add(new Leftover(program, LeftoverKind.FileType, classPath, new CleanupAction($@"{rootPath}\{classPath}")));
            deadTypes?.TryAdd(classPath, program);
        }
        else
        {
            foreach (var verb in dead)
            {
                found.Add(new Leftover(verb.Program!, LeftoverKind.MenuItem, $"{verb.Name} on {classPath}", new CleanupAction($@"{rootPath}\{classPath}\shell\{verb.Name}")));
            }
        }

        using var handlers = Open(@class, @"shellex\ContextMenuHandlers");
        foreach (var handler in handlers?.GetSubKeyNames() ?? [])
        {
            using var key = Open(handlers!, handler);
            var classId = key?.GetValue("") is string { Length: > 0 } value ? value : handler;
            if (!classId.StartsWith('{') || handlerFile(classId) is not { } file || Files.StateOf(file) != FileState.Missing) continue;

            found.Add(new Leftover(file, LeftoverKind.MenuHandler, $"{handler} on {classPath}", new CleanupAction($@"{rootPath}\{classPath}\shellex\ContextMenuHandlers\{handler}")));
        }
    }

    private static void AddOpenWithLinks(RegistryKey? extensionKey, string extensionPath, string extension, Dictionary<string, string> deadTypes, List<Leftover> found)
    {
        if (extensionKey is null) return;

        using var openWith = Open(extensionKey, "OpenWithProgids");
        foreach (var type in openWith?.GetValueNames() ?? [])
        {
            if (!deadTypes.TryGetValue(type, out var program)) continue;
            found.Add(new Leftover(program, LeftoverKind.FileTypeLink, $"{extension} files offer {type} under Open with", new CleanupAction($@"{extensionPath}\OpenWithProgids", type)));
        }
    }

    /// <summary>The DLL a menu extension is registered to load, or null if that is not recorded as a plain path.</summary>
    private static string? RegisteredHandlerFile(string classId)
    {
        using var server = RegistryPaths.Open($@"HKEY_CLASSES_ROOT\CLSID\{classId}\InprocServer32");
        return server?.GetValue("") is string { Length: > 0 } path ? Environment.ExpandEnvironmentVariables(path).Trim('"') : null;
    }

    private static RegistryKey? Open(RegistryKey parent, string name)
    {
        try
        {
            return parent.OpenSubKey(name);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
