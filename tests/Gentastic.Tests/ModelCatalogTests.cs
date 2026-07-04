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
    public void EveryModel_HasTheFourFluxFiles()
    {
        foreach (var model in _catalog.GetAvailableModels())
        {
            var roles = model.Files.Select(f => f.Role).ToHashSet();
            roles.ShouldContain(ModelFileRole.DiffusionModel);
            roles.ShouldContain(ModelFileRole.TextEncoderClip);
            roles.ShouldContain(ModelFileRole.TextEncoderT5);
            roles.ShouldContain(ModelFileRole.Vae);
        }
    }

    [Fact]
    public void DevModels_AreLicenseGated_SchnellIsNot()
    {
        foreach (var model in _catalog.GetAvailableModels())
        {
            if (model.Kind == ModelKind.FluxDev)
                model.License.Gated.ShouldBeTrue();
            else
                model.License.Gated.ShouldBeFalse();
        }
    }

    [Fact]
    public void FindById_IsCaseInsensitive()
    {
        _catalog.FindById("FLUX1-SCHNELL").ShouldNotBeNull();
        _catalog.FindById("does-not-exist").ShouldBeNull();
    }
}
