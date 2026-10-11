namespace RCConfigurator.Core;

/// <summary>Works out which program a registered command line runs.</summary>
public static class CommandTarget
{
    private const string ExecutableSuffix = ".exe";

    /// <summary>
    /// The program at the start of a command line such as <c>"C:\App\app.exe" "%1"</c>, with
    /// environment variables expanded. Null when the line is empty or the program cannot be told
    /// apart from its arguments.
    /// </summary>
    public static string? ProgramOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        var line = Environment.ExpandEnvironmentVariables(command).TrimStart();
        string program;
        if (line[0] == '"')
        {
            var close = line.IndexOf('"', 1);
            if (close < 0) return null;
            program = line[1..close];
        }
        else
        {
            // Unquoted paths may contain spaces, as in C:\Program Files\App\app.exe /play %1,
            // so the program ends at the first ".exe" that is followed by a space or the end.
            var end = EndOfExecutable(line);
            program = end > 0 ? line[..end] : line.Split(' ', 2)[0];
        }

        program = program.Trim();
        return program.Length > 0 ? program : null;
    }

    private static int EndOfExecutable(string line)
    {
        var from = 0;
        while (true)
        {
            var at = line.IndexOf(ExecutableSuffix, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;

            var end = at + ExecutableSuffix.Length;
            if (end == line.Length || char.IsWhiteSpace(line[end])) return end;
            from = end;
        }
    }
}
