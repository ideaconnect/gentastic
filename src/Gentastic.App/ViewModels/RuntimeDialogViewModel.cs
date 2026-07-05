using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FontAwesome.Sharp;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Gentastic.Core.Settings;

namespace Gentastic.App.ViewModels;

/// <summary>One selectable runtime in the startup detection dialog: either "Automatic" (defer to
/// detection) or a concrete backend with its probe status.</summary>
public sealed class RuntimeOption
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required BackendPreference Preference { get; init; }
    public required IconChar Icon { get; init; }
    public required Brush IconBrush { get; init; }

    /// <summary>Non-ready backends are shown for transparency but can't be chosen.</summary>
    public required bool IsSelectable { get; init; }
}

/// <summary>Backs the startup runtime-detection dialog: shows detected hardware and the CUDA → ROCm →
/// Vulkan → CPU probe results, and persists the user's choice.</summary>
public partial class RuntimeDialogViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    private static readonly Brush Accent = Frozen("#6366F1");
    private static readonly Brush ReadyBrush = Frozen("#3BA55D");
    private static readonly Brush WarnBrush = Frozen("#E0A100");
    private static readonly Brush MutedBrush = Frozen("#71757F");

    public RuntimeDialogViewModel(IRuntimeDetector detector, ISettingsService settings)
    {
        _settings = settings;
        var profile = detector.Detect();

        HardwareSummary = profile.Adapters.Count == 0
            ? "No GPU detected — generation will run on the CPU."
            : string.Join("   ·   ", profile.Adapters.Select(a => $"{a.Name} · {a.TotalMemoryGiB:F1} GiB"));

        // "Automatic" leads and is preselected: it re-detects each launch, so it keeps working if the
        // hardware or installed runtimes change.
        Options.Add(new RuntimeOption
        {
            Title = "Automatic",
            Detail = $"Recommended — use the best available runtime ({BackendName(profile.RecommendedBackend)}).",
            Preference = BackendPreference.Auto,
            Icon = IconChar.WandMagicSparkles,
            IconBrush = Accent,
            IsSelectable = true,
        });

        foreach (var probe in profile.BackendProbes)
            Options.Add(new RuntimeOption
            {
                Title = BackendName(probe.Backend),
                Detail = probe.Detail,
                Preference = ToPreference(probe.Backend),
                Icon = StatusIcon(probe.Availability),
                IconBrush = StatusBrush(probe.Availability),
                IsSelectable = probe.IsReady,
            });

        SelectedOption = Options[0];
    }

    public string HardwareSummary { get; }

    public ObservableCollection<RuntimeOption> Options { get; } = [];

    [ObservableProperty] private RuntimeOption? _selectedOption;

    /// <summary>Persists the chosen backend and marks the runtime as confirmed so the dialog stays a
    /// first-run event. Because this runs before the engine is resolved, the choice takes effect on
    /// this launch.</summary>
    public void Save()
    {
        if (SelectedOption is null)
            return;
        _settings.Current.PreferredBackend = SelectedOption.Preference;
        _settings.Current.RuntimeConfirmed = true;
        _settings.Save();
    }

    private static string BackendName(GenerationBackend backend) => backend switch
    {
        GenerationBackend.Cuda => "NVIDIA CUDA",
        GenerationBackend.Rocm => "AMD ROCm",
        GenerationBackend.Vulkan => "Vulkan",
        _ => "CPU",
    };

    private static BackendPreference ToPreference(GenerationBackend backend) => backend switch
    {
        GenerationBackend.Cuda => BackendPreference.Cuda,
        GenerationBackend.Rocm => BackendPreference.Rocm,
        GenerationBackend.Vulkan => BackendPreference.Vulkan,
        _ => BackendPreference.Cpu,
    };

    private static IconChar StatusIcon(BackendAvailability availability) => availability switch
    {
        BackendAvailability.Ready => IconChar.CircleCheck,
        BackendAvailability.NeedsSetup => IconChar.TriangleExclamation,
        _ => IconChar.Ban,
    };

    private static Brush StatusBrush(BackendAvailability availability) => availability switch
    {
        BackendAvailability.Ready => ReadyBrush,
        BackendAvailability.NeedsSetup => WarnBrush,
        _ => MutedBrush,
    };

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
