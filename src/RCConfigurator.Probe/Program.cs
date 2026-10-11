using System.Text;
using RCConfigurator.Core;

namespace RCConfigurator.Probe;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitFailed = 2;

    private const string Usage = """
        rcprobe - reads the right-click menu Windows builds for a target.
        It only reads the menu. It never shows it and never runs a command.

        Usage:
          rcprobe [options] <path> [<path>...]     Menu for the selected files, folders or drives
          rcprobe [options] --each <path>...       A separate menu for each path, one JSON line per path
          rcprobe [options] --background <folder>  Menu for the empty area inside a folder
          rcprobe [options] --desktop              Menu for the empty area of the desktop

        Options:
          --extended      Include items that only appear while Shift is held
          --no-submenus   Do not open submenus
          --tree          Print a readable tree instead of JSON
          --indent        Indent the JSON
        """;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? ExitUsage : ExitOk;
        }

        ProbeOptions options;
        var tree = false;
        var indent = false;
        var each = false;
        try
        {
            options = ParseArguments(args, out tree, out indent, out each);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine("Run rcprobe --help for usage.");
            return ExitUsage;
        }

        NativeMethods.OleInitialize(0);
        try
        {
            if (each) return ReadEach(options);

            var snapshot = ContextMenuReader.Read(options);
            if (tree)
            {
                PrintTree(snapshot.Items, depth: 0);
            }
            else
            {
                Console.WriteLine(SnapshotJson.Serialize(snapshot, indent));
            }

            return ExitOk;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return ExitFailed;
        }
        finally
        {
            NativeMethods.OleUninitialize();
        }
    }

    /// <summary>
    /// Reads one menu per path and writes each as a line of JSON, in the order given. Loading the
    /// shell extensions once and reusing them makes this far quicker than a run per file. A path
    /// whose menu cannot be read still gets a line, with no items, so lines and paths stay paired.
    /// </summary>
    private static int ReadEach(ProbeOptions options)
    {
        foreach (var path in options.Paths)
        {
            var single = options with { Paths = [path] };
            MenuSnapshot snapshot;
            try
            {
                snapshot = ContextMenuReader.Read(single);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{path}: {e.Message}");
                snapshot = new MenuSnapshot { Kind = TargetKind.Items, Paths = [path], ExtendedVerbs = options.ExtendedVerbs };
            }

            Console.WriteLine(SnapshotJson.Serialize(snapshot));
        }

        return ExitOk;
    }

    private static ProbeOptions ParseArguments(string[] args, out bool tree, out bool indent, out bool each)
    {
        tree = false;
        indent = false;
        each = false;
        var background = false;
        var desktop = false;
        var extended = false;
        var expandSubmenus = true;
        var paths = new List<string>();

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--background": background = true; break;
                case "--desktop": desktop = true; break;
                case "--extended": extended = true; break;
                case "--no-submenus": expandSubmenus = false; break;
                case "--tree": tree = true; break;
                case "--indent": indent = true; break;
                case "--each": each = true; break;
                default:
                    if (arg.StartsWith("--")) throw new ArgumentException($"Unknown option '{arg}'.");
                    paths.Add(Path.GetFullPath(arg));
                    break;
            }
        }

        if (desktop)
        {
            if (background || paths.Count > 0) throw new ArgumentException("--desktop takes no path and cannot be combined with --background.");
            return new ProbeOptions(TargetKind.Desktop, [], extended, expandSubmenus);
        }

        if (background)
        {
            if (paths.Count != 1) throw new ArgumentException("--background needs exactly one folder.");
            if (!Directory.Exists(paths[0])) throw new ArgumentException($"'{paths[0]}' is not a folder.");
            return new ProbeOptions(TargetKind.FolderBackground, paths, extended, expandSubmenus);
        }

        if (paths.Count == 0) throw new ArgumentException("Give at least one path, or use --background or --desktop.");
        foreach (var path in paths)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) throw new ArgumentException($"'{path}' does not exist.");
        }

        // Explorer can only select several items together when they sit in the same folder.
        var parent = Path.GetDirectoryName(paths[0]);
        if (!each && paths.Any(p => !string.Equals(Path.GetDirectoryName(p), parent, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("When several paths are given they must all be in the same folder.");
        }

        return new ProbeOptions(TargetKind.Items, paths, extended, expandSubmenus);
    }

    private static void PrintTree(IReadOnlyList<MenuEntry> items, int depth)
    {
        var indent = new string(' ', depth * 4);
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                Console.WriteLine($"{indent}--------");
                continue;
            }

            var line = new StringBuilder(indent).Append(item.Text ?? "(no text)");
            if (item.IsSubmenu) line.Append("  >");
            if (item.Verb is not null) line.Append($"  [{item.Verb}]");
            if (item.IsDefault) line.Append("  (default)");
            if (item.IsDisabled) line.Append("  (disabled)");
            if (item.IsChecked) line.Append("  (checked)");
            Console.WriteLine(line);

            PrintTree(item.Children, depth + 1);
        }
    }
}
