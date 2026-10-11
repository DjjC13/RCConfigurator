using System.IO;
using System.Net.Http;
using System.Text.Json;
using RCConfigurator.Core;

namespace RCConfigurator.App;

/// <summary>
/// Fetches the two public lists used to sort the file types Windows does not label, and keeps a
/// copy so they are only downloaded now and then. Whole lists are downloaded and matched on this
/// PC, so nothing about the PC's own file types is sent anywhere.
/// </summary>
public static class OnlineFileTypes
{
    /// <summary>mime-db: extensions and their IANA media types, as published on npm.</summary>
    public const string MimeDbUrl = "https://cdn.jsdelivr.net/npm/mime-db/db.json";

    /// <summary>GitHub Linguist's list of languages and the extensions their files use.</summary>
    public const string LinguistUrl = "https://raw.githubusercontent.com/github-linguist/linguist/main/lib/linguist/languages.yml";

    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);
    private static readonly HttpClient Http = CreateClient();
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RCConfigurator", "file-type-lists");

    public sealed record Lists(
        IReadOnlyDictionary<string, string> MediaTypes,
        IReadOnlyDictionary<string, string> LanguageTypes);

    /// <summary>
    /// Returns the lists, downloading any that have no recent copy. A list that cannot be had
    /// comes back empty; the result is null when neither is available.
    /// </summary>
    /// <param name="offline">True to use only copies already kept and never touch the network.</param>
    public static async Task<Lists?> LoadAsync(bool offline)
    {
        var mimeDb = await GetAsync("mime-db.json", MimeDbUrl, offline);
        var linguist = await GetAsync("languages.yml", LinguistUrl, offline);
        if (mimeDb is null && linguist is null) return null;

        IReadOnlyDictionary<string, string> mediaTypes = new Dictionary<string, string>();
        try
        {
            if (mimeDb is not null) mediaTypes = FileTypeCatalogue.ParseMimeDb(mimeDb);
        }
        catch (JsonException)
        {
            // A damaged download; carry on with the other list.
        }

        return new Lists(mediaTypes, linguist is null ? new Dictionary<string, string>() : FileTypeCatalogue.ParseLinguist(linguist));
    }

    private static async Task<string?> GetAsync(string name, string url, bool offline)
    {
        var path = Path.Combine(Folder, name);
        try
        {
            var isRecent = File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < KeepFor;
            if (!isRecent && !offline)
            {
                try
                {
                    var text = await Http.GetStringAsync(url);
                    Directory.CreateDirectory(Folder);
                    await File.WriteAllTextAsync(path, text);
                    return text;
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
                {
                    // No network or the host is down: an older copy is better than nothing.
                }
            }

            return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RCConfigurator");
        return client;
    }
}
