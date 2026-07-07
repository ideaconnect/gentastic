using Gentastic.Core.Settings;

namespace Gentastic.Core.Abstractions;

/// <summary>Loads and persists <see cref="AppSettings"/>.</summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    string SettingsPath { get; }

    void Save();

    /// <summary>Raised after <see cref="Save"/> persists. Lets live views react to setting changes
    /// without an app restart - e.g. the model pickers re-filter when <c>ShowAdultModels</c> flips.</summary>
    event EventHandler? Changed;
}
