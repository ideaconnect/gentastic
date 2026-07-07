using System.Text.RegularExpressions;

namespace Gentastic.Core.Models;

/// <summary>Model family. Determines the pipeline defaults and required companion files.</summary>
public enum ModelKind
{
    FluxSchnell,
    FluxDev,
    /// <summary>FLUX.2 klein - a distilled FLUX.2 variant. Uses a Qwen3 LLM text encoder (not
    /// CLIP-L + T5) and the FLUX.2 VAE.</summary>
    Flux2Klein,
    /// <summary>Stable Diffusion XL (incl. Illustrious/Pony/NoobAI anime finetunes). Loads from a
    /// single all-in-one checkpoint; not guidance-distilled, so negative prompts work with normal CFG.</summary>
    Sdxl,
    /// <summary>FLUX.1 Kontext [dev] - a FLUX.1 image-EDITING model. Loads like FLUX.1 (CLIP-L + T5-XXL
    /// + VAE) but takes an input image via a reference slot and edits it from an instruction prompt
    /// ("change the suit to grey"), preserving the rest incl. the face. FLUX quality, no SDXL.</summary>
    FluxKontext,
}

/// <summary>GGUF quantization level for the diffusion transformer. Lower = smaller + faster,
/// at some quality cost. Maps to stable-diffusion.cpp's supported types.</summary>
public enum Quantization
{
    F16,
    Q8_0,
    Q6_K,
    Q5_K_S,
    Q4_K_S,
    Q4_0,
    Q3_K_S,
    Q2_K,
}

/// <summary>Which part of a diffusion model a file provides.</summary>
public enum ModelFileRole
{
    DiffusionModel,   // the FLUX transformer (quantized GGUF)
    TextEncoderClip,  // CLIP-L
    TextEncoderT5,    // T5-XXL
    Vae,              // autoencoder
    TextEncoderLlm,   // LLM text encoder (Qwen3 for FLUX.2 klein)
    Checkpoint,       // single all-in-one checkpoint (SDXL: UNet + CLIP-L/G + VAE)
    PhotoMakerId,     // PhotoMaker stacked-id-embeddings weights (identity/face preservation, SDXL)
}

/// <summary>A single downloadable file that belongs to a model, addressed on the Hugging Face hub.</summary>
public sealed record ModelFile(
    ModelFileRole Role,
    string Repo,
    string Path,
    string? Sha256 = null,
    long? SizeBytes = null,
    string Revision = "main");

public sealed record ModelLicense(string Name, bool Gated, string? Url = null);

