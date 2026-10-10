using System.Diagnostics;
using RCConfigurator.Core;

namespace RCConfigurator.Tests;

/// <summary>
/// Runs the real rcprobe.exe against the real Windows shell, so results depend on the machine.
/// The assertions stick to items Windows itself always provides.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProbeTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("rcprobe-test-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_file_menu_has_the_standard_commands()
    {
        var file = Path.Combine(_folder, "sample.txt");
        File.WriteAllText(file, "sample");

        var snapshot = Probe(file);

        Assert.Equal(TargetKind.Items, snapshot.Kind);
        Assert.Equal([file], snapshot.Paths);
        var verbs = Flatten(snapshot.Items).Select(i => i.Verb).ToList();
        Assert.Contains("properties", verbs);
        Assert.Contains("delete", verbs);
        Assert.Contains("copy", verbs);
    }

    [Fact]
    public void Two_files_can_be_selected_together()
    {
        var first = Path.Combine(_folder, "first.txt");
        var second = Path.Combine(_folder, "second.txt");
        File.WriteAllText(first, "1");
        File.WriteAllText(second, "2");

        var snapshot = Probe(first, second);

        Assert.Equal(2, snapshot.Paths.Count);
        Assert.Contains("delete", Flatten(snapshot.Items).Select(i => i.Verb));
    }

    [Fact]
    public void A_folder_background_menu_has_items()
    {
        var snapshot = Probe("--background", _folder);

        Assert.Equal(TargetKind.FolderBackground, snapshot.Kind);
        Assert.Contains(snapshot.Items, i => !i.IsSeparator);
    }

    [Fact]
    public void A_missing_path_is_a_usage_error()
    {
        var (exitCode, _, error) = Run(Path.Combine(_folder, "missing.txt"));

        Assert.Equal(1, exitCode);
        Assert.Contains("does not exist", error);
    }

    private static MenuSnapshot Probe(params string[] arguments)
    {
        var (exitCode, output, error) = Run(arguments);
        Assert.True(exitCode == 0, $"rcprobe exited with {exitCode}: {error}");
        return SnapshotJson.Deserialize(output);
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] arguments)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "rcprobe.exe");
        Assert.True(File.Exists(exe), $"rcprobe.exe was not found at {exe}");

        var start = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("rcprobe did not finish within 60 seconds.");
        }

        return (process.ExitCode, output.Result, error.Result);
    }

    private static IEnumerable<MenuEntry> Flatten(IEnumerable<MenuEntry> items) =>
        items.SelectMany(i => Flatten(i.Children).Prepend(i));
}
