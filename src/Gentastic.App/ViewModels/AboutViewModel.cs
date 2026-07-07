using System.Diagnostics;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Gentastic.App.ViewModels;

/// <summary>Static "About" info: app name, version, author, and links to the project site and repo.</summary>
public partial class AboutViewModel : ObservableObject
{
    private const string NoticeFileName = "THIRD_PARTY_NOTICE.md";
    private const string NoticeUrl = "https://github.com/ideaconnect/gentastic/blob/main/THIRD_PARTY_NOTICE.md";

    public string AppName => "Gentastic";
    public string Tagline => "Local AI image generation - FLUX & SDXL, on your own GPU.";
    public string AuthorName => "Bartosz Pachołek";
    public string Copyright => "© 2026 IDCT · Bartosz Pachołek";
    public string WebsiteUrl => "https://idct.tech/gentastic";
    public string GitHubUrl => "https://github.com/ideaconnect/gentastic";

    /// <summary>Short credits line naming the components that require attribution. The full,
    /// per-component notices live in THIRD_PARTY_NOTICE.md (opened by <see cref="OpenLicensesCommand"/>).</summary>
    public string Acknowledgements =>
        "Built with StableDiffusion.NET, stable-diffusion.cpp and ggml (MIT); WPF UI and "
        + ".NET / WPF (MIT); HPPH (LGPL-2.1); and Font Awesome Free icons (CC BY 4.0 / SIL OFL 1.1). "
        + "Gentastic is BSD-3-Clause. See the full third-party notices for details.";

    public string LicenseLine => "Gentastic is open source under the BSD 3-Clause License.";

    public string VersionText { get; }

    public AboutViewModel()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0);
        VersionText = $"Version {v.Major}.{v.Minor}.{v.Build}";
    }

    [RelayCommand]
    private void OpenWebsite() => OpenUrl(WebsiteUrl);

    [RelayCommand]
    private void OpenGitHub() => OpenUrl(GitHubUrl);

    /// <summary>Opens the third-party notices. Prefers the copy bundled next to the app (present in
    /// the portable build), falling back to the GitHub-hosted version for a dev/source run.</summary>
    [RelayCommand]
    private void OpenLicenses()
    {
        var local = Path.Combine(AppContext.BaseDirectory, NoticeFileName);
        OpenUrl(File.Exists(local) ? local : NoticeUrl);
    }

    private static void OpenUrl(string url)
        => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
}
