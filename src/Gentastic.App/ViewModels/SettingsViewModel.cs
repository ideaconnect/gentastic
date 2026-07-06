using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gentastic.App;
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

    public SettingsViewModel(
        IRuntimeDetector detector,
        IModelRepository repository,
        IDiffusionEngine engine,
        ISettingsService settings,
        IUpdateService updateService,
        Func<RuntimeDialog> runtimeDialogFactory,
        CudaRuntime cudaRuntime)
    {
        _detector = detector;
        _repository = repository;
        _engine = engine;
        _settings = settings;
        _updateService = updateService;
        _runtimeDialogFactory = runtimeDialogFactory;
        _cudaRuntime = cudaRuntime;

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
    /// download in progress / done) — otherwise it's hidden.</summary>
    public bool HasCudaRuntimeSection => !string.IsNullOrEmpty(CudaRuntimeStatus);

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

        ThemeApplier.Apply(Theme); // apply immediately
        SaveStatus = "Saved. Token applies to the next download; backend/cache/model-visibility changes take effect after restart.";
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
            ? "Hugging Face token set — gated models such as FLUX.1-dev can download."
            : "No Hugging Face token — required for gated models such as FLUX.1-dev.";

        // Offer the on-demand CUDA runtime when an NVIDIA GPU is present but CUDA isn't usable yet
        // (no toolkit, runtime not downloaded) — so users get CUDA without installing the CUDA Toolkit.
        var hasNvidia = hardware.Adapters.Any(a => a.Vendor == GpuVendor.Nvidia);
        var cudaReady = hardware.ProbeFor(GenerationBackend.Cuda)?.IsReady == true;
        CanDownloadCudaRuntime = hasNvidia && !cudaReady && !CudaRuntime.IsInstalled && !IsDownloadingCuda;
        CudaRuntimeStatus =
            CudaRuntime.IsInstalled ? "CUDA runtime installed — restart to switch to CUDA."
            : CanDownloadCudaRuntime ? "NVIDIA GPU detected. Download the CUDA runtime (~540 MB) to enable CUDA — no CUDA Toolkit needed."
            : string.Empty;
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
        SaveStatus = "Runtime updated. Backend changes take effect after restart.";
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
            CudaRuntimeStatus = "CUDA runtime installed — restart to switch to CUDA.";
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
