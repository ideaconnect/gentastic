using Gentastic.Core.Settings;

namespace Gentastic.Core.Abstractions;

/// <summary>Loads and persists <see cref="AppSettings"/>.</summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    string SettingsPath { get; }

    void Save();
}
