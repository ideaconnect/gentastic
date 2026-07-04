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
    ];

    public IReadOnlyList<ModelSpec> GetAvailableModels() => _models;

    public ModelSpec? FindById(string id) =>
        _models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    private static ModelSpec Flux(
        string id, string name, ModelKind kind, Quantization quant,
        string transformerRepo, string transformerPath, ModelLicense license, int steps) =>
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
            DefaultCfg: 1.0f);
}
