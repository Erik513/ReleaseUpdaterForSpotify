using System.Net.Http.Headers;
using System.Text.Json;
using ReleaseUpdater.Core.Models;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdaterGui
{
    /// <summary>
    /// Checks GitHub's "latest release" endpoint for a newer tagged version. Failures
    /// (offline, rate limited, malformed response) are swallowed and reported as "no
    /// update found" since this check is a best-effort convenience, never a requirement.
    /// </summary>
    public sealed class GitHubUpdateChecker
    {
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/Erik513/ReleaseUpdaterGui/releases/latest";

        private readonly HttpClient httpClient;

        public GitHubUpdateChecker(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        public async Task<UpdateCheckResult?> CheckForUpdateAsync(
            Version currentVersion,
            CancellationToken cancellationToken)
        {
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, ReleasesApiUrl);
                request.Headers.UserAgent.Add(
                    new ProductInfoHeaderValue("ReleaseUpdaterGui", currentVersion.ToString()));
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                using HttpResponseMessage response = await httpClient.SendAsync(
                    request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using Stream stream =
                    await response.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument json = await JsonDocument.ParseAsync(
                    stream, cancellationToken: cancellationToken);

                JsonElement root = json.RootElement;
                string? tagName = root.TryGetProperty("tag_name", out JsonElement tagElement)
                    ? tagElement.GetString()
                    : null;
                string? releaseUrl = root.TryGetProperty("html_url", out JsonElement urlElement)
                    ? urlElement.GetString()
                    : null;
                string? downloadUrl = GetExeAssetDownloadUrl(root);

                return UpdateVersionParser.TryParseNewerRelease(
                    tagName,
                    releaseUrl,
                    downloadUrl,
                    currentVersion);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Finds the .exe attached to the release, if any, so it can be downloaded and
        /// swapped in automatically. Falls back to null (the caller then just links to
        /// the release page) if no exe asset was attached.
        /// </summary>
        private static string? GetExeAssetDownloadUrl(JsonElement releaseRoot)
        {
            if (!releaseRoot.TryGetProperty("assets", out JsonElement assets) ||
                assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString()
                    : null;

                if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (asset.TryGetProperty("browser_download_url", out JsonElement urlElement))
                {
                    return urlElement.GetString();
                }
            }

            return null;
        }
    }
}
