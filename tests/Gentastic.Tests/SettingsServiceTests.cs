using Gentastic.Core.Settings;
using Shouldly;
using Xunit;

namespace Gentastic.Tests;

public class SettingsServiceTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"gentastic-settings-{Guid.NewGuid():N}.json");

    [Fact]
    public void SaveThenReload_PersistsValues()
    {
        var path = TempPath();
        try
        {
            var svc = new JsonSettingsService(path);
            svc.Current.HuggingFaceToken = "hf_test";
            svc.Current.PreferredBackend = BackendPreference.Cpu;
            svc.Save();

            var reloaded = new JsonSettingsService(path);
            reloaded.Current.HuggingFaceToken.ShouldBe("hf_test");
            reloaded.Current.PreferredBackend.ShouldBe(BackendPreference.Cpu);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_RaisesChanged()
    {
        var path = TempPath();
        try
        {
            var svc = new JsonSettingsService(path);
            var raised = 0;
            svc.Changed += (_, _) => raised++;

            svc.Current.ShowAdultModels = true;
            svc.Save();

            // The model pickers listen to this to re-filter adult models live (no app restart).
            raised.ShouldBe(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var svc = new JsonSettingsService(TempPath());
        svc.Current.HuggingFaceToken.ShouldBeNull();
        svc.Current.PreferredBackend.ShouldBe(BackendPreference.Auto);
    }

    [Fact]
    public void Save_WritesEnumAsString()
    {
        var path = TempPath();
        try
        {
            var svc = new JsonSettingsService(path);
            svc.Current.PreferredBackend = BackendPreference.Vulkan;
            svc.Save();
            File.ReadAllText(path).ShouldContain("Vulkan"); // not a numeric enum
        }
        finally
        {
            File.Delete(path);
        }
    }
}
