using Gentastic.Core.Models;

namespace Gentastic.Core.Abstractions;

/// <summary>Downloads model files from Hugging Face and manages the local cache.</summary>
public interface IModelRepository
{
    string CacheRoot { get; }

    bool IsInstalled(ModelSpec model);

    /// <summary>Ensures every file for <paramref name="model"/> exists locally, downloading what is
    /// missing, and returns the resolved local paths.</summary>
    Task<ModelInstallation> EnsureInstalledAsync(
        ModelSpec model,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    IReadOnlyList<ModelSpec> GetInstalledModels(IEnumerable<ModelSpec> known);

    void Delete(ModelSpec model);

    long GetCacheSizeBytes();
}
