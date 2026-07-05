using System.Text.Json;
using System.Text.Json.Serialization;
using Gentastic.Core.Abstractions;

namespace Gentastic.Core.Settings;

/// <summary>Persists settings to a JSON file (defaults to
/// <c>%LOCALAPPDATA%\Gentastic\settings.json</c>). Load failures fall back to defaults.</summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string SettingsPath { get; }

    public AppSettings Current { get; }

    public JsonSettingsService(string? path = null)
    {
        SettingsPath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gentastic", "settings.json");
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Options)
                       ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable settings — start from defaults rather than crash.
        }

        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, Options));
    }
}
