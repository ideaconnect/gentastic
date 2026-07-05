namespace Gentastic.Core.Settings;

/// <summary>Which compute backend to use. <see cref="Auto"/> defers to hardware detection.</summary>
public enum BackendPreference
{
    Auto,
    Vulkan,
    Cpu,
}

/// <summary>User-configurable settings, persisted as JSON.</summary>
public sealed class AppSettings
{
    /// <summary>Hugging Face access token for gated models (e.g. FLUX.1-dev). Null = none.</summary>
    public string? HuggingFaceToken { get; set; }

    /// <summary>Preferred generation backend. Applied at startup (the native backend is fixed for the
    /// process lifetime, so changes take effect on the next launch).</summary>
    public BackendPreference PreferredBackend { get; set; } = BackendPreference.Auto;
}
