using System.Text.Json;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Update;

namespace Gentastic.Models;

public sealed class GitHubUpdateOptions
{
    /// <summary>"owner/repo" whose GitHub Releases are checked.</summary>
    public string Repository { get; set; } = "ideaconnect/gentastic";

    /// <summary>The running app's version, compared against the latest release tag.</summary>
    public Version CurrentVersion { get; set; } = new(0, 0, 0);
}

/// <summary>Checks the repository's latest GitHub Release and compares its tag to the running version.</summary>
public sealed class GitHubUpdateService(IHttpClientFactory httpFactory, GitHubUpdateOptions options)
    : IUpdateService
{
    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        var client = httpFactory.CreateClient("github");
        var url = $"https://api.github.com/repos/{options.Repository}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Gentastic-Updater");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            // 404 = no releases published yet; treat as "up to date".
            return new UpdateInfo(options.CurrentVersion, null, null, null);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;

        return new UpdateInfo(options.CurrentVersion, ParseVersion(tag), tag, htmlUrl);
    }

    /// <summary>Parses a release tag such as "v1.2.0" or "1.2.0-beta" into a <see cref="Version"/>.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var text = tag.Trim().TrimStart('v', 'V');
        var dash = text.IndexOf('-'); // drop any pre-release suffix
        if (dash >= 0)
            text = text[..dash];

        return Version.TryParse(text, out var version) ? version : null;
    }
}
