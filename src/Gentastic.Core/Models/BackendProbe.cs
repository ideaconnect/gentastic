namespace Gentastic.Core.Models;

/// <summary>Whether a compute backend can actually be used on this machine right now.</summary>
public enum BackendAvailability
{
    /// <summary>Bundled with the app, its runtime is present, and matching hardware was found -
    /// ready to select.</summary>
    Ready,

    /// <summary>Matching hardware exists, but the backend can't run yet: its runtime/SDK isn't
    /// installed (e.g. ROCm needs the AMD HIP SDK), or this build doesn't bundle its native library.
    /// The <see cref="BackendProbe.Detail"/> says which, and how to enable it.</summary>
    NeedsSetup,

    /// <summary>No hardware this backend could drive (e.g. CUDA with no NVIDIA GPU present).</summary>
    NotApplicable,
}

/// <summary>The result of probing one compute backend during startup runtime detection. Cheap to
/// produce (environment/file checks only) and safe to compute before the native library loads.</summary>
public sealed record BackendProbe(
    GenerationBackend Backend,
    BackendAvailability Availability,
    string Detail)
{
    public bool IsReady => Availability == BackendAvailability.Ready;
}
