using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gentastic.App;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Settings;
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

    public SettingsViewModel(
        IRuntimeDetector detector,
        IModelRepository repository,
        IDiffusionEngine engine,
        ISettingsService settings)
    {
        _detector = detector;
        _repository = repository;
        _engine = engine;
        _settings = settings;

        _huggingFaceToken = settings.Current.HuggingFaceToken ?? string.Empty;
        _preferredBackend = settings.Current.PreferredBackend;
        _cacheDirectoryOverride = settings.Current.CacheDirectory ?? string.Empty;
        _theme = settings.Current.Theme;
        Refresh();
    }

    public ObservableCollection<string> Adapters { get; } = [];

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
    [ObservableProperty] private string _saveStatus = string.Empty;

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
        _settings.Save();

        ThemeApplier.Apply(Theme); // apply immediately
        SaveStatus = "Saved. Token applies to the next download; backend/cache changes take effect after restart.";
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        var hardware = _detector.Detect();
        HardwareSummary = hardware.Summary;

        Adapters.Clear();
        if (hardware.Adapters.Count == 0)
            Adapters.Add("No hardware GPU detected — CPU fallback.");
        foreach (var adapter in hardware.Adapters)
            Adapters.Add($"{adapter.Name} — {adapter.Vendor}, {adapter.TotalMemoryGiB:F1} GiB total");

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
    }

    [RelayCommand]
    private void OpenCacheFolder()
    {
        Directory.CreateDirectory(CacheRoot);
        Process.Start(new ProcessStartInfo { FileName = CacheRoot, UseShellExecute = true });
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
