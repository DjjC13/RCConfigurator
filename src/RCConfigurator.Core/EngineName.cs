using System.Text;

namespace RCConfigurator.Core;

/// <summary>
/// Menu titles as Nilesoft Shell sees them when it matches rules against items. This mirrors
/// the engine's own normalisation, so a name produced here matches the item it came from.
/// </summary>
public static class EngineName
{
    public static string From(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText)) return "";

        // The engine trims trailing dots and surrounding control characters first, then stops
        // at the shortcut text and drops accelerator markers.
        var text = rawText.TrimEnd('.').Trim(c => char.IsControl(c));
        var name = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\0' or '\t' or '\b') break;

            if (c == '&')
            {
                if (i + 1 >= text.Length || text[i + 1] != '&') continue;
                i++;
            }

            name.Append(c);
        }

        return name.ToString();
    }

    /// <summary>The path the engine gives a submenu's items: lower-case parent names joined by slashes.</summary>
    public static string Path(IEnumerable<string> parentNames) =>
        string.Join('/', parentNames.Select(n => n.ToLowerInvariant()));

    private static string Trim(this string text, Func<char, bool> shouldTrim)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && shouldTrim(text[start])) start++;
        while (end > start && shouldTrim(text[end - 1])) end--;
        return text[start..end];
    }
}
