using System.Text.Json;
using System.Text.Json.Serialization;
using Gentastic.Core.Abstractions;

namespace Gentastic.Core.Presets;

/// <summary>Persists presets to a JSON array (defaults to
/// <c>%LOCALAPPDATA%\Gentastic\presets.json</c>).</summary>
public sealed class JsonPresetStore : IPresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string PresetsPath { get; }

    public JsonPresetStore(string? path = null)
    {
        PresetsPath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gentastic", "presets.json");
    }

    public IReadOnlyList<Preset> Load()
    {
        try
        {
            if (File.Exists(PresetsPath))
                return JsonSerializer.Deserialize<List<Preset>>(File.ReadAllText(PresetsPath), Options)
                       ?? [];
        }
        catch
        {
            // Corrupt file - start empty rather than crash.
        }

        return [];
    }

    public void Save(IReadOnlyList<Preset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PresetsPath)!);
        File.WriteAllText(PresetsPath, JsonSerializer.Serialize(presets, Options));
    }
}
