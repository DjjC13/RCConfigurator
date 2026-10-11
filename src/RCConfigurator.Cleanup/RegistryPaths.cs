using System.Security;
using Microsoft.Win32;

namespace RCConfigurator.Cleanup;

/// <summary>Opens registry keys by their full path, such as HKEY_CURRENT_USER\Software\Classes.</summary>
public static class RegistryPaths
{
    public static RegistryKey? Open(string fullPath, bool writable = false)
    {
        var (hive, rest) = Split(fullPath);
        if (hive is null) return null;

        try
        {
            var root = RegistryKey.OpenBaseKey(hive.Value, RegistryView.Default);
            if (rest.Length == 0) return root;

            using (root) return root.OpenSubKey(rest, writable);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Splits a path into the key that holds it and its own name.</summary>
    public static (string Parent, string Name) ParentAndName(string fullPath)
    {
        var at = fullPath.LastIndexOf('\\');
        return at < 0 ? ("", fullPath) : (fullPath[..at], fullPath[(at + 1)..]);
    }

    public static bool IsUnder(string fullPath, string prefix) =>
        fullPath.StartsWith(prefix.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static (RegistryHive? Hive, string SubPath) Split(string fullPath)
    {
        var at = fullPath.IndexOf('\\');
        var name = at < 0 ? fullPath : fullPath[..at];
        var rest = at < 0 ? "" : fullPath[(at + 1)..];
        RegistryHive? hive = name.ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            _ => null,
        };
        return (hive, rest);
    }
}

public enum FileState
{
    Exists,
    Missing,

    /// <summary>It cannot be told, for example because the path is on a drive that is not connected.</summary>
    Unknown,
}

public static class Files
{
    /// <summary>
    /// Says whether a program file is there. Only a full path on a drive that is present can be
    /// called missing; anything else is unknown, so that an unplugged drive or a bare program
    /// name is never mistaken for an uninstalled program.
    /// </summary>
    public static FileState StateOf(string path)
    {
        if (path.Length < 3 || path[1] != ':' || path[2] != '\\' || !char.IsAsciiLetter(path[0])) return FileState.Unknown;

        try
        {
            if (!Directory.Exists(path[..3])) return FileState.Unknown;
            return File.Exists(path) || File.Exists(path + ".exe") ? FileState.Exists : FileState.Missing;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return FileState.Unknown;
        }
    }
}
