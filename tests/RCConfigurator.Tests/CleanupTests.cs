using System.Diagnostics;
using Microsoft.Win32;
using RCConfigurator.Cleanup;
using RCConfigurator.Core;

namespace RCConfigurator.Tests;

public class CommandTargetTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" \"%1\"", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Program Files (x86)\\Winamp\\winamp.exe /HANDLE %1", "C:\\Program Files (x86)\\Winamp\\winamp.exe")]
    [InlineData("C:\\Tools\\run.exe", "C:\\Tools\\run.exe")]
    [InlineData("  notepad.exe %1", "notepad.exe")]
    [InlineData("rundll32 shell32.dll,Control_RunDLL", "rundll32")]
    [InlineData("C:\\Apps\\my.exe.config\\tool.exe -x", "C:\\Apps\\my.exe.config\\tool.exe")]
    [InlineData("\"unterminated", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ProgramOf_finds_the_program_at_the_start_of_a_command(string? command, string? expected)
    {
        Assert.Equal(expected, CommandTarget.ProgramOf(command));
    }

    [Fact]
    public void ProgramOf_expands_environment_variables()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe"),
            CommandTarget.ProgramOf("%SystemRoot%\\notepad.exe %1"));
    }
}

/// <summary>
/// Exercises the scanner and remover against a scratch key under HKEY_CURRENT_USER that is laid
/// out like the real classes key and deleted afterwards. Nothing real is touched.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CleanupTests : IDisposable
{
    private const string HandlerId = "{11111111-2222-3333-4444-555555555555}";

    private readonly string _name = $@"Software\RCConfigurator.Tests\{Guid.NewGuid():N}";
    private readonly string _sandbox;
    private readonly string _classes;
    private readonly string _fileExts;
    private readonly string _gone = $@"C:\RCConfigurator-no-such-folder-{Guid.NewGuid():N}\gone.exe";
    private readonly string _goneDll;
    private readonly string _notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
    private readonly string _backup = Path.Combine(Path.GetTempPath(), $"rcc-test-{Guid.NewGuid():N}.reg");

    public CleanupTests()
    {
        _sandbox = $@"HKEY_CURRENT_USER\{_name}";
        _classes = $@"{_sandbox}\Classes";
        _fileExts = $@"{_sandbox}\FileExts";
        _goneDll = Path.ChangeExtension(_gone, ".dll");

        // A file type whose program is gone, with values of every kind to check the backup.
        Set(@"Classes\Dead.File", "", "A \"dead\" type \\ with awkward characters");
        Set(@"Classes\Dead.File\DefaultIcon", "", $"{_gone},0");
        Set(@"Classes\Dead.File\shell\open\command", "", $"\"{_gone}\" \"%1\"");
        Set(@"Classes\Dead.File\shell\Play\command", "", $"{_gone} /play %1");
        Set(@"Classes\Dead.File", "Number", 42, RegistryValueKind.DWord);
        Set(@"Classes\Dead.File", "Big", 1234567890123L, RegistryValueKind.QWord);
        Set(@"Classes\Dead.File", "Expandable", "%SystemRoot%\\x", RegistryValueKind.ExpandString);
        Set(@"Classes\Dead.File", "Several", new[] { "one", "two" }, RegistryValueKind.MultiString);
        Set(@"Classes\Dead.File", "Bytes", new byte[] { 1, 2, 255 }, RegistryValueKind.Binary);

        // A healthy type, and one with a single dead action and a dead menu extension.
        Set(@"Classes\Live.File\shell\open\command", "", $"\"{_notepad}\" \"%1\"");
        Set(@"Classes\Mixed.File\shell\open\command", "", $"\"{_notepad}\" \"%1\"");
        Set(@"Classes\Mixed.File\shell\Old\command", "", $"\"{_gone}\" \"%1\"");
        Set(@"Classes\Mixed.File\shellex\ContextMenuHandlers\GoneHandler", "", HandlerId);

        // A dead action on a class Windows relies on, a dead Open-with program, and a program on a drive that is not there.
        Set(@"Classes\Directory\shell\GoneTool\command", "", $"\"{_gone}\" \"%1\"");
        Set(@"Classes\Applications\gone.exe\shell\open\command", "", $"\"{_gone}\" \"%1\"");
        Set(@"Classes\Unplugged.File\shell\open\command", "", $"\"{MissingDrive()}:\\tools\\tool.exe\" \"%1\"");

        // Extensions pointing at the dead and the healthy type.
        Set(@"Classes\.dead", "", "Dead.File");
        Set(@"Classes\.dead\OpenWithProgids", "Dead.File", "");
        Set(@"Classes\.dead\OpenWithProgids", "Live.File", "");
        Set(@"Classes\.live", "", "Live.File");
        Set(@"FileExts\.dead\OpenWithProgids", "Dead.File", "");
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_name, throwOnMissingSubKey: false);
        File.Delete(_backup);
    }

    [Fact]
    public void The_scan_finds_exactly_the_dead_entries()
    {
        var found = Scan();

        Assert.Equal(
            new[]
            {
                (LeftoverKind.FileType, $@"{_classes}\Applications\gone.exe", (string?)null),
                (LeftoverKind.FileType, $@"{_classes}\Dead.File", null),
                (LeftoverKind.MenuItem, $@"{_classes}\Directory\shell\GoneTool", null),
                (LeftoverKind.MenuItem, $@"{_classes}\Mixed.File\shell\Old", null),
                (LeftoverKind.MenuHandler, $@"{_classes}\Mixed.File\shellex\ContextMenuHandlers\GoneHandler", null),
                (LeftoverKind.FileTypeLink, $@"{_classes}\.dead", ""),
                (LeftoverKind.FileTypeLink, $@"{_classes}\.dead\OpenWithProgids", "Dead.File"),
                (LeftoverKind.FileTypeLink, $@"{_fileExts}\.dead\OpenWithProgids", "Dead.File"),
            },
            found.Select(l => (l.Kind, l.Action.KeyPath, l.Action.ValueName)).OrderBy(x => x.Kind).ThenBy(x => x.KeyPath, StringComparer.Ordinal));
        Assert.All(found.Where(l => l.Kind != LeftoverKind.MenuHandler), l => Assert.Equal(_gone, l.Program));
        Assert.Equal(_goneDll, found.Single(l => l.Kind == LeftoverKind.MenuHandler).Program);
    }

    [Fact]
    public void Removing_deletes_the_dead_entries_and_leaves_the_rest()
    {
        var result = LeftoverRemover.Remove(Scan(), _backup, [_sandbox]);

        Assert.Equal(8, result.Removed);
        Assert.Empty(result.Problems);
        Assert.False(Exists(@"Classes\Dead.File"));
        Assert.False(Exists(@"Classes\Applications\gone.exe"));
        Assert.False(Exists(@"Classes\Mixed.File\shell\Old"));
        Assert.False(Exists(@"Classes\Mixed.File\shellex\ContextMenuHandlers\GoneHandler"));
        Assert.False(Exists(@"Classes\Directory\shell\GoneTool"));
        Assert.Null(Get(@"Classes\.dead", ""));
        Assert.Null(Get(@"Classes\.dead\OpenWithProgids", "Dead.File"));

        Assert.True(Exists(@"Classes\Live.File\shell\open\command"));
        Assert.True(Exists(@"Classes\Mixed.File\shell\open\command"));
        Assert.True(Exists(@"Classes\Directory"));
        Assert.True(Exists(@"Classes\Unplugged.File\shell\open\command"));
        Assert.Equal("Live.File", Get(@"Classes\.live", ""));
        Assert.NotNull(Get(@"Classes\.dead\OpenWithProgids", "Live.File"));

        Assert.Empty(Scan());
    }

    [Fact]
    public void The_backup_puts_everything_back_when_imported()
    {
        LeftoverRemover.Remove(Scan(), _backup, [_sandbox]);

        using var import = Process.Start(new ProcessStartInfo("reg.exe", $"import \"{_backup}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true })!;
        import.WaitForExit();
        Assert.Equal(0, import.ExitCode);

        Assert.Equal("A \"dead\" type \\ with awkward characters", Get(@"Classes\Dead.File", ""));
        Assert.Equal($"\"{_gone}\" \"%1\"", Get(@"Classes\Dead.File\shell\open\command", ""));
        Assert.Equal($"{_gone},0", Get(@"Classes\Dead.File\DefaultIcon", ""));
        Assert.Equal(42, Get(@"Classes\Dead.File", "Number"));
        Assert.Equal(1234567890123L, Get(@"Classes\Dead.File", "Big"));
        Assert.Equal("%SystemRoot%\\x", Get(@"Classes\Dead.File", "Expandable", RegistryValueOptions.DoNotExpandEnvironmentNames));
        Assert.Equal(new[] { "one", "two" }, Get(@"Classes\Dead.File", "Several"));
        Assert.Equal(new byte[] { 1, 2, 255 }, Get(@"Classes\Dead.File", "Bytes"));
        Assert.Equal("Dead.File", Get(@"Classes\.dead", ""));
        Assert.NotNull(Get(@"Classes\.dead\OpenWithProgids", "Dead.File"));
        Assert.NotNull(Get(@"FileExts\.dead\OpenWithProgids", "Dead.File"));
        Assert.True(Exists(@"Classes\Mixed.File\shell\Old\command"));
        Assert.Equal(8, Scan().Count);
    }

    [Fact]
    public void Nothing_outside_the_allowed_part_of_the_registry_is_touched()
    {
        var result = LeftoverRemover.Remove(Scan(), _backup, [@"HKEY_CURRENT_USER\Software\RCConfigurator.Tests\somewhere-else"]);

        Assert.Equal(0, result.Removed);
        Assert.Equal(8, result.Problems.Count);
        Assert.Null(result.BackupFile);
        Assert.True(Exists(@"Classes\Dead.File"));
        Assert.False(File.Exists(_backup));
    }

    private IReadOnlyList<Leftover> Scan() =>
        LeftoverScanner.Scan([_classes], _fileExts, id => id == HandlerId ? _goneDll : null);

    private void Set(string path, string name, object value, RegistryValueKind kind = RegistryValueKind.String)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{_name}\{path}");
        key.SetValue(name, value, kind);
    }

    private object? Get(string path, string name, RegistryValueOptions options = RegistryValueOptions.None)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{_name}\{path}");
        return key?.GetValue(name, null, options);
    }

    private bool Exists(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{_name}\{path}");
        return key is not null;
    }

    private static char MissingDrive() =>
        Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c).First(c => !Directory.Exists($@"{c}:\"));
}
