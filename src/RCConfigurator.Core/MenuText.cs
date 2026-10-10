using System.Text;

namespace RCConfigurator.Core;

public static class MenuText
{
    /// <summary>
    /// Turns a raw Win32 menu string into the title the user sees: drops the shortcut text
    /// after a tab, removes accelerator markers ("&amp;Open" becomes "Open") and keeps
    /// escaped ampersands ("Cut &amp;&amp; Paste" becomes "Cut &amp; Paste").
    /// </summary>
    public static string? Clean(string? raw)
    {
        if (raw is null) return null;

        var tab = raw.IndexOf('\t');
        var title = (tab >= 0 ? raw[..tab] : raw).TrimEnd();

        // East Asian menus put the accelerator after the title, as in "打开(&O)".
        if (title.Length >= 4 && title[^1] == ')' && title[^3] == '&' && title[^4] == '(')
        {
            title = title[..^4].TrimEnd();
        }

        var result = new StringBuilder(title.Length);
        for (var i = 0; i < title.Length; i++)
        {
            if (title[i] != '&')
            {
                result.Append(title[i]);
            }
            else if (i + 1 < title.Length && title[i + 1] == '&')
            {
                result.Append('&');
                i++;
            }
        }

        return result.ToString();
    }
}
