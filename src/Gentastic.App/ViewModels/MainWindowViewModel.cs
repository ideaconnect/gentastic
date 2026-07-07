using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gentastic.Core.Abstractions;

namespace Gentastic.App.ViewModels;

/// <summary>Backs the shell/sidebar: support links (Buy me a coffee, report an issue) and the
/// bottom-corner update check against the project's GitHub releases.</summary>
public partial class MainWindowViewModel : ObservableObject
{
    private const string CoffeeUrl = "https://buymeacoffee.com/idct";
    private const string IssuesUrl = "https://github.com/ideaconnect/gentastic/issues";

    private readonly IUpdateService _updateService;

    public MainWindowViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    [ObservableProperty]
    private string _applicationTitle = "Gentastic";

    // --- Update check (surfaced in the sidebar's bottom corner) ---

    [ObservableProperty] private string _updateStatus = "Check for updates";
    [ObservableProperty] private bool _isCheckingUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private bool _updateAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private string? _latestReleaseUrl;

    /// <summary>True only when a newer release exists AND we have a URL to send the user to.</summary>
    public bool HasUpdate => UpdateAvailable && !string.IsNullOrEmpty(LatestReleaseUrl);

    [RelayCommand]
    private void OpenCoffee() => OpenUrl(CoffeeUrl);

    [RelayCommand]
    private void ReportIssue() => OpenUrl(IssuesUrl);

    [RelayCommand]
    private void OpenLatestRelease()
    {
        if (!string.IsNullOrEmpty(LatestReleaseUrl))
            OpenUrl(LatestReleaseUrl);
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates)
            return;

        IsCheckingUpdates = true;
        UpdateStatus = "Checking for updates…";
        try
        {
            var info = await _updateService.CheckAsync();
            UpdateAvailable = info.UpdateAvailable;
            LatestReleaseUrl = info.LatestUrl;
            UpdateStatus = info.UpdateAvailable
                ? $"Update available: {info.LatestTag}"
                : info.LatestVersion is null
                    ? $"No releases yet (v{info.CurrentVersion})"
                    : $"Up to date (v{info.CurrentVersion})";
        }
        catch (Exception ex)
        {
            UpdateAvailable = false;
            LatestReleaseUrl = null;
            // Network hiccup / offline - keep it quiet in the corner; the detail is in the tooltip.
            UpdateStatus = $"Update check failed: {ex.Message}";
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    private static void OpenUrl(string url)
        => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
}
