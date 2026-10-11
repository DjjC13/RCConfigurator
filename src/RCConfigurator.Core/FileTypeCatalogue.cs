using System.Text.Json;

namespace RCConfigurator.Core;

/// <summary>
/// Sorts file types into groups using two public lists, for the types Windows itself does not
/// label: mime-db, which maps extensions to IANA media types, and GitHub Linguist's list of
/// languages, which says which extensions hold code. Windows' own label always comes first.
/// </summary>
public static class FileTypeCatalogue
{
    public const string Code = "Code";
    public const string TextFiles = "Text files";

    // The first part of a media type, such as "image" in image/png, and the group it belongs to.
    // "application" is left out on purpose: it covers everything from fonts to installers, and
    // neither list says anything more specific about those types.
    private static readonly Dictionary<string, string> GroupOfMediaFamily = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image"] = "Images",
        ["video"] = "Videos",
        ["audio"] = "Audio",
        ["text"] = TextFiles,
        ["font"] = "Fonts",
        ["model"] = "3D models",
    };

    /// <summary>Reads mime-db's db.json into extension to media type, for example ".png" to "image/png".</summary>
    public static IReadOnlyDictionary<string, string> ParseMimeDb(string json)
    {
        var mediaTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        foreach (var mediaType in document.RootElement.EnumerateObject())
        {
            if (mediaType.Value.ValueKind != JsonValueKind.Object
                || !mediaType.Value.TryGetProperty("extensions", out var extensions)
                || extensions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var extension in extensions.EnumerateArray())
            {
                if (extension.GetString() is { Length: > 0 } name) mediaTypes.TryAdd("." + name, mediaType.Name);
            }
        }

        return mediaTypes;
    }

    /// <summary>
    /// Reads Linguist's languages.yml into extension to language type, which is one of
    /// programming, markup, data or prose. Only the few keys needed are read, so no YAML library is used.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseLinguist(string yaml)
    {
        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? type = null;
        var inExtensions = false;
        foreach (var line in yaml.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (text.Length == 0 || text[0] == '#') continue;

            if (!char.IsWhiteSpace(text[0]))
            {
                // A new language starts; its keys follow, indented.
                type = null;
                inExtensions = false;
                continue;
            }

            var entry = text.Trim();
            if (entry.StartsWith("type:", StringComparison.Ordinal))
            {
                type = entry["type:".Length..].Trim();
                inExtensions = false;
            }
            else if (entry == "extensions:")
            {
                inExtensions = true;
            }
            else if (inExtensions && entry.StartsWith('-'))
            {
                var extension = entry[1..].Trim().Trim('"', '\'');
                if (type is not null && extension.StartsWith('.')) types.TryAdd(extension, type);
            }
            else
            {
                inExtensions = false;
            }
        }

        return types;
    }

    /// <summary>The group the two lists put an extension in, or null if neither says anything useful.</summary>
    public static string? GroupFor(
        string extension,
        IReadOnlyDictionary<string, string> mediaTypes,
        IReadOnlyDictionary<string, string> languageTypes)
    {
        languageTypes.TryGetValue(extension, out var language);

        // Code first: several source-file extensions also have an unrelated media type.
        if (language is "programming" or "markup") return Code;

        if (mediaTypes.TryGetValue(extension, out var mediaType)
            && GroupOfMediaFamily.TryGetValue(mediaType.Split('/')[0], out var group))
        {
            return group;
        }

        return language is not null ? TextFiles : null;
    }

    /// <summary>
    /// Adds every type Windows left unlabelled to the group the lists put it in. Types Windows
    /// did label stay where Windows put them.
    /// </summary>
    public static IReadOnlyList<FileKind> Extend(
        IReadOnlyList<FileKind> windowsKinds,
        IEnumerable<string> allExtensions,
        IReadOnlyDictionary<string, string> mediaTypes,
        IReadOnlyDictionary<string, string> languageTypes)
    {
        var labelled = new HashSet<string>(windowsKinds.SelectMany(k => k.Extensions), StringComparer.OrdinalIgnoreCase);
        var names = windowsKinds.Select(k => k.Name).ToList();
        var members = windowsKinds.ToDictionary(k => k.Name, k => k.Extensions.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var extension in allExtensions.Where(e => !labelled.Contains(e)))
        {
            if (GroupFor(extension, mediaTypes, languageTypes) is not { } group) continue;

            if (!members.TryGetValue(group, out var list))
            {
                members[group] = list = [];
                names.Add(group);
            }

            list.Add(extension);
        }

        return names.Select(name => new FileKind { Name = name, Extensions = members[name], IsBuiltIn = true }).ToList();
    }
}
