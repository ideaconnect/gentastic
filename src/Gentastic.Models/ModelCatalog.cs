using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;

namespace Gentastic.Models;

/// <summary>
/// The curated, out-of-the-box model list. FLUX needs four files: the (quantized) transformer plus
/// the CLIP-L and T5-XXL text encoders and the VAE. The transformer comes from city96's GGUF
/// re-hosts; the encoders/VAE are the ungated ComfyUI / schnell copies so first-run works without
/// a token, while FLUX.1-dev is still flagged as license-gated.
/// </summary>
public sealed class ModelCatalog : IModelCatalog
{
    // Shared companion files (ungated).
    private static readonly ModelFile ClipL =
        new(ModelFileRole.TextEncoderClip, "comfyanonymous/flux_text_encoders", "clip_l.safetensors");
    private static readonly ModelFile T5Xxl =
        new(ModelFileRole.TextEncoderT5, "comfyanonymous/flux_text_encoders", "t5xxl_fp16.safetensors");
    // The FLUX autoencoder. Sourced from Comfy-Org's ungated re-host (byte-identical, 335,304,388 B)
    // rather than black-forest-labs/FLUX.1-schnell, which is gated and 401s without an HF token.
    private static readonly ModelFile FluxVae =
        new(ModelFileRole.Vae, "Comfy-Org/Lumina_Image_2.0_Repackaged", "split_files/vae/ae.safetensors");

    private static readonly ModelLicense SchnellLicense =
        new("Apache-2.0", Gated: false, "https://huggingface.co/black-forest-labs/FLUX.1-schnell");
    private static readonly ModelLicense DevLicense =
        new("FLUX.1 [dev] Non-Commercial License", Gated: true, "https://huggingface.co/black-forest-labs/FLUX.1-dev");
    // Community FLUX.1-dev finetunes re-hosted ungated; they inherit the FLUX.1 [dev] non-commercial
    // terms but download without a token and reuse the same CLIP-L / T5 / VAE companions.
    private static readonly ModelLicense AnimeFinetuneLicense =
        new("FLUX.1 [dev] finetune · Non-Commercial", Gated: false,
            "https://huggingface.co/alfredplpl/flux.1-dev-modern-anime-gguf");

    // FLUX.2 klein companions (all ungated re-hosts): a Qwen3-4B LLM text encoder (replaces the
    // CLIP-L + T5 pair) and the FLUX.2 VAE.
    private static readonly ModelFile Qwen3Encoder =
        new(ModelFileRole.TextEncoderLlm, "unsloth/Qwen3-4B-GGUF", "Qwen3-4B-Q4_K_M.gguf");
    // Community "ablated" Qwen3-4B encoder: FLUX.2 klein's content filter lives in the text encoder,
    // so swapping this in under the stock klein diffusion unlocks uncensored output.
    private static readonly ModelFile UncensoredQwen3Encoder =
        new(ModelFileRole.TextEncoderLlm, "Cordux/flux2-klein-4B-uncensored-text-encoder", "qwen3-4b-abl-q4_0.gguf");
    private static readonly ModelFile Flux2Vae =
        new(ModelFileRole.Vae, "Comfy-Org/flux2-dev", "split_files/vae/flux2-vae.safetensors");
    private static readonly ModelFile Flux2KleinDiffusion =
        new(ModelFileRole.DiffusionModel, "leejet/FLUX.2-klein-4B-GGUF", "flux-2-klein-4b-Q4_0.gguf");
    private static readonly ModelLicense Flux2KleinLicense =
        new("FLUX.2 [klein]", Gated: false, "https://huggingface.co/black-forest-labs/FLUX.2-klein-4B");
    private static readonly ModelLicense Flux2KleinUncensoredLicense =
        new("FLUX.2 [klein] + community uncensored encoder", Gated: false,
            "https://huggingface.co/Cordux/flux2-klein-4B-uncensored-text-encoder");

