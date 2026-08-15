using System.Net.Http.Headers;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace centre_app;

public sealed record UpdateInfo(string Version, string ReleaseUrl, string Name);

public static class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/bin-art-666/centre_app/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseUrl, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
        var url = root.GetProperty("html_url").GetString();
        if (!Version.TryParse(tag, out var remote) || string.IsNullOrWhiteSpace(url)) return null;
        var local = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        if (remote <= local) return null;
        var name = root.TryGetProperty("name", out var releaseName) ? releaseName.GetString() : null;
        return new UpdateInfo(tag!, url, string.IsNullOrWhiteSpace(name) ? $"Centre {tag}" : name!);
    }

    internal static bool IsNewer(string remoteVersion, Version localVersion) =>
        Version.TryParse(remoteVersion.TrimStart('v', 'V'), out var remote) && remote > localVersion;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Centre", "1.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
