using System.Security;
using System.Text.Json;

namespace RCConfigurator.Cleanup;

/// <param name="Removed">How many entries were deleted.</param>
/// <param name="Problems">One line for each entry that could not be deleted.</param>
/// <param name="BackupFile">The .reg file holding everything as it was, or null if nothing was touched.</param>
public sealed record CleanupResult(int Removed, IReadOnlyList<string> Problems, string? BackupFile);

/// <summary>Deletes leftovers from the registry, after saving them to a backup file.</summary>
public static class LeftoverRemover
{
    /// <param name="allowedRoots">
    /// Keys the deletions must sit under. Anything else is refused, whatever the list of leftovers says.
    /// </param>
    public static CleanupResult Remove(IReadOnlyList<Leftover> leftovers, string backupFile, IReadOnlyList<string> allowedRoots)
    {
        var problems = new List<string>();
        var allowed = new List<Leftover>();
        foreach (var leftover in leftovers)
        {
            if (allowedRoots.Any(root => RegistryPaths.IsUnder(leftover.Action.KeyPath, root))) allowed.Add(leftover);
            else problems.Add($"{leftover.Action.KeyPath}: outside the part of the registry this tool may change.");
        }

        if (allowed.Count == 0) return new CleanupResult(0, problems, null);

        // Save first. A deleted key is saved with everything under it; a deleted value with its key's values.
        RegFile.Write(backupFile, allowed
            .Select(l => (l.Action.KeyPath, WithSubkeys: l.Action.ValueName is null))
            .Distinct());

        var removed = 0;

        // Values before keys, so a value is not looked for in a key that has just gone.
        foreach (var leftover in allowed.OrderBy(l => l.Action.ValueName is null))
        {
            try
            {
                if (Delete(leftover.Action)) removed++;
            }
            catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
            {
                problems.Add($"{leftover.Action.KeyPath}: {e.Message}");
            }
        }

        return new CleanupResult(removed, problems, backupFile);
    }

    private static bool Delete(CleanupAction action)
    {
        if (action.ValueName is { } value)
        {
            using var key = RegistryPaths.Open(action.KeyPath, writable: true);
            if (key is null || !key.GetValueNames().Contains(value, StringComparer.OrdinalIgnoreCase)) return false;

            key.DeleteValue(value, throwOnMissingValue: false);
            return true;
        }

        var (parentPath, name) = RegistryPaths.ParentAndName(action.KeyPath);
        using var parent = RegistryPaths.Open(parentPath, writable: true);
        if (parent is null || !parent.GetSubKeyNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return false;

        parent.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        return true;
    }
}

/// <summary>
/// The part that runs with administrator rights. It is told which missing programs to clean up
/// after and where to save the backup, then looks for leftovers itself and removes only what it
/// finds, under the machine-wide classes key. A tampered request can therefore do no more than
/// remove genuinely dead entries.
/// </summary>
public static class ElevatedCleanup
{
    public sealed record Request(IReadOnlyList<string> Programs, string BackupFile, string ReportFile);

    public static void WriteRequest(string file, Request request) =>
        File.WriteAllText(file, JsonSerializer.Serialize(request));

    public static CleanupResult? ReadReport(string file) =>
        File.Exists(file) ? JsonSerializer.Deserialize<CleanupResult>(File.ReadAllText(file)) : null;

    /// <summary>Carries out a request. Returns a process exit code: zero when it ran, whatever it found.</summary>
    public static int Run(string requestFile)
    {
        try
        {
            var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(requestFile));
            if (request is null) return 2;

            var result = Remove(request.Programs, request.BackupFile, [LeftoverScanner.MachineClasses]);
            File.WriteAllText(request.ReportFile, JsonSerializer.Serialize(result));
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return 2;
        }
    }

    /// <summary>
    /// Looks for leftovers afresh and removes those of the given programs that sit under the given
    /// keys. The per-user keys must be done before the machine-wide ones: a file extension is only
    /// recognised as pointing at a dead file type while that type is still there.
    /// </summary>
    public static CleanupResult Remove(IReadOnlyList<string> programs, string backupFile, IReadOnlyList<string> roots)
    {
        var chosen = LeftoverScanner
            .Scan(LeftoverScanner.RealClassesRoots, LeftoverScanner.UserFileExtensions)
            .Where(l => programs.Contains(l.Program, StringComparer.OrdinalIgnoreCase))
            .Where(l => roots.Any(root => RegistryPaths.IsUnder(l.Action.KeyPath, root)))
            .ToList();
        return LeftoverRemover.Remove(chosen, backupFile, roots);
    }
}