    private readonly IReadOnlyList<ModelSpec> _models =
    [
        Flux("flux1-schnell", "FLUX.1 schnell", ModelKind.FluxSchnell, Quantization.Q4_K_S,
            "city96/FLUX.1-schnell-gguf", "flux1-schnell-Q4_K_S.gguf", SchnellLicense, steps: 4),
        Flux("flux1-schnell-q8", "FLUX.1 schnell (Q8)", ModelKind.FluxSchnell, Quantization.Q8_0,
            "city96/FLUX.1-schnell-gguf", "flux1-schnell-Q8_0.gguf", SchnellLicense, steps: 4),
        Flux("flux1-dev", "FLUX.1 dev", ModelKind.FluxDev, Quantization.Q4_K_S,
            "city96/FLUX.1-dev-gguf", "flux1-dev-Q4_K_S.gguf", DevLicense, steps: 20),
        Flux("flux1-dev-q8", "FLUX.1 dev (Q8)", ModelKind.FluxDev, Quantization.Q8_0,
            "city96/FLUX.1-dev-gguf", "flux1-dev-Q8_0.gguf", DevLicense, steps: 20),

        // FLUX.2 klein 4B (experimental) — a newer, distilled FLUX.2 variant. Smaller than FLUX.1
        // (4B transformer + 4B Qwen3 encoder vs FLUX.1's 12B + 9 GB T5), ~5 GB total. Uses the LLM
        // encoder path in the engine, not CLIP-L + T5.
        new ModelSpec(
            Id: "flux2-klein-4b",
            DisplayName: "FLUX.2 klein 4B (fast)",
            Kind: ModelKind.Flux2Klein,
            Quantization: Quantization.Q4_0,
            Files: [Flux2KleinDiffusion, Qwen3Encoder, Flux2Vae],
            License: Flux2KleinLicense,
            DefaultSteps: 4,
            DefaultCfg: 1.0f),

        // Adult models (hidden unless ShowAdultModels is enabled).
        // Realistic/general: stock FLUX.2 klein diffusion + the ablated Qwen3 encoder — fast (~18s),
        // strong at photorealism, and handles anime via prompting. Reuses the klein engine path.
        new ModelSpec(
            Id: "flux2-klein-uncensored",
            DisplayName: "FLUX.2 klein — Uncensored (fast, realistic)",
            Kind: ModelKind.Flux2Klein,
            Quantization: Quantization.Q4_0,
            Files: [Flux2KleinDiffusion, UncensoredQwen3Encoder, Flux2Vae],
            License: Flux2KleinUncensoredLicense,
            DefaultSteps: 4,
            DefaultCfg: 1.0f,
            IsAdult: true),
        // Anime: a FLUX.1-dev anime finetune (standard FLUX.1 companions, no engine change). Dedicated
        // FLUX.2 anime NSFW finetunes don't exist yet; SDXL (Pony/Illustrious) remains the anime
        // gold standard if stronger anime is needed.
        Flux("flux1-modern-anime", "FLUX.1 Modern Anime (uncensored)", ModelKind.FluxDev, Quantization.Q4_0,
            "alfredplpl/flux.1-dev-modern-anime-gguf", "modern-anime_Q4_0.gguf", AnimeFinetuneLicense,
            steps: 20, isAdult: true),
    ];

    public IReadOnlyList<ModelSpec> GetAvailableModels() => _models;

    public ModelSpec? FindById(string id) =>
        _models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    private static ModelSpec Flux(
        string id, string name, ModelKind kind, Quantization quant,
        string transformerRepo, string transformerPath, ModelLicense license, int steps,
        bool isAdult = false) =>
        new(
            Id: id,
            DisplayName: name,
            Kind: kind,
            Quantization: quant,
            Files:
            [
                new ModelFile(ModelFileRole.DiffusionModel, transformerRepo, transformerPath),
                ClipL,
                T5Xxl,
                FluxVae,
            ],
            License: license,
            DefaultSteps: steps,
            DefaultCfg: 1.0f,
            IsAdult: isAdult);
}
