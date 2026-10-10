using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace RCConfigurator.App;

/// <summary>
/// Restarts Windows Explorer. The engine reads its config only when Explorer starts, so this is
/// how new rules take effect. It needs no administrator rights: Explorer runs as the user.
/// </summary>
public static class ExplorerRestart
{
    // The message the taskbar's own "Exit Explorer" command sends. Explorer saves its state and
    // exits normally, which also stops Windows restarting it behind our back.
    private const uint ExitExplorerMessage = 0x5B4;

    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TaskbarTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartItOurselvesAfter = TimeSpan.FromSeconds(4);

    /// <summary>Restarts Explorer and waits for the taskbar to come back. Returns false if it did not.</summary>
    public static bool Restart()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var explorers = Process.GetProcessesByName("explorer").Where(p => p.SessionId == session).ToList();

        var taskbar = FindWindowW("Shell_TrayWnd", null);
        if (taskbar != 0) PostMessageW(taskbar, ExitExplorerMessage, 0, 0);

        // Give the shell a moment to leave by itself, then end whatever is left. That includes
        // folder windows running in their own process, which would keep the old rules.
        var exitedCleanly = taskbar != 0;
        var deadline = Stopwatch.StartNew();
        foreach (var explorer in explorers)
        {
            try
            {
                var remaining = ExitTimeout - deadline.Elapsed;
                if (remaining > TimeSpan.Zero && explorer.WaitForExit(remaining)) continue;

                exitedCleanly = false;
                explorer.Kill();
                explorer.WaitForExit(ExitTimeout);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                // It had already gone.
            }
        }

        // After a normal exit nothing brings the shell back, so start it now. After a forced one
        // Windows does that itself, and starting a second copy would only open a folder window.
        var startedOurselves = false;
        if (exitedCleanly)
        {
            StartExplorer();
            startedOurselves = true;
        }

        var timer = Stopwatch.StartNew();
        TimeSpan? goneSince = null;
        while (timer.Elapsed < TaskbarTimeout)
        {
            if (FindWindowW("Shell_TrayWnd", null) != 0) return true;

            if (Process.GetProcessesByName("explorer").Any(p => p.SessionId == session))
            {
                goneSince = null;
            }
            else
            {
                goneSince ??= timer.Elapsed;
                if (!startedOurselves && timer.Elapsed - goneSince > StartItOurselvesAfter)
                {
                    StartExplorer();
                    startedOurselves = true;
                }
            }

            Thread.Sleep(200);
        }

        return false;
    }

    // Started through the shell so that Explorer does not inherit this process's handles.
    private static void StartExplorer() =>
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        {
            UseShellExecute = true,
        })?.Dispose();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
