using Gentastic.Core.Models;
using Gentastic.Models;
using Shouldly;
using Xunit;

namespace Gentastic.Tests;

public class ModelCatalogTests
{
    private readonly ModelCatalog _catalog = new();

    [Fact]
    public void Catalog_ContainsFluxSchnellAndDev()
    {
        var models = _catalog.GetAvailableModels();

        models.ShouldContain(m => m.Kind == ModelKind.FluxSchnell);
        models.ShouldContain(m => m.Kind == ModelKind.FluxDev);
    }

    [Fact]
    public void EveryModel_HasItsArchitectureCompanionFiles()
    {
        foreach (var model in _catalog.GetAvailableModels())
        {
            var roles = model.Files.Select(f => f.Role).ToHashSet();

            if (model.Kind == ModelKind.Sdxl)
            {
                // SDXL is a single all-in-one checkpoint — no separate diffusion/encoder/VAE files.
                roles.ShouldContain(ModelFileRole.Checkpoint);
                continue;
            }

            roles.ShouldContain(ModelFileRole.DiffusionModel);
            roles.ShouldContain(ModelFileRole.Vae);

            if (model.Kind == ModelKind.Flux2Klein)
            {
                // FLUX.2 uses a single Qwen3 LLM encoder — no CLIP-L / T5.
                roles.ShouldContain(ModelFileRole.TextEncoderLlm);
                roles.ShouldNotContain(ModelFileRole.TextEncoderClip);
                roles.ShouldNotContain(ModelFileRole.TextEncoderT5);
            }
            else
            {
                // FLUX.1 uses CLIP-L + T5-XXL.
                roles.ShouldContain(ModelFileRole.TextEncoderClip);
                roles.ShouldContain(ModelFileRole.TextEncoderT5);
            }
        }
    }

    [Fact]
    public void SchnellIsNotGated_AndOfficialDevModelsAre()
    {
        var models = _catalog.GetAvailableModels();

        // Schnell is Apache-2.0 — never gated.
        foreach (var model in models.Where(m => m.Kind == ModelKind.FluxSchnell))
            model.License.Gated.ShouldBeFalse();

        // The official FLUX.1-dev base models need an HF token. (Community finetunes re-hosted
        // ungated are a separate, non-gated case — see the adult finetune below.)
        _catalog.FindById("flux1-dev")!.License.Gated.ShouldBeTrue();
        _catalog.FindById("flux1-dev-q8")!.License.Gated.ShouldBeTrue();
    }

    [Fact]
    public void AdultModels_AreFlagged_AndDefaultModelsAreNot()
    {
        var models = _catalog.GetAvailableModels();

        // At least one adult model exists and is flagged; the core FLUX models are not.
        models.ShouldContain(m => m.IsAdult);
        _catalog.FindById("flux1-schnell")!.IsAdult.ShouldBeFalse();
        _catalog.FindById("flux1-dev")!.IsAdult.ShouldBeFalse();
    }

    [Fact]
    public void FindById_IsCaseInsensitive()
    {
        _catalog.FindById("FLUX1-SCHNELL").ShouldNotBeNull();
        _catalog.FindById("does-not-exist").ShouldBeNull();
    }
}
