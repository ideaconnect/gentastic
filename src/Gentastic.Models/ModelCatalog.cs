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
    // so swapping this in under the stock klein diffusion unlocks uncensored output. Sourced from the
    // ungated Apache-2.0 WeReCooking re-host (same ablation as Cordux's, but token-free).
    private static readonly ModelFile UncensoredQwen3Encoder =
        new(ModelFileRole.TextEncoderLlm, "WeReCooking/flux2-klein-4B-uncensored-text-encoder", "qwen3-4b-abl-q4_0.gguf");
    private static readonly ModelFile Flux2Vae =
        new(ModelFileRole.Vae, "Comfy-Org/flux2-dev", "split_files/vae/flux2-vae.safetensors");
    private static readonly ModelFile Flux2KleinDiffusion =
        new(ModelFileRole.DiffusionModel, "leejet/FLUX.2-klein-4B-GGUF", "flux-2-klein-4b-Q4_0.gguf");
    private static readonly ModelLicense Flux2KleinLicense =
        new("FLUX.2 [klein]", Gated: false, "https://huggingface.co/black-forest-labs/FLUX.2-klein-4B");
    // The ablated encoder is now an ungated Apache-2.0 re-host (WeReCooking), so this downloads
    // token-free like the stock klein - no license acceptance or Hugging Face token needed.
    private static readonly ModelLicense Flux2KleinUncensoredLicense =
        new("FLUX.2 [klein] + community uncensored encoder (ungated)", Gated: false,
            "https://huggingface.co/WeReCooking/flux2-klein-4B-uncensored-text-encoder");
    // Illustrious XL - a single-file SDXL anime checkpoint (UNet + CLIP-L/G + VAE baked in), ungated.
    private static readonly ModelLicense IllustriousLicense =
        new("Illustrious-XL · Fair AI Public License 1.0-SD", Gated: false,
            "https://huggingface.co/OnomaAIResearch/Illustrious-xl-early-release-v0");
    // RealVisXL - a single-file photorealistic SDXL checkpoint, ungated.
    private static readonly ModelLicense RealVisLicense =
        new("RealVisXL V5.0 · SDXL (OpenRAIL++-M)", Gated: false,
            "https://huggingface.co/SG161222/RealVisXL_V5.0");
    // PhotoMaker v1 stacked-id-embeddings weights (identity/face preservation). sd.cpp-compatible
    // safetensors re-host; works with any SDXL base. Load path takes the FILE, not a directory.
    private static readonly ModelFile PhotoMakerWeights =
        new(ModelFileRole.PhotoMakerId, "bssrdf/PhotoMaker", "photomaker-v1.safetensors");
    private static readonly ModelLicense PhotoMakerLicense =
        new("PhotoMaker v1 (identity) + RealVisXL SDXL - ungated", Gated: false,
            "https://huggingface.co/bssrdf/PhotoMaker");
    // FLUX.1 Kontext [dev] - an image-editing model. Reuses the standard FLUX.1 companions.
    private static readonly ModelLicense KontextLicense =
        new("FLUX.1 Kontext [dev] · Non-Commercial (GGUF re-host)", Gated: false,
            "https://huggingface.co/QuantStack/FLUX.1-Kontext-dev-GGUF");
    // Lustify - a dedicated hardcore/explicit photorealistic SDXL checkpoint, ungated single file.
    private static readonly ModelLicense LustifyLicense =
        new("LUSTIFY! SDXL v2.0 · community re-host (ungated)", Gated: false,
            "https://huggingface.co/TheImposterImposters/LUSTIFY-v2.0");
    // Acorn is Spinning - a dedicated hardcore/explicit FLUX.1-dev finetune, ungated GGUF. Uses the
    // Q4_K build (f16 norms, no bf16): the StableDiffusion.NET Vulkan native crashes in new_sd_ctx on
    // bf16-containing GGUFs, so a bf16-free quant is required here (the Q4_0 build of these finetunes
    // keeps bf16 norms and will NOT load - verified).
    private static readonly ModelLicense AcornLicense =
        new("Acorn is Spinning FLUX v1.1 · FLUX.1 [dev] finetune - Non-Commercial (ungated)", Gated: false,
            "https://huggingface.co/sherlockbt/acorn-is-spinning-flux-guff");
    // CyberRealistic Pony - a photoreal Pony Diffusion V6 XL (SDXL) checkpoint, ungated single file.
    // Pony was trained on a large explicit dataset with a score/rating tag system, so it's the strongest
    // model here for genuinely hardcore acts/poses - but it REQUIRES the Pony score tags to work well.
    private static readonly ModelLicense CyberRealisticPonyLicense =
        new("CyberRealistic Pony · Pony Diffusion V6 XL (SDXL) - ungated", Gated: false,
            "https://huggingface.co/cyberdelia/CyberRealisticPony");
    // NoobAI-XL - a danbooru+e621-trained explicit anime SDXL checkpoint (Illustrious-derived), ungated.
    private static readonly ModelLicense NoobAiLicense =
        new("NoobAI-XL v1.1 (eps) · SDXL - Fair AI Public License 1.0-SD (ungated)", Gated: false,
            "https://huggingface.co/Laxhar/noobai-XL-1.1");

    private readonly IReadOnlyList<ModelSpec> _models =
    [
        // FLUX.2 klein 4B - the default. A newer, distilled FLUX.2 variant: smaller than FLUX.1 (4B
        // transformer + 4B Qwen3 encoder vs FLUX.1's 12B + 9 GB T5, ~5 GB total) and the fastest model
        // on this hardware (~1.8 s/step). Uses the LLM encoder path in the engine, not CLIP-L + T5.
        new ModelSpec(
            Id: "flux2-klein-4b",
            DisplayName: "FLUX.2 klein 4B (fast)",
            Kind: ModelKind.Flux2Klein,
            Quantization: Quantization.Q4_0,
            Files: [Flux2KleinDiffusion, Qwen3Encoder, Flux2Vae],
            License: Flux2KleinLicense,
            DefaultSteps: 4,
            DefaultCfg: 1.0f,
            // Measured: 6.07 GB weights (sd.cpp params log) + ~1.3 GB sampling + 2.6 GB VAE decode.
            ApproxMemoryGB: 10,
            ApproxDownloadGB: 5.0),

        Flux("flux1-schnell", "FLUX.1 schnell", ModelKind.FluxSchnell, Quantization.Q4_K_S,
            "city96/FLUX.1-schnell-gguf", "flux1-schnell-Q4_K_S.gguf", SchnellLicense, steps: 4),
        Flux("flux1-schnell-q8", "FLUX.1 schnell (Q8)", ModelKind.FluxSchnell, Quantization.Q8_0,
            "city96/FLUX.1-schnell-gguf", "flux1-schnell-Q8_0.gguf", SchnellLicense, steps: 4,
            approxMemoryGB: 27, approxDownloadGB: 22.6),
        Flux("flux1-dev", "FLUX.1 dev", ModelKind.FluxDev, Quantization.Q4_K_S,
            "city96/FLUX.1-dev-gguf", "flux1-dev-Q4_K_S.gguf", DevLicense, steps: 20),
        Flux("flux1-dev-q8", "FLUX.1 dev (Q8)", ModelKind.FluxDev, Quantization.Q8_0,
            "city96/FLUX.1-dev-gguf", "flux1-dev-Q8_0.gguf", DevLicense, steps: 20,
            approxMemoryGB: 27, approxDownloadGB: 22.6),

        // PhotoMaker - keep a person's face across generations (identity lock). An SDXL base (RealVisXL
        // photoreal) + PhotoMaker v1 id-embedding weights: supply a reference face photo and a prompt
        // with a class word + the "img" trigger (e.g. "a woman img, on a beach"), and the generated
        // person keeps that face. Not adult-gated. UsesPhotoMaker drives the reference-face UI + engine.
        new ModelSpec(
            Id: "photomaker-keepface",
            DisplayName: "RealVis XL - Keep Face (SDXL identity, photoreal)",
            Kind: ModelKind.Sdxl,
            Quantization: Quantization.F16,
            Files:
            [
                new ModelFile(ModelFileRole.Checkpoint, "SG161222/RealVisXL_V5.0", "RealVisXL_V5.0_fp16.safetensors"),
                PhotoMakerWeights,
            ],
            License: PhotoMakerLicense,
            DefaultSteps: 26,
            DefaultCfg: 5.0f,
            // SDXL fp16 weights ~6.6 GB + PhotoMaker id weights 0.9 GB + ~1.2 GB sampling
            // + 3.6 GB conv-direct VAE decode at 1024².
            ApproxMemoryGB: 12,
            ApproxDownloadGB: 7.5),

        // FLUX.1 Kontext - image editing. Give it an input image + an instruction ("change the suit to
        // grey", "put them on a beach") and it edits that, keeping the face/composition. Modern FLUX
        // quality (vs SDXL PhotoMaker). Reuses the FLUX.1 companions; input image routes to RefImages.
        Flux("flux-kontext-edit", "FLUX.1 Kontext - Edit / Keep Face (slow, best)", ModelKind.FluxKontext,
            Quantization.Q4_K_S, "QuantStack/FLUX.1-Kontext-dev-GGUF", "flux1-kontext-dev-Q4_K_M.gguf",
            KontextLicense, steps: 20, isImageEdit: true),

        // FLUX.2 klein - Edit: the SAME fast klein model (already the default) does image editing when
        // given an input image + instruction ("change the suit"), keeping the face. ~4 steps (fast) vs
        // Kontext's ~20 slow steps, and NO extra download - reuses the klein diffusion + Qwen3 + VAE.
        new ModelSpec(
            Id: "flux2-klein-edit",
            DisplayName: "FLUX.2 klein - Edit / Keep Face (fast)",
            Kind: ModelKind.Flux2Klein,
            Quantization: Quantization.Q4_0,
            Files: [Flux2KleinDiffusion, Qwen3Encoder, Flux2Vae],
            License: Flux2KleinLicense,
            DefaultSteps: 4,
            DefaultCfg: 1.0f,
            IsImageEdit: true,
            ApproxMemoryGB: 10,
            ApproxDownloadGB: 5.0),

        // Adult models (hidden unless ShowAdultModels is enabled).
        // Realistic/general: stock FLUX.2 klein diffusion + the ablated Qwen3 encoder - fast (~18s),
        // strong at photorealism, and handles anime via prompting. Reuses the klein engine path; the
        // ablated encoder is now an ungated Apache-2.0 re-host, so no Hugging Face token is needed.
        new ModelSpec(
            Id: "flux2-klein-uncensored",
            DisplayName: "FLUX.2 klein - Uncensored (fast, realistic)",
            Kind: ModelKind.Flux2Klein,
            Quantization: Quantization.Q4_0,
            Files: [Flux2KleinDiffusion, UncensoredQwen3Encoder, Flux2Vae],
            License: Flux2KleinUncensoredLicense,
            DefaultSteps: 4,
            DefaultCfg: 1.0f,
            IsAdult: true,
            ApproxMemoryGB: 10,
            ApproxDownloadGB: 4.9),
        // Uncensored FLUX.2 klein in EDIT mode: same uncensored stack (ablated Qwen3 encoder) but takes
        // an input image + instruction to edit it while keeping the face - the fast klein editor with no
        // content filter. Reuses the uncensored klein files (no extra download); engine routes RefImages.
        new ModelSpec(
            Id: "flux2-klein-uncensored-edit",
            DisplayName: "FLUX.2 klein - Uncensored Edit / Keep Face",
            Kind: ModelKind.Flux2Klein,
            Quantization: Quantization.Q4_0,
            Files: [Flux2KleinDiffusion, UncensoredQwen3Encoder, Flux2Vae],
            License: Flux2KleinUncensoredLicense,
            DefaultSteps: 4,
            DefaultCfg: 1.0f,
            IsAdult: true,
            IsImageEdit: true,
            ApproxMemoryGB: 10,
            ApproxDownloadGB: 4.9),
        // Hardcore (dedicated, explicit): Lustify - the canonical photorealistic hardcore SDXL
        // checkpoint, purpose-built for true adult content. Single ungated all-in-one file (~6.5 GB);
        // natural-language prompting (not danbooru/score tags). SDXL isn't guidance-distilled, so
        // negative prompts work normally. Vulkan VAE decode still runs on CPU (Strix Halo 2 GB cap).
        new ModelSpec(
            Id: "lustify-hardcore-sdxl",
            DisplayName: "Lustify - Adult content (SDXL)",
            Kind: ModelKind.Sdxl,
            Quantization: Quantization.F16,
            Files:
            [
                new ModelFile(ModelFileRole.Checkpoint, "TheImposterImposters/LUSTIFY-v2.0",
                    "lustifySDXLNSFWSFW_v20.safetensors"),
            ],
            License: LustifyLicense,
            DefaultSteps: 30,
            DefaultCfg: 5.0f,
            IsAdult: true,
            // SDXL fp16: ~6.6 GB weights + ~1.2 GB sampling + 3.6 GB conv-direct VAE decode at 1024².
            ApproxMemoryGB: 11,
            ApproxDownloadGB: 6.6),
        // Hardcore (modern, FLUX): Acorn is Spinning - a dedicated explicit FLUX.1-dev finetune. Newer
        // architecture than the SDXL models with stronger prompt adherence; natural-language prompts.
        // Standalone Q4_K diffusion GGUF (bf16-free - see AcornLicense) reusing the shared FLUX.1
        // companions (CLIP-L + T5-XXL + VAE), ungated. Same engine path as the other FLUX.1 models.
        Flux("acorn-hardcore-flux1", "Acorn - Adult content (FLUX.1)", ModelKind.FluxDev,
            Quantization.Q4_K_S, "sherlockbt/acorn-is-spinning-flux-guff",
            "acornIsSpinningFLUX_v11-Q4_K_M.gguf", AcornLicense, steps: 24, isAdult: true),
        // Hardcore (explicit acts, Pony): CyberRealistic Pony - the strongest model here for genuinely
        // explicit hardcore content. Pony understands acts/poses far better than FLUX or plain SDXL, but
        // NEEDS its score tags in the prompt (e.g. "score_9, score_8_up, score_7_up, rating_explicit, …")
        // - without them output is weak. Single ungated SDXL checkpoint; negative prompts work normally.
        new ModelSpec(
            Id: "cyberrealistic-pony",
            DisplayName: "CyberRealistic Pony - Adult content (SDXL, needs score tags)",
            Kind: ModelKind.Sdxl,
            Quantization: Quantization.F16,
            Files:
            [
                new ModelFile(ModelFileRole.Checkpoint, "cyberdelia/CyberRealisticPony",
                    "CyberRealisticPony_V66.safetensors"),
            ],
            License: CyberRealisticPonyLicense,
            DefaultSteps: 28,
            DefaultCfg: 5.0f,
            IsAdult: true,
            // Auto-injected Pony score tags (quality boosters). The "Explicit adult content" switch
            // appends rating_explicit on demand, so SFW still works with the switch off.
            PromptPrefix: "score_9, score_8_up, score_7_up, score_6_up, score_5_up, ",
            ExplicitTag: "rating_explicit",
            ApproxMemoryGB: 11,
            ApproxDownloadGB: 6.6),
        // CyberRealistic Pony - Keep Face: the hardcore Pony model + PhotoMaker identity weights, to
        // generate a specific person (from a reference face) with Pony. SDXL PhotoMaker path (square
        // reference + "img" trigger) plus Pony's score tags. Reuses both cached files (no new download).
        new ModelSpec(
            Id: "cyberrealistic-pony-keepface",
            DisplayName: "CyberRealistic Pony - Keep Face (SDXL identity)",
            Kind: ModelKind.Sdxl,
            Quantization: Quantization.F16,
            Files:
            [
                new ModelFile(ModelFileRole.Checkpoint, "cyberdelia/CyberRealisticPony",
                    "CyberRealisticPony_V66.safetensors"),
                PhotoMakerWeights,
            ],
            License: CyberRealisticPonyLicense,
            DefaultSteps: 28,
            DefaultCfg: 5.0f,
            IsAdult: true,
            PromptPrefix: "score_9, score_8_up, score_7_up, score_6_up, score_5_up, ",
            ExplicitTag: "rating_explicit",
            ApproxMemoryGB: 12,
            ApproxDownloadGB: 7.5),
        // Realistic (dedicated, capable): RealVisXL - a top photorealistic SDXL checkpoint, ungated.
        new ModelSpec(
            Id: "realvisxl-v5",
            DisplayName: "RealVis XL V5 - Realistic (SDXL)",
            Kind: ModelKind.Sdxl,
            Quantization: Quantization.F16,
            Files:
            [
                new ModelFile(ModelFileRole.Checkpoint, "SG161222/RealVisXL_V5.0", "RealVisXL_V5.0_fp16.safetensors"),
            ],
            License: RealVisLicense,
            DefaultSteps: 25,
            DefaultCfg: 5.0f,
            IsAdult: true,
            ApproxMemoryGB: 11,
            ApproxDownloadGB: 6.6),
        // Anime (dedicated, capable): Illustrious XL - the SDXL anime gold standard, danbooru-trained.
        // Single ungated checkpoint; SDXL isn't guidance-distilled, so negative prompts work normally.
        new ModelSpec(
            Id: "illustrious-xl",
            DisplayName: "Illustrious XL - Anime (SDXL)",
            Kind: ModelKind.Sdxl,
            Quantization: Quantization.F16,
            Files:
            [
                new ModelFile(ModelFileRole.Checkpoint, "OnomaAIResearch/Illustrious-xl-early-release-v0",
                    "Illustrious-XL-v0.1.safetensors"),
            ],
            License: IllustriousLicense,
            DefaultSteps: 24,   // SDXL is ~10 s/step on this Vulkan path; 24 keeps quality, trims time
            DefaultCfg: 6.0f,
            IsAdult: true,
            // Illustrious wants danbooru quality tags; the "Explicit adult content" switch appends its nsfw tag.
            PromptPrefix: "masterpiece, best quality, ",
            ExplicitTag: "nsfw",
            ApproxMemoryGB: 11,
            ApproxDownloadGB: 6.6),
        // Anime (hardcore): NoobAI-XL - a danbooru+e621-trained explicit anime SDXL checkpoint, the
        // anime NSFW leader. EPS build (loads with standard epsilon sampling; the v-pred variant would
        // need backend v-prediction we don't force). Pure booru-tag prompting (comma-separated tags,
        // NOT natural language); auto-injects the quality prefix - add "explicit"/"nsfw" + danbooru
        // tags yourself. Single ungated checkpoint; negative prompts work (SDXL isn't distilled).
        new ModelSpec(
            Id: "noobai-xl-hardcore",
            DisplayName: "NoobAI XL - Anime Adult content (SDXL, booru tags)",
            Kind: ModelKind.Sdxl,
            Quantization: Quantization.F16,
            Files:
            [
                new ModelFile(ModelFileRole.Checkpoint, "Laxhar/noobai-XL-1.1", "NoobAI-XL-v1.1.safetensors"),
            ],
            License: NoobAiLicense,
            DefaultSteps: 28,
            DefaultCfg: 5.0f,
            IsAdult: true,
            // Quality boosters only so SFW still works; the "Explicit adult content" switch appends
            // NoobAI's e621 rating tag on demand.
            PromptPrefix: "masterpiece, best quality, newest, absurdres, highres, ",
            ExplicitTag: "explicit",
            ApproxMemoryGB: 11,
            ApproxDownloadGB: 6.8),
        // Anime (lightweight): a FLUX.1-dev anime finetune (standard FLUX.1 companions, no engine change).
        Flux("flux1-modern-anime", "FLUX.1 Modern Anime (uncensored)", ModelKind.FluxDev, Quantization.Q4_0,
            "alfredplpl/flux.1-dev-modern-anime-gguf", "modern-anime_Q4_0.gguf", AnimeFinetuneLicense,
            steps: 20, isAdult: true),
    ];

    public IReadOnlyList<ModelSpec> GetAvailableModels() => _models;

    public ModelSpec? FindById(string id) =>
        _models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    // Memory/download defaults for a Q4-class FLUX.1: ~6.5 GB transformer + 9.3 GB T5-XXL fp16 +
    // 0.24 GB CLIP-L + 0.33 GB VAE ≈ 16.4 GB of weights, plus ~2 GB sampling compute and the
    // ~2.6 GB conv-direct VAE decode buffer at 1024² → ~21 GB peak.
    private static ModelSpec Flux(
        string id, string name, ModelKind kind, Quantization quant,
        string transformerRepo, string transformerPath, ModelLicense license, int steps,
        bool isAdult = false, bool isImageEdit = false,
        int approxMemoryGB = 21, double approxDownloadGB = 16.4) =>
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
            IsAdult: isAdult,
            IsImageEdit: isImageEdit,
            ApproxMemoryGB: approxMemoryGB,
            ApproxDownloadGB: approxDownloadGB);
}
