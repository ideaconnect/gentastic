using System.Windows;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;

namespace Gentastic.App;

/// <summary>Startup runtime check: warns when no GPU-backed generation is available, so the user
/// isn't surprised by very slow CPU generation.</summary>
internal static class RuntimeCheck
{
    public static void Warn(IRuntimeDetector detector)
    {
        var profile = detector.Detect();
        if (profile.RecommendedAdapter is not null && profile.RecommendedBackend != GenerationBackend.Cpu)
            return; // a hardware GPU was found — nothing to warn about

        MessageBox.Show(
            "No compatible GPU was detected, so image generation will run on the CPU and be very slow.\n\n"
            + "A DirectX 12 / Vulkan-capable GPU is strongly recommended.",
            "Gentastic — no GPU detected",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
