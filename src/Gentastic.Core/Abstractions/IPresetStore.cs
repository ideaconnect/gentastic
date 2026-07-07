using Gentastic.Core.Presets;

namespace Gentastic.Core.Abstractions;

/// <summary>Loads and persists named generation presets.</summary>
public interface IPresetStore
{
    string PresetsPath { get; }

    IReadOnlyList<Preset> Load();

    void Save(IReadOnlyList<Preset> presets);
}
