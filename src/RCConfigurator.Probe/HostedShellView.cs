using System.ComponentModel;
using System.Diagnostics;
using static RCConfigurator.Probe.NativeMethods;

namespace RCConfigurator.Probe;

/// <summary>
/// Hosts a real Explorer folder view in a window that is never shown. The background menu of a
/// real view includes the items the view itself adds (View, Sort by, Refresh, Paste), which the
/// folder alone does not report.
/// </summary>
internal sealed class HostedShellView : IDisposable
{
    private const int Width = 800;
    private const int Height = 600;
    private const int ViewTimeoutMs = 5000;

    private readonly IntPtr _window;
    private readonly IExplorerBrowser _browser;
    private readonly IShellView _view;

    private HostedShellView(IntPtr window, IExplorerBrowser browser, IShellView view)
    {
        _window = window;
        _browser = browser;
        _view = view;
    }

    public static HostedShellView Open(IntPtr pidl, bool isDesktop)
    {
        var window = CreateWindowExW(0, "STATIC", null, WS_POPUP | WS_CLIPCHILDREN, 0, 0, Width, Height, 0, 0, 0, 0);
        if (window == 0) throw new Win32Exception();

        IExplorerBrowser? browser = null;
        try
        {
            browser = (IExplorerBrowser)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ExplorerBrowser, throwOnError: true)!)!;
            var rect = new RECT { right = Width, bottom = Height };
            var settings = new FOLDERSETTINGS { ViewMode = FVM_DETAILS, fFlags = isDesktop ? FWF_DESKTOP : 0 };
            Check(browser.Initialize(window, ref rect, ref settings), "create the folder view");
            Check(browser.BrowseToIDList(pidl, 0), "open the folder in the view");
            return new HostedShellView(window, browser, WaitForView(browser));
        }
        catch
        {
            browser?.Destroy();
            DestroyWindow(window);
            throw;
        }
    }

    public object GetBackgroundMenu()
    {
        Check(_view.GetItemObject(SVGIO_BACKGROUND, ref IID_IContextMenu, out var menu), "get the background menu");
        return menu!;
    }

    public void Dispose()
    {
        _browser.Destroy();
        DestroyWindow(_window);
    }

    private static IShellView WaitForView(IExplorerBrowser browser)
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            if (browser.GetCurrentView(ref IID_IShellView, out var view) >= 0 && view is IShellView shellView)
            {
                return shellView;
            }

            if (timer.ElapsedMilliseconds > ViewTimeoutMs)
            {
                throw new TimeoutException("The folder view did not open in time.");
            }

            PumpMessages();
            Thread.Sleep(10);
        }
    }

    private static void PumpMessages()
    {
        while (PeekMessageW(out var message, 0, 0, 0, PM_REMOVE))
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }
}
