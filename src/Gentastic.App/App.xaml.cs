using System.Windows;
using Gentastic.App.ViewModels;
using Gentastic.App.Views;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Presets;
using Gentastic.Core.Services;
using Gentastic.Core.Settings;
using Gentastic.Engine;
using Gentastic.Hardware;
using Gentastic.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;

namespace Gentastic.App;

/// <summary>
/// Application entry point. Builds the generic host, wires dependency injection for the engine,
/// hardware detection, model management and the WPF-UI navigation shell, then shows the main window.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host = Host
        .CreateApplicationBuilder()
        .ConfigureGentastic()
        .Build();

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Screenshot/CI mode: render in software so a plain screen-grab captures the real content
        // (hardware-composited WPF + Mica otherwise defeats window capture).
        if (Environment.GetEnvironmentVariable("GENTASTIC_SCREENSHOT") == "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        base.OnStartup(e);
        await _host.StartAsync();
        ThemeApplier.Apply(_host.Services.GetRequiredService<ISettingsService>().Current.Theme);
        RuntimeCheck.Warn(_host.Services.GetRequiredService<IRuntimeDetector>());
        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}

internal static class HostBuilderExtensions
{
    public static HostApplicationBuilder ConfigureGentastic(this HostApplicationBuilder builder)
    {
        var services = builder.Services;

        // WPF-UI navigation infrastructure.
        services.AddNavigationViewPageProvider();
        services.AddSingleton<INavigationService, NavigationService>();

        // Infrastructure.
        services.AddHttpClient();

        // Domain services.
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IPresetStore, JsonPresetStore>();
        services.AddSingleton<IRuntimeDetector, RuntimeDetector>();
        services.AddSingleton<IModelCatalog, ModelCatalog>();
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            return new HuggingFaceOptions
            {
                CacheRoot = string.IsNullOrWhiteSpace(settings.Current.CacheDirectory)
                    ? null
                    : settings.Current.CacheDirectory,
                TokenProvider = () => string.IsNullOrWhiteSpace(settings.Current.HuggingFaceToken)
                    ? Environment.GetEnvironmentVariable("HF_TOKEN")
                    : settings.Current.HuggingFaceToken,
            };
        });
        services.AddSingleton<IModelRepository, HuggingFaceModelRepository>();
        services.AddSingleton<IDiffusionEngine, StableDiffusionEngine>();
        services.AddSingleton<IGenerationService, GenerationService>();
        services.AddSingleton(new GitHubUpdateOptions
        {
            CurrentVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0),
        });
        services.AddSingleton<IUpdateService, GitHubUpdateService>();

        // Shell.
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        // Pages + view models.
        services.AddSingleton<GeneratePage>();
        services.AddSingleton<GenerateViewModel>();
        services.AddSingleton<ModelsPage>();
        services.AddSingleton<ModelsViewModel>();
        services.AddSingleton<GalleryPage>();
        services.AddSingleton<GalleryViewModel>();
        services.AddSingleton<SettingsPage>();
        services.AddSingleton<SettingsViewModel>();

        return builder;
    }
}
