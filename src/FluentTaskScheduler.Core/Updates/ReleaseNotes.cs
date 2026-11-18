using System.Text.Json;

namespace FluentTaskScheduler.Updates;

/// <summary>What a release says about itself.</summary>
/// <param name="Version">The release the notes belong to, e.g. "1.9.0".</param>
/// <param name="Title">The name the release was given, without its version number; empty if it has none.</param>
/// <param name="Markdown">The notes as they were written, in Markdown.</param>
/// <param name="Url">The page of the release.</param>
public sealed record ReleaseNotes(string Version, string Title, string Markdown, string Url);

public interface IReleaseNotesSource
{
    /// <summary>
    /// The notes of the release numbered <paramref name="version"/> ("1.9.0"). Null when that
    /// release has none or they cannot be had right now; this never throws for lack of a connection.
    /// </summary>
    Task<ReleaseNotes?> FindAsync(string version);
}

/// <summary>Reads release notes from the project's releases on GitHub, where a release is tagged "V" plus its version.</summary>
public sealed class GitHubReleaseNotes : IReleaseNotesSource, IDisposable
{
    private const string ReleasesByTag = "https://api.github.com/repos/" + AppInfo.Repository + "/releases/tags/";

    private readonly HttpClient _http;

    /// <param name="handler">Answers the requests in place of the network; for tests.</param>
    public GitHubReleaseNotes(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(10);

        // GitHub turns away requests that do not say who is asking.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FluentTaskScheduler");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<ReleaseNotes?> FindAsync(string version)
    {
        string tag = "V" + version;
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(ReleasesByTag + tag);
            if (!response.IsSuccessStatusCode)
            {
                Serilog.Log.Information("No release notes for {Tag}: GitHub answered {Status}", tag, (int)response.StatusCode);
                return null;
            }

            using JsonDocument release = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string notes = Text(release, "body");
            return string.IsNullOrWhiteSpace(notes) ? null : new ReleaseNotes(version, TitleWithoutTag(Text(release, "name"), tag), notes, Text(release, "html_url"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Serilog.Log.Information(ex, "The release notes for {Tag} could not be fetched", tag);
            return null;
        }
    }

    public void Dispose() => _http.Dispose();

    private static string Text(JsonDocument release, string property) =>
        release.RootElement.ValueKind == JsonValueKind.Object
        && release.RootElement.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : "";

    // Releases are named like "V1.9.0 - The Chain Reaction Update", or just "V1.9.0".
    private static string TitleWithoutTag(string name, string tag)
    {
        string title = name.Trim();
        if (title.StartsWith(tag, StringComparison.OrdinalIgnoreCase)) title = title[tag.Length..];
        return title.TrimStart(' ', '-', ':', '\u2013', '\u2014');
    }
}
