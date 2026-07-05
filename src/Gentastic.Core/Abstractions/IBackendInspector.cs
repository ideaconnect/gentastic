using Gentastic.Core.Models;

namespace Gentastic.Core.Abstractions;

/// <summary>Reports, per compute backend, whether this build ships its native library and whether
/// its runtime/SDK is installed on the machine. Implementations must stay lightweight — environment
/// variables and file checks only — so this is safe to call at startup, before the diffusion engine
/// loads (and pins) the native backend.</summary>
public interface IBackendInspector
{
    /// <summary>True when this build includes the native library for the backend (so it could be
    /// loaded if the hardware/runtime allow).</summary>
    bool IsBundled(GenerationBackend backend);

    /// <summary>True when the backend's runtime is present: the NVIDIA CUDA 12 toolkit, the AMD HIP
    /// SDK (ROCm), a Vulkan loader, etc. CPU is always present.</summary>
    bool IsRuntimePresent(GenerationBackend backend);
}
