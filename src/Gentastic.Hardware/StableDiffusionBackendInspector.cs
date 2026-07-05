using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using StableDiffusion.NET;

namespace Gentastic.Hardware;

/// <summary>
/// Default <see cref="IBackendInspector"/>. Runtime presence is delegated to StableDiffusion.NET's
/// own <c>Backends.*.IsAvailable</c> checks (which read CUDA_PATH / the AMD HIP SDK's HIP_PATH / OS
/// + architecture — no native library is loaded). Bundling is a file check for the backend's native
/// variant folder next to the app. Everything here is cheap and side-effect free, so it's safe to
/// run before the engine pins a backend.
/// </summary>
public sealed class StableDiffusionBackendInspector : IBackendInspector
{
    public bool IsRuntimePresent(GenerationBackend backend)
    {
        try
        {
            return backend switch
            {
                GenerationBackend.Cuda => Backends.CudaBackend.IsAvailable,
                GenerationBackend.Rocm => Backends.RocmBackend.IsAvailable,
                GenerationBackend.Vulkan => Backends.VulkanBackend.IsAvailable,
                GenerationBackend.Cpu => Backends.CpuBackend.IsAvailable,
                _ => false,
            };
        }
        catch
        {
            // A misconfigured SDK (e.g. an unreadable CUDA version.json) must not crash detection.
            return false;
        }
    }

    public bool IsBundled(GenerationBackend backend) => backend switch
    {
        // Gentastic.App always ships the CPU (base + AVX variants) and Vulkan native backends as
        // direct package references, so they're bundled regardless of the running process's layout
        // (e.g. the test host doesn't lay down the native folders).
        GenerationBackend.Cpu => true,
        GenerationBackend.Vulkan => true,
        // CUDA/ROCm are optional extras — detect them by their bundled native variant folder so that
        // adding the backend package lights them up automatically.
        GenerationBackend.Cuda => NativeVariantExists("cuda12"),
        GenerationBackend.Rocm => NativeVariantExists("rocm6") || NativeVariantExists("rocm5"),
        _ => false,
    };

    /// <summary>True when a <c>stable-diffusion.dll</c> for the given native variant folder ships
    /// alongside the app, across the layouts we produce (framework-dependent keeps
    /// <c>runtimes/&lt;rid&gt;/native/&lt;variant&gt;/</c>; a flattened publish keeps
    /// <c>&lt;variant&gt;/</c>).</summary>
    private static bool NativeVariantExists(string variant)
    {
        var baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "runtimes", "win-x64", "native", variant, "stable-diffusion.dll"),
            Path.Combine(baseDir, "runtimes", "linux-x64", "native", variant, "stable-diffusion.dll"),
            Path.Combine(baseDir, variant, "stable-diffusion.dll"),
        ];
        return candidates.Any(File.Exists);
    }
}
