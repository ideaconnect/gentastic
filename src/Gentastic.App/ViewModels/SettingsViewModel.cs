using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gentastic.App;
using Gentastic.App.Services;
using Gentastic.App.Views;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Gentastic.Core.Settings;
using Gentastic.Models;
using Microsoft.Win32;

namespace Gentastic.App.ViewModels;

/// <summary>Shows the detected runtime and model cache, and edits the Hugging Face token and the
/// preferred backend (persisted via <see cref="ISettingsService"/>).</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IRuntimeDetector _detector;
    private readonly IModelRepository _repository;
    private readonly IDiffusionEngine _engine;
    private readonly ISettingsService _settings;
    private readonly IUpdateService _updateService;
    private readonly Func<RuntimeDialog> _runtimeDialogFactory;
    private readonly CudaRuntime _cudaRuntime;
    private readonly IContentGate _contentGate;

    // Guards the age-gate revert below so setting ShowAdultModels back to false doesn't re-enter the gate.
    private bool _revertingAdultToggle;

    public SettingsViewModel(
        IRuntimeDetector detector,
        IModelRepository repository,
        IDiffusionEngine engine,
        ISettingsService settings,
        IUpdateService updateService,
        Func<RuntimeDialog> runtimeDialogFactory,
        CudaRuntime cudaRuntime,
        IContentGate contentGate)
    {
        _detector = detector;
        _repository = repository;
        _engine = engine;
        _settings = settings;
        _updateService = updateService;
        _runtimeDialogFactory = runtimeDialogFactory;
        _cudaRuntime = cudaRuntime;
        _contentGate = contentGate;

        _huggingFaceToken = settings.Current.HuggingFaceToken ?? string.Empty;
        _preferredBackend = settings.Current.PreferredBackend;
        _cacheDirectoryOverride = settings.Current.CacheDirectory ?? string.Empty;
        _theme = settings.Current.Theme;
        _showAdultModels = settings.Current.ShowAdultModels;
        Refresh();
    }

    /// <summary>Per-backend probe results (CUDA → ROCm → Vulkan → CPU), the same view shown in the
    /// first-run runtime dialog.</summary>
    public ObservableCollection<RuntimeOption> Probes { get; } = [];

    public IReadOnlyList<BackendPreference> BackendOptions { get; } = Enum.GetValues<BackendPreference>();

    public IReadOnlyList<ThemePreference> ThemeOptions { get; } = Enum.GetValues<ThemePreference>();

    [ObservableProperty] private string _hardwareSummary = string.Empty;
    [ObservableProperty] private string _backendStatus = string.Empty;
    [ObservableProperty] private string _cacheRoot = string.Empty;
    [ObservableProperty] private string _cacheSize = string.Empty;
    [ObservableProperty] private string _tokenStatus = string.Empty;
    [ObservableProperty] private string _huggingFaceToken = string.Empty;
    [ObservableProperty] private BackendPreference _preferredBackend;
    [ObservableProperty] private string _cacheDirectoryOverride = string.Empty;
    [ObservableProperty] private ThemePreference _theme;
    [ObservableProperty] private bool _showAdultModels;
    [ObservableProperty] private string _saveStatus = string.Empty;
    [ObservableProperty] private string _updateStatus = string.Empty;

    // On-demand CUDA runtime (lets the CUDA backend run without the CUDA Toolkit).
    [ObservableProperty] private bool _canDownloadCudaRuntime;
    [ObservableProperty] private bool _isDownloadingCuda;
    [ObservableProperty] private double _cudaDownloadFraction;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCudaRuntimeSection))]
    private string _cudaRuntimeStatus = string.Empty;

    /// <summary>Whether the CUDA-runtime section has anything to show (an NVIDIA machine, or a
    /// download in progress / done) - otherwise it's hidden.</summary>
    public bool HasCudaRuntimeSection => !string.IsNullOrEmpty(CudaRuntimeStatus);

    /// <summary>The engine's live backend expressed as a preference, so a pending change (the saved
    /// preference differs from what's actually running) can be detected. The native backend is fixed
    /// for the process, so a mismatch means "an app restart is pending".</summary>
    private BackendPreference ActiveBackendPreference => _engine.Backend switch
    {
        GenerationBackend.Cuda => BackendPreference.Cuda,
        GenerationBackend.Rocm => BackendPreference.Rocm,
        GenerationBackend.Vulkan => BackendPreference.Vulkan,
        _ => BackendPreference.Cpu,
    };

    /// <summary>Builds the CUDA-runtime status line. Pure and state-aware so it stops telling the user
    /// to restart once CUDA is actually the active backend - the previous code showed "restart to
    /// switch to CUDA" permanently, even after they had already switched. Every restart it mentions is
    /// an application restart (the native backend is pinned for the process lifetime).</summary>
    internal static string BuildCudaRuntimeStatus(bool cudaActive, bool cudaInstalled, bool canDownload) =>
        cudaActive ? "CUDA runtime installed - NVIDIA CUDA is active."
        : cudaInstalled ? "CUDA runtime installed. Set the preferred backend to \"Cuda\" above, then restart the app "
                          + "(close and reopen Gentastic) to switch to it."
        : canDownload ? "NVIDIA GPU detected. Download the CUDA runtime (~540 MB) to enable CUDA - no CUDA Toolkit needed."
        : string.Empty;

    // Age gate: when the user switches the adult models on, require an age confirmation. If they decline,
    // flip the toggle straight back off. (The constructor seeds the field directly, so this never fires
    // on load - only on a genuine user toggle.)
    partial void OnShowAdultModelsChanged(bool value)
    {
        if (_revertingAdultToggle || !value)
            return;

        if (!_contentGate.ConfirmAdultAge())
        {
            _revertingAdultToggle = true;
            ShowAdultModels = false;
            _revertingAdultToggle = false;
        }
    }

    [RelayCommand]
    private void BrowseCacheDirectory()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a model cache folder" };
        if (dialog.ShowDialog() == true)
            CacheDirectoryOverride = dialog.FolderName;
    }

    [RelayCommand]
    private void Save()
    {
        _settings.Current.HuggingFaceToken =
            string.IsNullOrWhiteSpace(HuggingFaceToken) ? null : HuggingFaceToken.Trim();
        _settings.Current.PreferredBackend = PreferredBackend;
        _settings.Current.CacheDirectory =
            string.IsNullOrWhiteSpace(CacheDirectoryOverride) ? null : CacheDirectoryOverride.Trim();
        _settings.Current.Theme = Theme;
        _settings.Current.ShowAdultModels = ShowAdultModels;
        _settings.Save();

        // Theme and adult-model visibility apply immediately (the latter via the settings Changed
        // event, which the model pickers listen to). The native backend is pinned per process, so a
        // backend change only lands on the next launch - and only nag about that when the saved choice
        // actually differs from what's running (selecting the backend you're already on shouldn't keep
        // telling you to restart). Always say "the app", never an ambiguous "restart".
        ThemeApplier.Apply(Theme);
        SaveStatus = PreferredBackend != ActiveBackendPreference
            ? $"Saved. The engine is still running on {_engine.Backend} - restart the app (close and reopen "
              + $"Gentastic) to switch to {PreferredBackend}. The cache folder also changes on the next launch."
            : "Saved. Cache-folder changes take effect after you restart the app (close and reopen Gentastic).";
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        var hardware = _detector.Detect();
        HardwareSummary = hardware.Summary;

        Probes.Clear();
        foreach (var row in RuntimeDialogViewModel.BackendRows(hardware))
            Probes.Add(row);

        BackendStatus = _engine.IsAvailable
            ? $"Active backend: {_engine.Backend}"
            : "Inference engine unavailable.";

        CacheRoot = _repository.CacheRoot;
        CacheSize = FormatBytes(_repository.GetCacheSizeBytes());

        var hasToken = !string.IsNullOrWhiteSpace(_settings.Current.HuggingFaceToken)
                       || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HF_TOKEN"));
        TokenStatus = hasToken
            ? "Hugging Face token set - gated models such as FLUX.1-dev can download."
            : "No Hugging Face token - required for gated models such as FLUX.1-dev.";

        // Offer the on-demand CUDA runtime when an NVIDIA GPU is present but CUDA isn't usable yet
        // (no toolkit, runtime not downloaded) - so users get CUDA without installing the CUDA Toolkit.
        var hasNvidia = hardware.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia);
        var cudaReady = hardware.ProbeFor(GenerationBackend.Cuda)?.IsReady == true;
        CanDownloadCudaRuntime = hasNvidia && !cudaReady && !CudaRuntime.IsInstalled && !IsDownloadingCuda;
        // Don't recompute the status mid-download - the progress handler owns the text then.
        if (!IsDownloadingCuda)
            CudaRuntimeStatus = BuildCudaRuntimeStatus(
                cudaActive: _engine.Backend == GenerationBackend.Cuda,
                cudaInstalled: CudaRuntime.IsInstalled,
                canDownload: CanDownloadCudaRuntime);
    }

    [RelayCommand]
    private void ReDetectRuntime()
    {
        var dialog = _runtimeDialogFactory();
        dialog.Owner = Application.Current.MainWindow;
        dialog.ShowDialog();

        // Reflect whatever the dialog persisted so the dropdown and status stay in sync.
        PreferredBackend = _settings.Current.PreferredBackend;
        Refresh();
        SaveStatus = PreferredBackend != ActiveBackendPreference
            ? $"Runtime updated. Restart the app (close and reopen Gentastic) to switch to {PreferredBackend}."
            : "Runtime updated.";
    }

    [RelayCommand]
    private async Task DownloadCudaRuntimeAsync()
    {
        IsDownloadingCuda = true;
        CanDownloadCudaRuntime = false;
        try
        {
            var progress = new Progress<CudaDownloadProgress>(p =>
            {
                CudaDownloadFraction = p.Fraction ?? 0;
                CudaRuntimeStatus =
                    $"Downloading CUDA runtime… {p.BytesReceived / 1_000_000.0:F0} MB (file {p.FileIndex}/{p.FileCount})";
            });
            await _cudaRuntime.InstallAsync(progress);
            CudaRuntimeStatus = BuildCudaRuntimeStatus(
                cudaActive: _engine.Backend == GenerationBackend.Cuda,
                cudaInstalled: true,
                canDownload: false);
        }
        catch (Exception ex)
        {
            CudaRuntimeStatus = $"CUDA runtime download failed: {ex.Message}";
        }
        finally
        {
            IsDownloadingCuda = false;
            CudaDownloadFraction = 0;
            Refresh();
        }
    }

    [RelayCommand]
    private void OpenCacheFolder()
    {
        Directory.CreateDirectory(CacheRoot);
        Process.Start(new ProcessStartInfo { FileName = CacheRoot, UseShellExecute = true });
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatus = "Checking…";
        try
        {
            var info = await _updateService.CheckAsync();
            UpdateStatus = info.UpdateAvailable
                ? $"Update available: {info.LatestTag} (you have v{info.CurrentVersion}). See releases below."
                : info.LatestVersion is null
                    ? $"No releases published yet (you have v{info.CurrentVersion})."
                    : $"You're up to date (v{info.CurrentVersion}).";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Update check failed: {ex.Message}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:F1} {units[unit]}";
    }
}
