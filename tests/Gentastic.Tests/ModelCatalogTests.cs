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
    public void EveryModel_DeclaresMemoryAndDownloadEstimates()
    {
        foreach (var model in _catalog.GetAvailableModels())
        {
            // The model picker hint and the "may not fit your GPU" warning rely on these; every
            // catalog entry must declare both (calibrated from sd.cpp logs / file sizes).
            model.ApproxMemoryGB.ShouldBeInRange(1, 64, model.Id);
            model.ApproxDownloadGB.ShouldBeInRange(0.1, 64, model.Id);
            model.MemoryLabel.ShouldBe($"~{model.ApproxMemoryGB} GB", model.Id);
        }
    }

    [Fact]
    public void EveryModel_HasItsArchitectureCompanionFiles()
    {
        foreach (var model in _catalog.GetAvailableModels())
        {
            var roles = model.Files.Select(f => f.Role).ToHashSet();

            if (model.Kind == ModelKind.Sdxl)
            {
                // SDXL is a single all-in-one checkpoint - no separate diffusion/encoder/VAE files.
                roles.ShouldContain(ModelFileRole.Checkpoint);
                continue;
            }

            roles.ShouldContain(ModelFileRole.DiffusionModel);
            roles.ShouldContain(ModelFileRole.Vae);

            if (model.Kind == ModelKind.Flux2Klein)
            {
                // FLUX.2 uses a single Qwen3 LLM encoder - no CLIP-L / T5.
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

        // Schnell is Apache-2.0 - never gated.
        foreach (var model in models.Where(m => m.Kind == ModelKind.FluxSchnell))
            model.License.Gated.ShouldBeFalse();

        // The official FLUX.1-dev base models need an HF token. (Community finetunes re-hosted
        // ungated are a separate, non-gated case - see the adult finetune below.)
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

    [Fact]
    public void PonyModel_HasScoreTagPrefix()
    {
        var pony = _catalog.FindById("cyberrealistic-pony")!;
        pony.PromptPrefix.ShouldNotBeNullOrWhiteSpace();
        pony.PromptPrefix!.ShouldStartWith("score_9");
    }

    [Fact]
    public void ApplyPromptPrefix_Prepends_WhenSetAndAbsent()
    {
        var pony = _catalog.FindById("cyberrealistic-pony")!;
        pony.ApplyPromptPrefix("a cat").ShouldBe(pony.PromptPrefix + "a cat");
    }

    [Fact]
    public void ApplyPromptPrefix_DoesNotDuplicate_WhenPromptAlreadyHasTag()
    {
        var pony = _catalog.FindById("cyberrealistic-pony")!;
        const string already = "score_9, score_8_up, a dog";
        pony.ApplyPromptPrefix(already).ShouldBe(already);
    }

    [Fact]
    public void ApplyPromptPrefix_PassesThrough_WhenNoPrefix()
    {
        var schnell = _catalog.FindById("flux1-schnell")!;
        schnell.PromptPrefix.ShouldBeNull();
        schnell.ApplyPromptPrefix("a bird").ShouldBe("a bird");
    }

    [Fact]
    public void SupportsExplicitSwitch_OnlyForTagBasedModels()
    {
        _catalog.FindById("cyberrealistic-pony")!.SupportsExplicitSwitch.ShouldBeTrue();
        _catalog.FindById("noobai-xl-hardcore")!.SupportsExplicitSwitch.ShouldBeTrue();
        // Natural-language / photoreal models have no discrete explicit tag.
        _catalog.FindById("flux1-schnell")!.SupportsExplicitSwitch.ShouldBeFalse();
        _catalog.FindById("lustify-hardcore-sdxl")!.SupportsExplicitSwitch.ShouldBeFalse();
    }

    [Fact]
    public void ComposePrompt_AppendsExplicitTag_WhenSwitchOn()
    {
        var pony = _catalog.FindById("cyberrealistic-pony")!;
        var on = pony.ComposePrompt("a woman", isExplicit: true);
        on.ShouldStartWith(pony.PromptPrefix!);   // score tags still prepended
        on.ShouldEndWith("rating_explicit");
    }

    [Fact]
    public void ComposePrompt_OmitsExplicitTag_WhenSwitchOff()
    {
        var pony = _catalog.FindById("cyberrealistic-pony")!;
        pony.ComposePrompt("a woman", isExplicit: false).ShouldNotContain("rating_explicit");
    }

    [Fact]
    public void ComposePrompt_DoesNotDuplicate_WhenTagAlreadyPresent()
    {
        var noobai = _catalog.FindById("noobai-xl-hardcore")!;
        var composed = noobai.ComposePrompt("1girl, explicit, standing", isExplicit: true);
        composed.Split("explicit").Length.ShouldBe(2); // "explicit" appears exactly once
    }

    [Fact]
    public void ComposePrompt_ExplicitSwitch_NoOp_ForUnsupportedModel()
    {
        var schnell = _catalog.FindById("flux1-schnell")!;
        schnell.ComposePrompt("a bird", isExplicit: true).ShouldBe("a bird");
    }

    [Fact]
    public void PhotoMakerModel_IsFlagged_HasBothFiles_AndIsNotAdult()
    {
        var pm = _catalog.FindById("photomaker-keepface")!;
        pm.UsesPhotoMaker.ShouldBeTrue();
        pm.Files.ShouldContain(f => f.Role == ModelFileRole.Checkpoint);      // SDXL base
        pm.Files.ShouldContain(f => f.Role == ModelFileRole.PhotoMakerId);    // identity weights
        pm.IsAdult.ShouldBeFalse();                                           // mainstream feature
        _catalog.FindById("realvisxl-v5")!.UsesPhotoMaker.ShouldBeFalse();
    }

    // The generation acknowledgement must fire for adult models AND for real-face models - including the
    // ungated RealVis keep-face model, which is not adult-flagged but still carries deepfake risk.
    [Fact]
    public void RequiresContentAcknowledgement_CoversAdult_AndRealFaceModels()
    {
        // Regression: RealVis keep-face is NOT adult, but must still require the acknowledgement.
        var keepFace = _catalog.FindById("photomaker-keepface")!;
        keepFace.IsAdult.ShouldBeFalse();
        keepFace.RequiresContentAcknowledgement.ShouldBeTrue();

        // Image-edit "keep face" models (non-adult) require it too.
        _catalog.FindById("flux-kontext-edit")!.RequiresContentAcknowledgement.ShouldBeTrue();
        _catalog.FindById("flux2-klein-edit")!.RequiresContentAcknowledgement.ShouldBeTrue();

        // Adult models require it via IsAdult.
        _catalog.FindById("lustify-hardcore-sdxl")!.RequiresContentAcknowledgement.ShouldBeTrue();
        _catalog.FindById("realvisxl-v5")!.RequiresContentAcknowledgement.ShouldBeTrue();

        // Plain SFW base models do not.
        _catalog.FindById("flux1-schnell")!.RequiresContentAcknowledgement.ShouldBeFalse();
        _catalog.FindById("flux2-klein-4b")!.RequiresContentAcknowledgement.ShouldBeFalse();
    }

    // PhotoMaker: sd.cpp hard-asserts (crashes) without the "img" trigger, so ComposePrompt must inject it.
    [Fact]
    public void PhotoMaker_ComposePrompt_InsertsImgAfterClassWord_WhenMissing()
    {
        var pm = _catalog.FindById("photomaker-keepface")!;
        pm.ComposePrompt("a woman on a beach", isExplicit: false).ShouldBe("a woman img on a beach");
    }

    [Fact]
    public void PhotoMaker_ComposePrompt_KeepsExistingImgTrigger()
    {
        var pm = _catalog.FindById("photomaker-keepface")!;
        pm.ComposePrompt("a man img, in a suit", isExplicit: false).ShouldBe("a man img, in a suit");
    }

    [Fact]
    public void PhotoMaker_ComposePrompt_PrependsSubject_WhenNoClassWord()
    {
        var pm = _catalog.FindById("photomaker-keepface")!;
        var composed = pm.ComposePrompt("on a snowy mountain", isExplicit: false);
        composed.ShouldBe("a person img, on a snowy mountain");
        ModelSpec.HasPhotoMakerTrigger(composed).ShouldBeTrue();
    }

    [Fact]
    public void NonPhotoMaker_ComposePrompt_LeavesPromptWithoutImg()
    {
        var schnell = _catalog.FindById("flux1-schnell")!;
        schnell.ComposePrompt("a woman on a beach", isExplicit: false).ShouldBe("a woman on a beach");
    }

    [Fact]
    public void KontextModel_IsImageEdit_AndReusesFluxCompanions()
    {
        var k = _catalog.FindById("flux-kontext-edit")!;
        k.Kind.ShouldBe(ModelKind.FluxKontext);
        k.IsImageEdit.ShouldBeTrue();
        k.UsesPhotoMaker.ShouldBeFalse();
        k.IsGuidanceDistilled.ShouldBeTrue();
        var roles = k.Files.Select(f => f.Role).ToHashSet();
        roles.ShouldContain(ModelFileRole.DiffusionModel);   // Kontext GGUF
        roles.ShouldContain(ModelFileRole.TextEncoderClip);  // shared FLUX companions
        roles.ShouldContain(ModelFileRole.TextEncoderT5);
        roles.ShouldContain(ModelFileRole.Vae);
        _catalog.FindById("flux1-schnell")!.IsImageEdit.ShouldBeFalse();
    }

    [Fact]
    public void KleinEditModel_IsImageEdit_AndReusesKleinFiles()
    {
        var edit = _catalog.FindById("flux2-klein-edit")!;
        edit.Kind.ShouldBe(ModelKind.Flux2Klein);
        edit.IsImageEdit.ShouldBeTrue();
        // Reuses the exact same files as the default klein model - no extra download.
        var klein = _catalog.FindById("flux2-klein-4b")!;
        edit.Files.Select(f => (f.Repo, f.Path)).OrderBy(x => x.Path)
            .ShouldBe(klein.Files.Select(f => (f.Repo, f.Path)).OrderBy(x => x.Path));
        klein.IsImageEdit.ShouldBeFalse(); // the default klein is text-to-image, not edit
    }

    [Fact]
    public void PonyKeepFace_IsPhotoMaker_Adult_WithScoreTags()
    {
        var pm = _catalog.FindById("cyberrealistic-pony-keepface")!;
        pm.Kind.ShouldBe(ModelKind.Sdxl);
        pm.UsesPhotoMaker.ShouldBeTrue();     // PhotoMaker identity on the Pony base
        pm.IsAdult.ShouldBeTrue();
        pm.PromptPrefix.ShouldStartWith("score_9");   // still wants Pony score tags
        var roles = pm.Files.Select(f => f.Role).ToHashSet();
        roles.ShouldContain(ModelFileRole.Checkpoint);    // Pony checkpoint
        roles.ShouldContain(ModelFileRole.PhotoMakerId);  // identity weights
    }

    [Fact]
    public void UncensoredKleinEdit_IsAdult_AndImageEdit_ReusesUncensoredFiles()
    {
        var edit = _catalog.FindById("flux2-klein-uncensored-edit")!;
        edit.Kind.ShouldBe(ModelKind.Flux2Klein);
        edit.IsAdult.ShouldBeTrue();      // behind the ShowAdultModels gate
        edit.IsImageEdit.ShouldBeTrue();
        // Same files as the (non-edit) uncensored klein - the ablated encoder, no extra download.
        var unc = _catalog.FindById("flux2-klein-uncensored")!;
        edit.Files.Select(f => (f.Repo, f.Path)).OrderBy(x => x.Path)
            .ShouldBe(unc.Files.Select(f => (f.Repo, f.Path)).OrderBy(x => x.Path));
    }
}
