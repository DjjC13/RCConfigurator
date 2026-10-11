using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using RCConfigurator.Cleanup;

namespace RCConfigurator.App;

/// <summary>Everything one missing program left behind.</summary>
public sealed class LeftoverProgram(string program, IReadOnlyList<Leftover> entries) : Observable
{
    private bool _isSelected;

    public string Program { get; } = program;

    public IReadOnlyList<Leftover> Entries { get; } = entries;

    public string Name => Path.GetFileName(Program);

    public bool NeedsAdministrator => Entries.Any(e => RegistryPaths.IsUnder(e.Action.KeyPath, LeftoverScanner.MachineClasses));

    public string Detail => $"{Summary} · {Program} is missing";

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    private string Summary => string.Join(", ", new[]
    {
        Count(LeftoverKind.FileType, "file type", "file types"),
        Count(LeftoverKind.MenuItem, "menu item", "menu items"),
        Count(LeftoverKind.MenuHandler, "menu extension", "menu extensions"),
        Count(LeftoverKind.FileTypeLink, "link from a file extension", "links from file extensions"),
    }.OfType<string>());

    private string? Count(LeftoverKind kind, string one, string many)
    {
        var count = Entries.Count(e => e.Kind == kind);
        return count == 0 ? null : count == 1 ? $"1 {one}" : $"{count} {many}";
    }
}

/// <param name="Removed">How many entries were deleted.</param>
/// <param name="Problems">What could not be done, in the user's terms.</param>
public sealed record CleanupOutcome(int Removed, IReadOnlyList<string> Problems);

/// <summary>
/// Finds and removes what uninstalled programs left in the registry. Per-user entries are removed
/// by the app itself; machine-wide ones by a second copy of the app that Windows is asked to run
/// with administrator rights.
/// </summary>
public static class CleanupService
{
    /// <summary>The command-line option that makes the app carry out an elevated clean-up and exit.</summary>
    public const string ElevatedOption = "--clean";

    private const string Restored = ".restored";
    private static readonly string[] UserRoots = [LeftoverScanner.UserClasses, LeftoverScanner.UserFileExtensions];

    public static string BackupFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RCConfigurator", "backups");

    public static Task<IReadOnlyList<LeftoverProgram>> ScanAsync() => Task.Run<IReadOnlyList<LeftoverProgram>>(() =>
        LeftoverScanner.Scan(LeftoverScanner.RealClassesRoots, LeftoverScanner.UserFileExtensions)
            .GroupBy(l => l.Program, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new LeftoverProgram(g.Key, g.ToList()))
            .ToList());

    public static async Task<CleanupOutcome> RemoveAsync(IReadOnlyList<LeftoverProgram> programs)
    {
        var paths = programs.Select(p => p.Program).ToList();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(BackupFolder);

        // Per-user entries first; see ElevatedCleanup.Remove for why the order matters.
        var user = await Task.Run(() => ElevatedCleanup.Remove(paths, Path.Combine(BackupFolder, $"{stamp}-user.reg"), UserRoots));
        var removed = user.Removed;
        var problems = user.Problems.ToList();

        if (programs.Any(p => p.NeedsAdministrator))
        {
            var request = Path.Combine(Path.GetTempPath(), $"rcconfigurator-clean-{stamp}.json");
            var report = Path.ChangeExtension(request, ".report.json");
            ElevatedCleanup.WriteRequest(request, new ElevatedCleanup.Request(paths, Path.Combine(BackupFolder, $"{stamp}-machine.reg"), report));
            try
            {
                if (await RunElevatedAsync(Environment.ProcessPath!, $"{ElevatedOption} \"{request}\"") && ElevatedCleanup.ReadReport(report) is { } machine)
                {
                    removed += machine.Removed;
                    problems.AddRange(machine.Problems);
                }
                else
                {
                    problems.Add("Windows did not give permission, so the entries that apply to the whole PC were left in place.");
                }
            }
            finally
            {
                File.Delete(request);
                File.Delete(report);
            }
        }

        NotifyShell();
        return new CleanupOutcome(removed, problems);
    }

    /// <summary>The backups of the most recent clean-up that has not been undone, or none.</summary>
    public static IReadOnlyList<string> LastBackups()
    {
        if (!Directory.Exists(BackupFolder)) return [];

        var files = Directory.GetFiles(BackupFolder, "*.reg").Where(f => !f.Contains(Restored, StringComparison.OrdinalIgnoreCase)).ToList();
        var latest = files.Select(StampOf).Max();
        return files.Where(f => StampOf(f) == latest).ToList();

        static string StampOf(string file) => Path.GetFileName(file)[..15];
    }

    /// <summary>Puts back what the most recent clean-up removed. Returns what happened, for showing to the user.</summary>
    public static async Task<string> UndoAsync()
    {
        var backups = LastBackups();
        if (backups.Count == 0) return "There is no backup to restore.";

        foreach (var backup in backups)
        {
            var isMachine = backup.EndsWith("-machine.reg", StringComparison.OrdinalIgnoreCase);
            var done = isMachine
                ? await RunElevatedAsync("reg.exe", $"import \"{backup}\"")
                : await RunHiddenAsync("reg.exe", $"import \"{backup}\"");
            if (!done) return "Not every entry could be restored. The backup files are still in " + BackupFolder + ".";

            File.Move(backup, Path.ChangeExtension(backup, null) + Restored + ".reg", overwrite: true);
        }

        NotifyShell();
        return "The entries from the last clean-up were restored.";
    }

    private static async Task<bool> RunElevatedAsync(string program, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // The user said no to the permission prompt.
            return false;
        }
    }

    private static async Task<bool> RunHiddenAsync(string program, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = false, CreateNoWindow = true });
        if (process is null) return false;

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    /// <summary>Tells Explorer that file types changed, so menus and icons are rebuilt without a restart.</summary>
    private static void NotifyShell() => SHChangeNotify(0x08000000, 0, 0, 0);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