/// <summary>A catalog entry describing everything needed to run one model at one quantization.</summary>
public sealed record ModelSpec(
    string Id,
    string DisplayName,
    ModelKind Kind,
    Quantization Quantization,
    IReadOnlyList<ModelFile> Files,
    ModelLicense License,
    int DefaultSteps,
    float DefaultCfg,
    int DefaultWidth = 1024,
    int DefaultHeight = 1024,
    bool IsAdult = false,
    string? PromptPrefix = null,
    string? ExplicitTag = null,
    /// <summary>An image-editing model (FLUX.1 Kontext, FLUX.2 klein): the input image is edited from
    /// an instruction prompt while the rest (incl. the face) is preserved. Drives the UI + engine.</summary>
    bool IsImageEdit = false,
    /// <summary>Approximate peak GPU/unified memory needed to generate at the model's default
    /// resolution (weights + sampling compute buffer + conv-direct VAE decode buffer), in GB.
    /// Calibrated from sd.cpp logs on the reference machine - used for the model picker hint and
    /// the "may not fit your GPU" warning, not for hard gating.</summary>
    int ApproxMemoryGB = 0,
    /// <summary>Approximate download size of this model's files in GB (shared companion files are
    /// counted here too, though they download only once across models).</summary>
    double ApproxDownloadGB = 0)
{
    /// <summary>Short memory tag for the model picker (e.g. "~10 GB").</summary>
    public string MemoryLabel => ApproxMemoryGB > 0 ? $"~{ApproxMemoryGB} GB" : string.Empty;

    /// <summary>True when the diffusion transformer itself is guidance-distilled, so a real
    /// negative prompt only takes effect with CFG &gt; 1 (roughly 2x slower).</summary>
    public bool IsGuidanceDistilled =>
        Kind is ModelKind.FluxSchnell or ModelKind.FluxDev or ModelKind.Flux2Klein or ModelKind.FluxKontext;

    /// <summary>A PhotoMaker model: an SDXL checkpoint plus PhotoMaker id-embedding weights that
    /// preserve a person's face from a reference photo. The engine wires <c>WithPhotomaker</c> + the
    /// reference image, and the UI shows a reference-face picker + identity-strength control.</summary>
    public bool UsesPhotoMaker => Files.Any(f => f.Role == ModelFileRole.PhotoMakerId);

    /// <summary>Whether generating with this model must first show the content-guardrails acknowledgement.
    /// Fires for adult (NSFW) models AND for any model that reproduces a real person's face from a
    /// reference - the PhotoMaker identity ("keep face") models and the image-edit models - since those
    /// carry the real-person / deepfake risk the acknowledgement's checkboxes cover, even when the base
    /// model itself isn't adult-rated (e.g. the ungated RealVis keep-face model).</summary>
    public bool RequiresContentAcknowledgement => IsAdult || UsesPhotoMaker || IsImageEdit;

    /// <summary>Whether this model has a discrete "explicit" rating tag the UI can toggle on/off
    /// (tag-based SDXL models - Pony's <c>rating_explicit</c>, NoobAI's <c>explicit</c>, Illustrious'
    /// <c>nsfw</c>). Natural-language models (FLUX, photoreal SDXL) have none, so the switch is hidden.</summary>
    public bool SupportsExplicitSwitch => !string.IsNullOrWhiteSpace(ExplicitTag);

    /// <summary>Prepends this model's required quality/score-tag prefix to the user's prompt when set
    /// (Pony wants "score_9, score_8_up, …"; Illustrious/NoobAI want "masterpiece, best quality, …").
    /// Skips prepending if the prompt already contains the prefix's first tag, so manual tags aren't
    /// duplicated. Returns the effective prompt to send to the engine.</summary>
    public string ApplyPromptPrefix(string prompt)
    {
        if (string.IsNullOrWhiteSpace(PromptPrefix))
            return prompt;
        var firstTag = PromptPrefix.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)[0];
        return prompt.Contains(firstTag, StringComparison.OrdinalIgnoreCase) ? prompt : PromptPrefix + prompt;
    }

    /// <summary>Builds the effective prompt: applies <see cref="ApplyPromptPrefix"/>, then - when
    /// <paramref name="isExplicit"/> is on and the model has an <see cref="ExplicitTag"/> - appends that
    /// explicit rating tag. Both steps skip if the tag is already present (no duplication).</summary>
    public string ComposePrompt(string prompt, bool isExplicit)
    {
        var result = ApplyPromptPrefix(prompt);
        if (isExplicit && !string.IsNullOrWhiteSpace(ExplicitTag))
        {
            var firstTag = ExplicitTag.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)[0];
            if (!result.Contains(firstTag, StringComparison.OrdinalIgnoreCase))
                result = result.TrimEnd(' ', ',') + ", " + ExplicitTag;
        }
        if (UsesPhotoMaker)
            result = EnsurePhotoMakerTrigger(result);
        return result;
    }

    /// <summary>PhotoMaker requires a class word followed by the <c>img</c> trigger token, or sd.cpp
    /// hard-asserts (<c>GGML_ASSERT(it != tokens.end())</c>) and crashes the process. When the user
    /// omitted it, insert <c>img</c> after the first class word, else prepend a neutral subject.</summary>
    public static bool HasPhotoMakerTrigger(string prompt) => Regex.IsMatch(prompt, @"\bimg\b", RegexOptions.IgnoreCase);

    private static string EnsurePhotoMakerTrigger(string prompt)
    {
        if (HasPhotoMakerTrigger(prompt))
            return prompt;
        var cls = Regex.Match(prompt, @"\b(wom[ae]n|m[ae]n|girl|boy|person|people|lady|guy|male|female)\b",
            RegexOptions.IgnoreCase);
        return cls.Success ? prompt.Insert(cls.Index + cls.Length, " img") : "a person img, " + prompt;
    }
}

/// <summary>Local, on-disk result of installing a <see cref="ModelSpec"/>.</summary>
public sealed record ModelInstallation(
    ModelSpec Spec,
    IReadOnlyDictionary<ModelFileRole, string> LocalPaths);

public sealed record DownloadProgress(
    string CurrentFile,
    int FileIndex,
    int FileCount,
    long BytesReceived,
    long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value : null;
}
