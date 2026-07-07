using System.Windows;
using Gentastic.App.Services;
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
        // Last-resort exception handling: a background bug must not silently kill the whole app.
        // Managed exceptions on the UI thread are logged + shown and the app keeps running; faults
        // on background threads are logged. (Native aborts/access violations inside sd.cpp cannot
        // be caught from managed code - those are prevented by pre-guards in the engine instead.)
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("ui", args.Exception);
            args.Handled = true;
            MessageBox.Show(
                $"Unexpected error: {args.Exception.Message}\n\n"
                + @"A crash log was saved to %LOCALAPPDATA%\Gentastic\crashes.",
                "Gentastic", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("task", args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash("fatal", args.ExceptionObject as Exception);

        // Screenshot/CI mode: render in software so a plain screen-grab captures the real content
        // (hardware-composited WPF + Mica otherwise defeats window capture).
        if (Environment.GetEnvironmentVariable("GENTASTIC_SCREENSHOT") == "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        base.OnStartup(e);
        await _host.StartAsync();
        var settings = _host.Services.GetRequiredService<ISettingsService>();
        ThemeApplier.Apply(settings.Current.Theme);

        // If the user downloaded the CUDA runtime (no toolkit), point CUDA_PATH + the DLL search path
        // at it before anything resolves the engine. Must precede detection and the first native call.
        Gentastic.Models.CudaRuntime.ActivateIfInstalled();

        // Screenshot harness for the dialog itself: show it, let it self-capture and shut down.
        if (Environment.GetEnvironmentVariable("GENTASTIC_SHOT_RUNTIME") == "1")
        {
            _host.Services.GetRequiredService<RuntimeDialog>().ShowDialog();
            return;
        }

        // Screenshot harness for the adult-content confirmation modals. GENTASTIC_SHOT_DIALOG starts with
        // "age" or "ack"; append "-checked" (e.g. "ack-checked") to pre-tick the boxes so the enabled
        // button state can be captured.
        var shotDialog = Environment.GetEnvironmentVariable("GENTASTIC_SHOT_DIALOG");
        if (shotDialog is not null)
        {
            Window? dialog = shotDialog.StartsWith("age") ? new AgeConfirmationDialog()
                : shotDialog.StartsWith("ack") ? new AdultAcknowledgementDialog()
                : null;
            if (dialog is not null)
            {
                dialog.ShowDialog();
                return;
            }
        }

        // First run: let the user confirm the auto-detected compute runtime (CUDA → ROCm → Vulkan →
        // CPU). Skipped once confirmed, and during headless screenshot/auto-gen runs (it would block).
        var headless = Environment.GetEnvironmentVariable("GENTASTIC_SCREENSHOT") == "1"
                       || Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN") == "1";
        if (!settings.Current.RuntimeConfirmed && !headless)
        {
            _host.Services.GetRequiredService<RuntimeDialog>().ShowDialog();
        }

        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }

    /// <summary>Writes an unhandled exception to %LOCALAPPDATA%\Gentastic\crashes. Never throws -
    /// the crash logger must not be able to crash the crash handling.</summary>
    private static void LogCrash(string kind, Exception? exception)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Gentastic", "crashes");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}-{kind}.txt"),
                $"{DateTime.Now:O} [{kind}]\n{exception}\n");
        }
        catch
        {
            // best effort only
        }
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
        services.AddSingleton<CudaRuntime>();
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

        // Adult-content confirmation modals (age gate + per-generation acknowledgement).
        services.AddSingleton<IContentGate, ContentGate>();

        // Shell.
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        // Runtime-detection dialog (transient - shown at first run and re-openable from Settings).
        services.AddTransient<RuntimeDialog>();
        services.AddTransient<RuntimeDialogViewModel>();
        services.AddTransient<Func<RuntimeDialog>>(sp => sp.GetRequiredService<RuntimeDialog>);

        // Pages + view models.
        services.AddSingleton<GeneratePage>();
        services.AddSingleton<GenerateViewModel>();
        services.AddSingleton<ModelsPage>();
        services.AddSingleton<ModelsViewModel>();
        services.AddSingleton<GalleryPage>();
        services.AddSingleton<GalleryViewModel>();
        services.AddSingleton<SettingsPage>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutPage>();
        services.AddSingleton<AboutViewModel>();

        return builder;
    }
}
