using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gentastic.Core.Abstractions;

namespace Gentastic.App.ViewModels;

/// <summary>Shows the detected runtime, the model cache and Hugging Face token status.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IRuntimeDetector _detector;
    private readonly IModelRepository _repository;
    private readonly IDiffusionEngine _engine;

    public SettingsViewModel(
        IRuntimeDetector detector,
        IModelRepository repository,
        IDiffusionEngine engine)
    {
        _detector = detector;
        _repository = repository;
        _engine = engine;
        Refresh();
    }

    public ObservableCollection<string> Adapters { get; } = [];

    [ObservableProperty] private string _hardwareSummary = string.Empty;
    [ObservableProperty] private string _backendStatus = string.Empty;
    [ObservableProperty] private string _cacheRoot = string.Empty;
    [ObservableProperty] private string _cacheSize = string.Empty;
    [ObservableProperty] private string _tokenStatus = string.Empty;

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
            ? $"{_engine.Backend} ready"
            : "Inference engine implementation pending (milestone M1).";

        CacheRoot = _repository.CacheRoot;
        CacheSize = FormatBytes(_repository.GetCacheSizeBytes());
        TokenStatus = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HF_TOKEN"))
            ? "No HF_TOKEN set — required for gated models such as FLUX.1-dev."
            : "HF_TOKEN detected.";
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
