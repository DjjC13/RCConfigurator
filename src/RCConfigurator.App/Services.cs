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

    /// <param name="second">True for the second sample, used when several things are selected together.</param>
    public static string FileWithExtension(string extension, bool second = false)
    {
        var path = Path.Combine(Folder, FileStem + (second ? " 2" : "") + extension);
        if (!File.Exists(path)) File.WriteAllBytes(path, []);
        return path;
    }

    public static string SubFolder(bool second = false)
    {
        var path = Path.Combine(Folder, FolderName + (second ? " 2" : ""));
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

    // The class the engine registers as a context-menu handler.
    private const string ContextMenuClassId = "{BAE3934B-8A6A-4BFB-81BD-3FC599A1BAF1}";

    public bool IsInstalled => Folder is not null;

    /// <summary>True when the engine is registered with Windows, which makes Explorer load it every time it starts.</summary>
    public static bool IsRegistered
    {
        get
        {
            using var key = Registry.ClassesRoot.OpenSubKey($@"CLSID\{ContextMenuClassId}");
            return key is not null;
        }
    }

    /// <summary>
    /// True when Explorer has the engine loaded right now, false when it does not, and null when
    /// that cannot be seen, as happens when an x64 build looks at Explorer on an ARM PC.
    /// </summary>
    public bool? IsLoaded()
    {
        if (Folder is null) return false;
        try
        {
            var session = Process.GetCurrentProcess().SessionId;
            foreach (var explorer in Process.GetProcessesByName("explorer").Where(p => p.SessionId == session))
            {
                foreach (ProcessModule module in explorer.Modules)
                {
                    if (module.FileName.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Registers the engine with Windows and restarts Explorer. This is the one step that needs
    /// administrator rights, so Windows asks for permission. Returns false if that is refused.
    /// </summary>
    public async Task<bool> RegisterAsync()
    {
        if (Folder is null) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Folder, "shell.exe"), "-register -restart -silent")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            if (process is null) return false;

            await process.WaitForExitAsync();
            return IsRegistered;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static Engine Find()
    {
        // The installer and winget use Program Files; Scoop keeps it under the user's profile.
        string[] folders =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nilesoft Shell"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Nilesoft Shell"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "nilesoft-shell", "current"),
        ];
        foreach (var folder in folders)
        {
            var exe = Path.Combine(folder, "shell.exe");
            // The file version is the full one, such as 1.9.18; the product version stops at 1.9.
            if (File.Exists(exe)) return new Engine(folder, FileVersionInfo.GetVersionInfo(exe).FileVersion);
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
