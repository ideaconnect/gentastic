namespace Gentastic.Core.Settings;

/// <summary>Which compute backend to use. <see cref="Auto"/> defers to hardware detection, which
/// prefers accelerators in the order CUDA → ROCm → Vulkan → CPU. A forced choice is honoured only
/// when that backend is actually usable; otherwise detection's recommendation is used.</summary>
public enum BackendPreference
{
    Auto,
    Cuda,
    Rocm,
    Vulkan,
    Cpu,
}

/// <summary>UI theme. <see cref="System"/> follows the OS setting.</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>User-configurable settings, persisted as JSON.</summary>
public sealed class AppSettings
{
    /// <summary>Hugging Face access token for gated models (e.g. FLUX.1-dev). Null = none.</summary>
    public string? HuggingFaceToken { get; set; }

    /// <summary>Preferred generation backend. Applied at startup (the native backend is fixed for the
    /// process lifetime, so changes take effect on the next launch).</summary>
    public BackendPreference PreferredBackend { get; set; } = BackendPreference.Auto;

    /// <summary>Overrides where models are cached. Null = the default
    /// <c>%LOCALAPPDATA%\Gentastic\models</c>. Applied on the next launch.</summary>
    public string? CacheDirectory { get; set; }

    /// <summary>UI theme.</summary>
    public ThemePreference Theme { get; set; } = ThemePreference.Dark;

    /// <summary>Set once the user has seen the startup runtime-detection dialog and confirmed a
    /// backend. Keeps that dialog to a first-run event rather than nagging on every launch; it can
    /// still be reopened from Settings.</summary>
    public bool RuntimeConfirmed { get; set; }
}
