using System.Net;
using System.Net.Http.Headers;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Microsoft.Extensions.Logging;

namespace Gentastic.Models;

/// <summary>Options for the Hugging Face repository. Wired from settings in the app layer.</summary>
public sealed class HuggingFaceOptions
{
    /// <summary>Where models are cached. Defaults to <c>%LOCALAPPDATA%\Gentastic\models</c>.</summary>
    public string? CacheRoot { get; set; }

    /// <summary>Resolves the current HF access token (for gated models). Defaults to the
    /// <c>HF_TOKEN</c> environment variable.</summary>
    public Func<string?>? TokenProvider { get; set; }
}

/// <summary>
/// Downloads model files straight from the Hugging Face hub (plain HTTPS: no Python, no hub client)
/// and caches them under a per-repo directory tree so shared encoders/VAE are fetched once and
/// reused across models. Downloads stream to a <c>.part</c> file and are atomically moved on success.
/// </summary>
public sealed class HuggingFaceModelRepository : IModelRepository
{
    private const string Host = "https://huggingface.co";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HuggingFaceModelRepository> _logger;
    private readonly Func<string?> _tokenProvider;

    public string CacheRoot { get; }

    public HuggingFaceModelRepository(
        IHttpClientFactory httpFactory,
        ILogger<HuggingFaceModelRepository> logger,
        HuggingFaceOptions? options = null)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        options ??= new HuggingFaceOptions();
        CacheRoot = options.CacheRoot ?? DefaultCacheRoot();
        _tokenProvider = options.TokenProvider ?? (static () => Environment.GetEnvironmentVariable("HF_TOKEN"));
    }

    public bool IsInstalled(ModelSpec model) => model.Files.All(f => File.Exists(LocalPath(f)));

    public async Task<ModelInstallation> EnsureInstalledAsync(
        ModelSpec model,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var paths = new Dictionary<ModelFileRole, string>();
        for (var i = 0; i < model.Files.Count; i++)
        {
            var file = model.Files[i];
            var local = LocalPath(file);
            if (!File.Exists(local))
                await DownloadAsync(file, local, i + 1, model.Files.Count, progress, ct).ConfigureAwait(false);
            paths[file.Role] = local;
        }

        return new ModelInstallation(model, paths);
    }

    public IReadOnlyList<ModelSpec> GetInstalledModels(IEnumerable<ModelSpec> known) =>
        known.Where(IsInstalled).ToList();

    /// <summary>Removes the model-specific transformer. Shared encoders/VAE are intentionally kept
    /// because other catalog models depend on them (ref-counted cleanup is a follow-up).</summary>
    public void Delete(ModelSpec model)
    {
        foreach (var file in model.Files.Where(f => f.Role == ModelFileRole.DiffusionModel))
        {
            var local = LocalPath(file);
            if (File.Exists(local))
            {
                File.Delete(local);
                _logger.LogInformation("Deleted {Path}", local);
            }
        }
    }

    public long GetCacheSizeBytes()
    {
        if (!Directory.Exists(CacheRoot))
            return 0;

        return Directory
            .EnumerateFiles(CacheRoot, "*", SearchOption.AllDirectories)
            .Sum(p => new FileInfo(p).Length);
    }

    private async Task DownloadAsync(
        ModelFile file, string local, int index, int count,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        var url = $"{Host}/{file.Repo}/resolve/{file.Revision}/{file.Path}";
        var tempPath = local + ".part";
        var fileName = Path.GetFileName(file.Path);

        // Resume a prior partial download if a .part file is present.
        var resumeFrom = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;
        _logger.LogInformation("Downloading {Url}{Resume}", url,
            resumeFrom > 0 ? $" (resuming at {resumeFrom} bytes)" : string.Empty);

        var client = _httpFactory.CreateClient("huggingface");
        client.Timeout = Timeout.InfiniteTimeSpan; // large files stream past the default 100s

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var token = _tokenProvider();
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (resumeFrom > 0)
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);

        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException(
                $"'{file.Repo}' is gated. Accept the model's license on huggingface.co, then set a "
                + "Hugging Face token in Settings (or the HF_TOKEN environment variable).");

        response.EnsureSuccessStatusCode();

        // Append only when the server honoured the range; otherwise it sent the full file, so restart.
        var append = resumeFrom > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append)
            resumeFrom = 0;

        var total = response.Content.Headers.ContentRange?.Length
                    ?? (response.Content.Headers.ContentLength is { } len ? resumeFrom + len : null);

        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var destination = new FileStream(
            tempPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[1 << 20];
            var received = resumeFrom;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                progress?.Report(new DownloadProgress(fileName, index, count, received, total));
            }
        }

        // Verify the download when the catalog provides an expected hash; drop the corrupt part.
        if (!string.IsNullOrWhiteSpace(file.Sha256))
        {
            try
            {
                await Checksum.VerifyAsync(tempPath, file.Sha256, ct).ConfigureAwait(false);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        File.Move(tempPath, local, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private string LocalPath(ModelFile file) =>
        Path.Combine(CacheRoot, file.Repo.Replace('/', Path.DirectorySeparatorChar), file.Path);

    private static string DefaultCacheRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Gentastic", "models");
}
