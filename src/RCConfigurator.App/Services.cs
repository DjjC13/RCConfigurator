using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using RCConfigurator.Core;

namespace RCConfigurator.App;

/// <summary>Runs rcprobe.exe, which sits next to the app, and reads the menu it reports.</summary>
public static class ProbeRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static async Task<MenuSnapshot> RunAsync(IEnumerable<string> arguments)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "rcprobe.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("rcprobe.exe is missing from the app's folder.", exe);

        var start = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("rcprobe.exe did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Reading the menu took too long. A shell extension may be stuck.");
        }

        if (process.ExitCode != 0)
        {
            var reason = (await error).Trim();
            throw new InvalidOperationException(reason.Length > 0 ? reason : $"rcprobe.exe failed with code {process.ExitCode}.");
        }

        return SnapshotJson.Deserialize(await output);
    }
}

/// <summary>The stand-in files and folders the probe right-clicks.</summary>
public static class SampleFiles
{
    public const string FileStem = "sample";
    public const string FolderName = "Sample folder";

    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RCConfigurator", "samples");

    public static string Folder
    {
        get
        {
            Directory.CreateDirectory(Root);
            return Root;
        }
    }

    public static string FileWithExtension(string extension)
    {
        var path = Path.Combine(Folder, FileStem + extension);
        if (!File.Exists(path)) File.WriteAllBytes(path, []);
        return path;
    }

    public static string SubFolder()
    {
        var path = Path.Combine(Folder, FolderName);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    public static bool IsValidExtension(string extension) =>
        extension.Length >= 2
        && extension[0] == '.'
        && extension.AsSpan(1).IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !extension.AsSpan(1).Contains('.')
        && !extension.EndsWith(' ');
}

public static class ProfileStore
{
    /// <summary>Where the rules are kept. Starting the app with "--profile file.json" points this elsewhere.</summary>
    public static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RCConfigurator", "profile.json");

    public static Profile Load()
    {
        try
        {
            return File.Exists(FilePath) ? ProfileJson.Deserialize(File.ReadAllText(FilePath)) : new Profile();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // An unreadable profile should not stop the app from opening; keep it aside and start clean.
            try { File.Copy(FilePath, FilePath + ".unreadable", overwrite: true); } catch (IOException) { }
            return new Profile();
        }
    }

    public static void Save(Profile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, ProfileJson.Serialize(profile));
    }
}

/// <summary>Nilesoft Shell, the program that applies the rules inside Explorer.</summary>
public sealed record Engine(string? Folder, string? Version)
{
    // The engine reads its config path from this per-user value and falls back to its own shell.nss without it.
    private const string RegistryKey = @"Software\Nilesoft\Shell";
    private const string RegistryValue = "config";

    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RCConfigurator", "shell.nss");

    public bool IsInstalled => Folder is not null;

    public static Engine Find()
    {
        foreach (var programFiles in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var folder = Path.Combine(Environment.GetFolderPath(programFiles), "Nilesoft Shell");
            var exe = Path.Combine(folder, "shell.exe");
            if (File.Exists(exe)) return new Engine(folder, FileVersionInfo.GetVersionInfo(exe).ProductVersion);
        }

        return new Engine(null, null);
    }

    public static bool IsConnected
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            return string.Equals(key?.GetValue(RegistryValue) as string, ConfigPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public string BuildConfig(Profile profile) =>
        NssWriter.Write(profile, new NssOptions(EngineFile("theme.nss"), EngineFile("images.nss")));

    public void WriteConfig(Profile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, BuildConfig(profile));
    }

    public static void Connect()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
        key.SetValue(RegistryValue, ConfigPath);
    }

    public static void Disconnect()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
        key?.DeleteValue(RegistryValue, throwOnMissingValue: false);
    }

    private string? EngineFile(string name)
    {
        if (Folder is null) return null;
        var path = Path.Combine(Folder, "imports", name);
        return File.Exists(path) ? path : null;
    }
}
