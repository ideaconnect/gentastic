using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;

namespace Gentastic.App.ViewModels;

/// <summary>One row in the model manager.</summary>
public partial class ModelRowViewModel : ObservableObject
{
    public ModelRowViewModel(ModelSpec spec, bool installed)
    {
        Spec = spec;
        _isInstalled = installed;
        _status = installed ? "Installed" : "Not installed";
    }

    public ModelSpec Spec { get; }
    public string DisplayName => Spec.DisplayName;

    public string Details =>
        $"{Spec.Kind} · {Spec.Quantization} · {Spec.DefaultSteps} steps · " +
        $"{Spec.License.Name}{(Spec.License.Gated ? " (gated)" : string.Empty)}";

    /// <summary>True when the model requires accepting a license / requesting access on Hugging Face
    /// (and a token) before it can download.</summary>
    public bool IsGated => Spec.License.Gated && !string.IsNullOrWhiteSpace(Spec.License.Url);

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private string _status;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isBusy;
}

/// <summary>Model manager: lists the catalog, shows install state, and downloads/removes models.</summary>
public partial class ModelsViewModel : ObservableObject
{
    private readonly IModelCatalog _catalog;
    private readonly IModelRepository _repository;
    private readonly ISettingsService _settings;

    public ModelsViewModel(IModelCatalog catalog, IModelRepository repository, ISettingsService settings)
    {
        _catalog = catalog;
        _repository = repository;
        _settings = settings;
        Refresh();
    }

    public ObservableCollection<ModelRowViewModel> Models { get; } = [];

    [ObservableProperty] private string _statusMessage = string.Empty;

    [RelayCommand]
    private void Refresh()
    {
        Models.Clear();
        foreach (var spec in _catalog.GetAvailableModels())
        {
            if (spec.IsAdult && !_settings.Current.ShowAdultModels)
                continue;
            Models.Add(new ModelRowViewModel(spec, _repository.IsInstalled(spec)));
        }

        StatusMessage = $"Cache: {_repository.CacheRoot} · {FormatBytes(_repository.GetCacheSizeBytes())} on disk";
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

    [RelayCommand]
    private async Task DownloadAsync(ModelRowViewModel? row)
    {
        if (row is null || row.IsBusy)
            return;

        row.IsBusy = true;
        row.Status = "Starting…";
        try
        {
            var progress = new Progress<DownloadProgress>(d =>
            {
                row.Progress = d.Fraction ?? 0;
                var pct = d.Fraction is { } f ? $" {f:P0}" : string.Empty;
                row.Status = $"Downloading {d.CurrentFile}{pct} ({d.FileIndex}/{d.FileCount})";
            });

            await _repository.EnsureInstalledAsync(row.Spec, progress);
            row.IsInstalled = true;
            row.Status = "Installed";
        }
        catch (Exception ex)
        {
            row.Status = $"Failed: {ex.Message}";
        }
        finally
        {
            row.IsBusy = false;
            row.Progress = 0;
        }
    }

    [RelayCommand]
    private void Delete(ModelRowViewModel? row)
    {
        if (row is null)
            return;
        _repository.Delete(row.Spec);
        row.IsInstalled = _repository.IsInstalled(row.Spec);
        row.Status = row.IsInstalled ? "Installed" : "Removed";
    }

    /// <summary>Opens the gated model's Hugging Face page in the default browser so the user can accept
    /// its license / request access, then add a token in Settings and retry the download.</summary>
    [RelayCommand]
    private void OpenLicensePage(ModelRowViewModel? row)
    {
        var url = row?.Spec.License.Url;
        if (string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            row!.Status = "Opened the model page — accept access, add a token in Settings, then download.";
        }
        catch (Exception ex)
        {
            row!.Status = $"Couldn't open the browser: {ex.Message}";
        }
    }
}
