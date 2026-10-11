using System.Text;
using Microsoft.Win32;

namespace RCConfigurator.Cleanup;

/// <summary>
/// Writes registry keys to a .reg file in the format Windows' own tools read, so a backup can be
/// restored by double-clicking it or with "reg import".
/// </summary>
public static class RegFile
{
    private const string Header = "Windows Registry Editor Version 5.00";

    /// <param name="keys">Full key paths, each with whether the keys beneath it are wanted too.</param>
    public static void Write(string file, IEnumerable<(string KeyPath, bool WithSubkeys)> keys)
    {
        var text = new StringBuilder(Header).Append("\r\n");
        foreach (var (path, withSubkeys) in keys)
        {
            using var key = RegistryPaths.Open(path);
            if (key is not null) Append(text, path, key, withSubkeys);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllText(file, text.ToString(), Encoding.Unicode);
    }

    private static void Append(StringBuilder text, string path, RegistryKey key, bool withSubkeys)
    {
        text.Append("\r\n[").Append(path).Append("]\r\n");
        foreach (var name in key.GetValueNames())
        {
            text.Append(name.Length == 0 ? "@" : Quote(name)).Append('=').Append(Format(key, name)).Append("\r\n");
        }

        if (!withSubkeys) return;
        foreach (var name in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(name);
            if (child is not null) Append(text, $@"{path}\{name}", child, withSubkeys: true);
        }
    }

    private static string Format(RegistryKey key, string name)
    {
        var kind = key.GetValueKind(name);
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        switch (kind)
        {
            // A string holding a line break or other control character cannot be quoted safely.
            case RegistryValueKind.String when value is string text && !text.Any(char.IsControl):
                return Quote(text);
            case RegistryValueKind.String:
                return Hex(1, Terminated((string?)value ?? ""));
            case RegistryValueKind.ExpandString:
                return Hex(2, Terminated((string?)value ?? ""));
            case RegistryValueKind.MultiString:
                return Hex(7, [.. ((string[]?)value ?? []).SelectMany(Terminated), 0, 0]);
            case RegistryValueKind.DWord:
                return $"dword:{unchecked((uint)(int)value!):x8}";
            case RegistryValueKind.QWord:
                return Hex(11, BitConverter.GetBytes((long)value!));
            case RegistryValueKind.Binary:
                return "hex:" + Bytes((byte[]?)value ?? []);
            default:
                return Hex((int)kind, value as byte[] ?? []);
        }
    }

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static byte[] Terminated(string text) => Encoding.Unicode.GetBytes(text + "\0");

    private static string Hex(int kind, byte[] bytes) => $"hex({kind:x}):{Bytes(bytes)}";

    private static string Bytes(byte[] bytes) => string.Join(',', bytes.Select(b => b.ToString("x2")));
}
