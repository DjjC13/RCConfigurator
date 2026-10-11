using Microsoft.Win32;
using RCConfigurator.Core;

namespace RCConfigurator.App;

/// <summary>What Windows knows about the file types on this PC.</summary>
public static class WindowsFileTypes
{
    // Windows labels many file types with a kind in the registry. These are the labels worth
    // offering as groups, with the name shown for each. "system" and "application" are left out:
    // their members have little in common.
    private static readonly (string Label, string Name)[] Kinds =
    [
        ("image", "Images"),
        ("video", "Videos"),
        ("audio", "Audio"),
        ("text", "Text files"),
        ("document", "Documents"),
        ("compressed", "Archives"),
    ];

    /// <summary>Every file extension registered on this PC, lower-case and sorted.</summary>
    public static IReadOnlyList<string> AllExtensions()
    {
        return Registry.ClassesRoot.GetSubKeyNames()
            .Where(name => name.StartsWith('.'))
            .Select(name => name.ToLowerInvariant())
            .Where(SampleFiles.IsValidExtension)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The groups Windows itself defines, such as Images, with the types this PC has in each.</summary>
    public static IReadOnlyList<FileKind> BuiltInKinds()
    {
        var byLabel = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in AllExtensions())
        {
            using var key = Registry.ClassesRoot.OpenSubKey(extension);
            if (key?.GetValue("PerceivedType") is not string label) continue;

            if (!byLabel.TryGetValue(label, out var members)) byLabel[label] = members = [];
            members.Add(extension);
        }

        return Kinds
            .Where(k => byLabel.ContainsKey(k.Label))
            .Select(k => new FileKind { Name = k.Name, Extensions = byLabel[k.Label], IsBuiltIn = true })
            .ToList();
    }
}

/// <summary>Reads the menus of many file types and remembers them for the rest of the session.</summary>
public sealed class MenuScanner
{
    // Small enough that one misbehaving shell extension costs little, large enough to be quick.
    private const int FilesPerRun = 40;
    private const int RunsAtOnce = 4;

    private readonly Dictionary<(string Type, bool Extended, bool Submenus), MenuSnapshot?> _known = [];

    /// <summary>
    /// Returns the menu of each type that has one. Types already read this session are not read
    /// again unless <paramref name="fresh"/> is set. <paramref name="progress"/> is told how many
    /// types are done.
    /// </summary>
    public async Task<IReadOnlyList<(string Type, MenuSnapshot Menu)>> ScanAsync(
        IReadOnlyList<string> types, bool extended, bool submenus, bool fresh, IProgress<int>? progress)
    {
        var missing = types.Where(t => fresh || !_known.ContainsKey((t, extended, submenus))).ToList();
        var done = types.Count - missing.Count;
        progress?.Report(done);

        using var limit = new SemaphoreSlim(RunsAtOnce);
        var runs = missing.Chunk(FilesPerRun).Select(async chunk =>
        {
            await limit.WaitAsync();
            try
            {
                var menus = await ReadAsync(chunk, extended, submenus);
                lock (_known)
                {
                    for (var i = 0; i < chunk.Length; i++) _known[(chunk[i], extended, submenus)] = menus[i];
                    done += chunk.Length;
                }

                progress?.Report(done);
            }
            finally
            {
                limit.Release();
            }
        });
        await Task.WhenAll(runs);

        return types
            .Select(t => (Type: t, Menu: _known.GetValueOrDefault((t, extended, submenus))))
            .Where(m => m.Menu is not null)
            .Select(m => (m.Type, m.Menu!))
            .ToList();
    }

    private static async Task<IReadOnlyList<MenuSnapshot?>> ReadAsync(string[] types, bool extended, bool submenus)
    {
        var files = types.Select(t => SampleFiles.FileWithExtension(t)).ToList();
        try
        {
            return await ProbeRunner.RunEachAsync(files, extended, submenus);
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            // Something in this batch upset the probe. Read the files one at a time so that only
            // the troublesome type is lost.
            var menus = new List<MenuSnapshot?>(files.Count);
            foreach (var file in files)
            {
                try
                {
                    menus.Add((await ProbeRunner.RunEachAsync([file], extended, submenus))[0]);
                }
                catch (Exception inner) when (inner is InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
                {
                    menus.Add(null);
                }
            }

            return menus;
        }
    }
}
