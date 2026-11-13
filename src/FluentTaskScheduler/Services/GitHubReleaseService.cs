using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FluentTaskScheduler.Services
{
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";
    }

    public static class GitHubReleaseService
    {
        private static readonly HttpClient _http = new()
        {
            DefaultRequestHeaders =
            {
                { "User-Agent", "FluentTaskScheduler" },
                { "Accept", "application/vnd.github+json" }
            },
            Timeout = TimeSpan.FromSeconds(10)
        };

        private const string ApiUrl =
            "https://api.github.com/repos/TRGamer-tech/FluentTaskScheduler/releases/latest";

        /// <summary>
        /// Fetches the latest GitHub release. Returns null on any error (network, parse, etc.).
        /// </summary>
        public static async Task<GitHubRelease?> GetLatestReleaseAsync()
        {
            try
            {
                string json = await _http.GetStringAsync(ApiUrl);
                return JsonSerializer.Deserialize<GitHubRelease>(json);
            }
            catch (Exception ex)
            {
                Serilog.Log.Information("{Message}", $"[GitHubReleaseService] Could not fetch release notes: {ex.Message}");
                return null;
            }
        }
    }
}
