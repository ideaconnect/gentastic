using System.IO.Compression;
using System.Net.Http;

namespace Gentastic.Models;

/// <summary>Progress for the CUDA runtime download.</summary>
public sealed record CudaDownloadProgress(int FileIndex, int FileCount, long BytesReceived, long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value : null;
}

/// <summary>
/// Lets the NVIDIA CUDA backend run WITHOUT the CUDA Toolkit installed, by downloading the redistributable
/// runtime libraries on demand (kept out of the base build to keep it small).
///
/// The bundled CUDA backend native (runtimes/win-x64/native/cuda12/stable-diffusion.dll) dynamically links
/// cudart64_12.dll and cuBLAS (cublas64_12 + cublasLt64_12). Those are NVIDIA redistributables - we fetch
/// them from NVIDIA's official redist archive into %LOCALAPPDATA%\Gentastic\cuda-runtime, write a version.json
/// so StableDiffusion.NET's CudaBackend detects "CUDA 12", and at startup point CUDA_PATH + the DLL search
/// path at that folder. The driver API (nvcuda.dll) ships with the NVIDIA driver, so nothing else is needed.
/// </summary>
public sealed class CudaRuntime
{
    private const string RedistBase = "https://developer.download.nvidia.com/compute/cuda/redist";

    // CUDA 12.8 redistributables - the toolkit version the backend native was built against (12.8.1).
    private static readonly (string Url, string[] Dlls)[] Components =
    [
        ($"{RedistBase}/cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-12.8.90-archive.zip",
            new[] { "cudart64_12.dll" }),
        ($"{RedistBase}/libcublas/windows-x86_64/libcublas-windows-x86_64-12.8.4.1-archive.zip",
            new[] { "cublas64_12.dll", "cublasLt64_12.dll" }),
    ];

    // StableDiffusion.NET's CudaBackend reads <CUDA_PATH>/version.json and parses libcublas.version's major.
    private const string VersionJson = "{\"libcublas\":{\"version\":\"12.8.4.1\"}}";

    private static readonly string[] RequiredDlls = ["cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll"];

    /// <summary>Where the downloaded runtime lives.</summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gentastic", "cuda-runtime");

    /// <summary>Approximate total download size (~567 MB, dominated by cuBLAS).</summary>
    public const long ApproxDownloadBytes = 567_000_000;

    public static bool IsInstalled =>
        File.Exists(Path.Combine(Directory, "version.json"))
        && RequiredDlls.All(dll => File.Exists(Path.Combine(Directory, dll)));

    /// <summary>Points CUDA_PATH + the process DLL search path at the downloaded runtime so the bundled
    /// CUDA backend loads without the toolkit. No-op if a real toolkit is already present (system CUDA_PATH
    /// set) or the runtime isn't downloaded. Must run before the engine's first native call.</summary>
    public static void ActivateIfInstalled()
    {
        if (!IsInstalled)
            return;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUDA_PATH")))
            return; // a real CUDA Toolkit is installed - leave it alone

        Environment.SetEnvironmentVariable("CUDA_PATH", Directory);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable("PATH", Directory + Path.PathSeparator + path);
    }

    /// <summary>Downloads NVIDIA's redistributable cudart + cuBLAS and extracts the DLLs into
    /// <see cref="Directory"/>. Safe to re-run; leaves a valid install only on success.</summary>
    public async Task InstallAsync(IProgress<CudaDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Directory);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var tmp = Path.Combine(Directory, "_download.tmp");

        for (var i = 0; i < Components.Length; i++)
        {
            var (url, dlls) = Components[i];

            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dest = File.Create(tmp);
                var buffer = new byte[1 << 20];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new CudaDownloadProgress(i + 1, Components.Length, received, total));
                }
            }

            // NVIDIA redist archives store the DLLs under <archive-root>/bin/, so match by file name.
            using (var zip = ZipFile.OpenRead(tmp))
            {
                foreach (var dll in dlls)
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(dll, StringComparison.OrdinalIgnoreCase))
                                ?? throw new InvalidOperationException($"{dll} not found in {Path.GetFileName(url)}.");
                    entry.ExtractToFile(Path.Combine(Directory, dll), overwrite: true);
                }
            }

            File.Delete(tmp);
        }

        await File.WriteAllTextAsync(Path.Combine(Directory, "version.json"), VersionJson, ct).ConfigureAwait(false);
    }

    public void Delete()
    {
        if (System.IO.Directory.Exists(Directory))
            System.IO.Directory.Delete(Directory, recursive: true);
    }
}
