using Gentastic.Core.Settings;
using Wpf.Ui.Appearance;

namespace Gentastic.App;

/// <summary>Maps the persisted <see cref="ThemePreference"/> to a WPF-UI theme.</summary>
internal static class ThemeApplier
{
    public static void Apply(ThemePreference preference)
    {
        switch (preference)
        {
            case ThemePreference.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case ThemePreference.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }
}
