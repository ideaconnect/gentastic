using Gentastic.Core.Models;
using Gentastic.Core.Presets;
using Shouldly;
using Xunit;

namespace Gentastic.Tests;

public class PresetStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"gentastic-presets-{Guid.NewGuid():N}.json");

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = TempPath();
        try
        {
            var store = new JsonPresetStore(path);
            store.Save(
            [
                new Preset
                {
                    Name = "Portrait", Prompt = "a portrait", ModelId = "flux1-schnell",
                    Width = 768, Height = 1024, Steps = 4, Seed = 7, Cfg = 1.0f, Sampler = Sampler.Euler,
                },
            ]);

            var loaded = new JsonPresetStore(path).Load();
            loaded.Count.ShouldBe(1);
            loaded[0].Name.ShouldBe("Portrait");
            loaded[0].Width.ShouldBe(768);
            loaded[0].Sampler.ShouldBe(Sampler.Euler);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty() =>
        new JsonPresetStore(TempPath()).Load().ShouldBeEmpty();
}
