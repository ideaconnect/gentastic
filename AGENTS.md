# AGENTS.md - Gentastic engineering handoff

This is the authoritative technical guide to Gentastic, written so that a new engineer or a
different AI model can pick up the project at any point without re-deriving how it works. It
covers the architecture, the full image-generation pipeline (sampling, VAE, guidance), the model
catalog, hardware detection, the WPF app, the build/release story, and the sharp edges that will
bite you if you don't know them.

For a warmer, concept-first tour aimed at humans (what diffusion is, how to use the app), read
[HUMANS.md](HUMANS.md). For the marketing-level summary, read the [README](README.md).

> Style note: this repo uses plain hyphens, never em-dashes or en-dashes, in code and docs.

---

## 1. What Gentastic is

Gentastic is a modern, no-Python **Windows desktop app for running FLUX and Stable Diffusion XL
image-generation models locally**. It is a focused, out-of-the-box alternative to Amuse AI /
ComfyUI: sane defaults instead of endless knobs. Text-to-image, image-to-image, image editing
(keep-face), automatic model download from Hugging Face, and automatic GPU/runtime detection.

The whole thing is C# on **.NET 10**. Inference runs through
[StableDiffusion.NET](https://github.com/DarthAffe/StableDiffusion.NET), a managed binding over
**stable-diffusion.cpp**, on native accelerator backends (Vulkan / CUDA / ROCm / CPU). There is no
Python, no ONNX, no external runtime to install: the native backends ship inside the app.

The original development target is an **AMD Radeon 8060S / Strix Halo (gfx1151)** APU on Windows 11
with ~47 GiB unified memory, which is why Vulkan is the primary path and why several
Vulkan-specific workarounds exist (see the gotchas). The shipped portable build is universal (CPU +
Vulkan + NVIDIA CUDA) and auto-selects the best backend.

---

## 2. Orientation for a new agent (start here)

```sh
# Build everything (solution uses the new .slnx format)
dotnet build Gentastic.slnx -c Debug

# Run the app
dotnet run --project src/Gentastic.App

# Run the unit tests
dotnet test Gentastic.slnx

# Headless end-to-end smoke test (needs a GPU + a cached model)
dotnet run --project tools/Gentastic.Smoke                 # defaults to flux1-schnell
dotnet run --project tools/Gentastic.Smoke out.png flux2-klein-4b
```

Requires the **.NET 10 SDK** on Windows. A DirectX 12 / Vulkan-capable GPU is strongly recommended
(CPU works but is very slow). The native backends are NuGet package references, so a plain
`dotnet build` pulls them in; CUDA is opt-in (see build section).

**Where to look first**, by concern:

| I want to change...            | Start in |
|--------------------------------|----------|
| How an image is actually made  | [`Gentastic.Engine/StableDiffusionEngine.cs`](src/Gentastic.Engine/StableDiffusionEngine.cs) |
| The orchestration/glue         | [`Gentastic.Core/Services/GenerationService.cs`](src/Gentastic.Core/Services/GenerationService.cs) |
| Which models exist             | [`Gentastic.Models/ModelCatalog.cs`](src/Gentastic.Models/ModelCatalog.cs) |
| Model file/spec shape          | [`Gentastic.Core/Models/Catalog.cs`](src/Gentastic.Core/Models/Catalog.cs) |
| Downloading models             | [`Gentastic.Models/HuggingFaceModelRepository.cs`](src/Gentastic.Models/HuggingFaceModelRepository.cs) |
| GPU / backend detection        | [`Gentastic.Hardware/RuntimeDetector.cs`](src/Gentastic.Hardware/RuntimeDetector.cs) |
| The Generate page behavior     | [`Gentastic.App/ViewModels/GenerateViewModel.cs`](src/Gentastic.App/ViewModels/GenerateViewModel.cs) |
| DI wiring / startup            | [`Gentastic.App/App.xaml.cs`](src/Gentastic.App/App.xaml.cs) |
| Navigation / shell             | [`Gentastic.App/MainWindow.xaml.cs`](src/Gentastic.App/MainWindow.xaml.cs) |
| Packaging                      | [`scripts/publish-portable.ps1`](scripts/publish-portable.ps1) |

---

## 3. Solution layout

Five class libraries plus the app, a test project, and a console smoke tool. The dependency
direction is strictly inward: everything depends on `Gentastic.Core`; nothing depends on
`Gentastic.App`.

```
Gentastic.slnx
├─ src/
│  ├─ Gentastic.Core      Domain models, service interfaces, orchestration, settings/presets, PNG metadata.
│  │                      Pure managed, no WPF, no native binding. The contract layer everything shares.
│  ├─ Gentastic.Engine    The one IDiffusionEngine implementation (StableDiffusion.NET / stable-diffusion.cpp).
│  ├─ Gentastic.Hardware  GPU enumeration (DXGI via Vortice) + backend probing/selection.
│  ├─ Gentastic.Models    Model catalog, Hugging Face downloader, CUDA on-demand runtime, GitHub updater.
│  └─ Gentastic.App       WPF (.NET 10) + WPF-UI shell, MVVM view models, DI bootstrap. The only exe.
├─ tests/Gentastic.Tests  xUnit + Shouldly. Core logic that doesn't need a GPU.
└─ tools/Gentastic.Smoke  Console harness: download -> load -> sample -> save, for manual GPU verification.
```

Key design rule: the engine is hidden behind `IDiffusionEngine`, so swapping stable-diffusion.cpp
for a future CUDA/ROCm/Windows-ML backend is a substitution, not a rewrite. Likewise `IModelRepository`,
`IRuntimeDetector`, `ISettingsService`, `IPresetStore`, `IUpdateService`, `IModelCatalog` all have
interfaces in Core and concrete implementations further out.

UI stack: **WPF-UI** (Fluent design), **CommunityToolkit.Mvvm** (source-generated `[ObservableProperty]`
/ `[RelayCommand]`), **FontAwesome.Sharp** for icons, `Microsoft.Extensions.Hosting` for the DI
generic host.

---

## 4. The generation pipeline, end to end

This is the single most important mental model. One "Generate" click flows through these layers:

```
GenerateViewModel.GenerateAsync           (App)      builds the request, loops the batch, saves output
   -> GenerationRequest                   (Core)     TextToImageRequest | ImageToImageRequest (+reference)
   -> IGenerationService.RunAsync         (Core)     orchestrates: download -> load -> sample -> decode
        -> IModelRepository.EnsureInstalledAsync      download missing files from Hugging Face
        -> IDiffusionEngine.LoadModelAsync            load GGUF/checkpoint onto the backend (cached across runs)
        -> IDiffusionEngine.TextToImageAsync / ImageToImageAsync
             -> stable-diffusion.cpp: sample N steps, then VAE-decode latent -> RGB
   -> RenderedImage                       (Core)     raw RGB byte[] (backend-agnostic; UI owns PNG encoding)
   -> SaveOutput                          (App)      PNG to Pictures\Gentastic with embedded metadata
```

`GenerationService.RunAsync(request, model, progress, ct)` reports a `GenerationStatus` with a
`GenerationStage` at each phase, which the UI turns into a status line + progress bar:

`Downloading` -> `LoadingModel` -> `Sampling` (per step) -> `Decoding` (VAE) -> `Done`.

The model is only reloaded when `engine.LoadedModel?.Spec.Id != model.Id`, so repeated generations
with the same model skip the multi-second load. The engine caches one loaded model at a time.

`RenderedImage(byte[] Pixels, int Width, int Height, int Channels)` is deliberately a plain record
of raw pixels: Core has no image library. The App layer encodes to PNG with WPF's `PngBitmapEncoder`
([`RenderedImageExtensions.SavePng`](src/Gentastic.App/Imaging/RenderedImageExtensions.cs)).

---

## 5. How sampling works

Diffusion image generation starts from **random Gaussian noise in a compressed latent space** and
iteratively removes noise over a fixed number of **steps**, steered by the text prompt, until a clean
latent remains. Gentastic delegates the actual math to stable-diffusion.cpp; the C# side only wires
the knobs and forwards progress.

The per-run parameters live on `GenerationRequest` (in [`Generation.cs`](src/Gentastic.Core/Models/Generation.cs))
and are mapped to native `ImageGenerationParameter` in
[`StableDiffusionEngine.BuildParameter`](src/Gentastic.Engine/StableDiffusionEngine.cs):

| Request field       | Meaning | Native call |
|---------------------|---------|-------------|
| `Prompt`            | Positive prompt (already composed with model tags, see below) | `ImageGenerationParameter.TextToImage(prompt)` |
| `NegativePrompt`    | What to avoid; only effective with CFG > 1 on distilled models | `.WithNegativePrompt(...)` |
| `Width` / `Height`  | Output resolution | `.WithSize(w, h)` |
| `Steps`             | Number of denoising steps | `.WithSteps(n)` |
| `Cfg`               | Classifier-free guidance scale (1.0 = off/fastest) | `.WithCfg(f)` |
| `Seed`              | RNG seed; `-1` = random | `.WithSeed(l)` |
| `Sampler`           | Sampling algorithm | `.WithSampler(MapSampler(...))` |

**Samplers.** The `Sampler` enum is a curated subset of what sd.cpp supports: `EulerA` (default),
`Euler`, `Heun`, `DpmPP2M`, `DpmPP2Mv2`, `Lcm`. `MapSampler` translates them to
`StableDiffusion.NET.Sampler`. The **scheduler is not exposed** as a user knob; sd.cpp's default is
used. Adding a sampler is a two-line change (enum value + `MapSampler` case).

**Pipeline defaults by family.** `BuildParameter` calls `.WithSDXLDefaults()` for SDXL and
`.WithFluxDefaults()` for everything FLUX. These set the prediction type / flow-matching mode that
each architecture needs. sd.cpp auto-detects the transformer architecture from the model file (you
will see "Flux2 FLOW mode" etc. in the sd.cpp log).

**Guidance distillation and the negative-prompt gate.** Base FLUX models are *guidance-distilled*:
they were trained to bake guidance in, so they run great at **CFG = 1** with very few steps (~4 for
schnell/klein), but a real negative prompt does nothing unless you raise CFG above 1 (which roughly
doubles the work because it runs a second, unconditioned pass per step). `ModelSpec.IsGuidanceDistilled`
is true for all FLUX kinds. The UI enforces this: `GenerateViewModel.IsNegativePromptEnabled` disables
the negative-prompt box (with a hint) until CFG > 1 for distilled models. **SDXL is not distilled**,
so it uses normal CFG (~5-6), more steps (~24-30), and negative prompts always work.

**Batch.** `GenerateViewModel.GenerateAsync` loops `BatchCount` times. With a fixed seed it uses
`Seed + i` (reproducible variations); with seed `-1` each image is randomized. Output filenames are
collision-safe (a `_2`, `_3` suffix) because several random-seed images can land in the same second.

**Progress and cancellation.** sd.cpp raises a static `Progress` event per step. The engine subscribes
only for the duration of one run (it is a static event, so leaking a handler would cross runs). Each
callback reports `GenerationProgress(Step, TotalSteps)`. Cancellation is a `CancellationToken`: the
work runs on `Task.Run`, and the token is checked before sampling starts; the UI's Cancel button
trips the `CancellationTokenSource`.

---

## 6. How the VAE decode works (and where it runs)

The diffusion transformer works entirely in a small **latent** space (roughly 1/8 resolution per
axis). The final latent is not an image yet. A **VAE (variational autoencoder) decoder** expands that
latent back into full-resolution RGB pixels. This is a **separate stage that happens once, after the
last sampling step**, and stable-diffusion.cpp does not emit step callbacks during it.

That "silent" decode caused a UI problem (the bar looked frozen at "step N/N") and, on the target
hardware, a correctness problem:

- **The frozen-bar fix.** After the final step the engine reports a synthetic
  `GenerationProgress(Steps, Steps, GenerationProgress.DecodingStage)` marker. `GenerationService`
  maps that to `GenerationStage.Decoding`, so the UI shows "Decoding image (VAE)... almost there".
  Step counting was always correct; the decode is simply not a step.

- **The Vulkan buffer cap and conv-direct (load-bearing history).** The classic im2col+GEMM VAE
  decode needs one huge compute buffer (measured: 6.6 GB for FLUX at 1024^2, 7.7 GB for SDXL), and
  Vulkan drivers cap a single buffer allocation (~2 GB `maxStorageBufferRange` on the AMD driver) -
  so an on-GPU im2col decode fails with `ErrorOutOfDeviceMemory` *after* sampling finishes,
  surfacing as "The engine returned no image." (sd.cpp #1290; `WithVaeTiling()` alone does not fix
  it - ggml reserves the full-resolution graph up front.) The engine used to work around this with
  `WithVaeOnCpu()` on Vulkan. **Since 2026-07-07 the Vulkan path instead uses
  `WithVaeConvDirect()`** (direct Conv2d, sd.cpp PR #744): it avoids materializing the im2col
  matrix, shrinking the decode buffer to 2.5-3.6 GB, which allocates fine - so the VAE now stays on
  the GPU. Benchmarked on the 8060S at 1024^2: decode dropped from ~43 s (CPU) to ~5.4 s on all
  three architectures (klein / FLUX.1 / SDXL), total klein generation 84 -> 48 s, with
  bit-near-identical output (max pixel diff 1/255, pure float rounding). img2img encode also runs
  on-GPU (387 MB buffer). **CUDA uses conv-direct too** (same reasoning - the 8.5 GB im2col decode
  buffer would OOM any <= 8 GB NVIDIA card at 1024^2; the bundled ggml implements CONV_2D for CUDA;
  pending real-hardware verification). The CPU backend keeps im2col because conv-direct measured
  *slower* on CPU (14.3 s vs 9.4 s at 512^2). Escape hatches, no rebuild needed:
  `GENTASTIC_VAE_CPU=1` (decode on CPU, safe everywhere) and `GENTASTIC_VAE_NO_CONV_DIRECT=1`
  (GPU im2col, the pre-2026-07 behaviour). Upstream tracks a Vulkan conv-direct corruption bug on
  one Linux/RADV Vega iGPU (sd.cpp #1673) that does not reproduce here.

**Rule of thumb:** any future feature that builds a large single Vulkan compute buffer (upscalers,
higher resolutions, ControlNet, big batches) may hit the same per-allocation cap. The first lever is
now `With*ConvDirect` (VAE / diffusion / ESRGAN variants exist); the fallbacks remain CPU placement
(`WithVaeOnCpu`, `WithClipNetOnCpu`, `WithControlNetOnCpu`, `WithOffloadedParamsToCPU`) or splitting
the work.

---

## 7. Model architectures and the catalog

### 7.1 Model kinds

`ModelKind` (in [`Catalog.cs`](src/Gentastic.Core/Models/Catalog.cs)) decides which files a model
needs and which engine branch loads it:

| Kind | Files it needs | How the engine loads it |
|------|----------------|-------------------------|
| `FluxSchnell` / `FluxDev` | transformer GGUF + CLIP-L + T5-XXL + VAE | `.WithDiffusionModelPath` + `.WithClipLPath` + `.WithT5xxlPath` + `.WithVae` |
| `Flux2Klein` | transformer GGUF + Qwen3 LLM encoder + FLUX.2 VAE | `.WithDiffusionModelPath` + `.WithLLMPath` + `.WithVae` |
| `Sdxl` | one all-in-one checkpoint (UNet + CLIP-L/G + VAE baked in) | `.WithModelPath` + `.WithSDXLDefaults` |
| `FluxKontext` | like FLUX.1 (CLIP-L + T5 + VAE) | FLUX.1 load path; input image routes to `RefImages` (image editing) |

`ModelFileRole` names each file: `DiffusionModel`, `TextEncoderClip`, `TextEncoderT5`, `Vae`,
`TextEncoderLlm`, `Checkpoint`, `PhotoMakerId`. A `ModelInstallation` maps each role to a local path.

Two capabilities are attached to SDXL via an extra file:
- **PhotoMaker (keep-face / identity).** An SDXL checkpoint + PhotoMaker v1 id-embedding weights
  (`ModelFileRole.PhotoMakerId`). `.WithPhotomaker(path)` at load; at generation the reference face
  is sent as `PhotoMaker.IdImages` and the prompt must contain a class word + the `img` trigger
  (auto-inserted by `ModelSpec.ComposePrompt`). The reference is center-cropped to a square and
  resized to 512^2 first, because a non-square reference makes sd.cpp's PhotoMaker face tensor index
  out of range and abort the process.
- **Image edit (Kontext / FLUX.2 klein edit).** `IsImageEdit = true`. The input image goes to
  `RefImages` with `AutoResizeRefImage`; the model edits it from an instruction prompt while
  preserving the rest, including the face.

### 7.2 ModelSpec shape

`ModelSpec` is one catalog entry = one model at one quantization:

```csharp
ModelSpec(
    string Id, string DisplayName, ModelKind Kind, Quantization Quantization,
    IReadOnlyList<ModelFile> Files, ModelLicense License,
    int DefaultSteps, float DefaultCfg,
    int DefaultWidth = 1024, int DefaultHeight = 1024,
    bool IsAdult = false, string? PromptPrefix = null, string? ExplicitTag = null,
    bool IsImageEdit = false)
```

Derived/behavioral members:
- `IsGuidanceDistilled` - true for all FLUX kinds (drives the CFG negative-prompt gate).
- `UsesPhotoMaker` - true when a `PhotoMakerId` file is present.
- `RequiresContentAcknowledgement` - `IsAdult || UsesPhotoMaker || IsImageEdit`. Any model that can
  reproduce a real person's face is gated even if it isn't adult-rated.
- `SupportsExplicitSwitch` - true when `ExplicitTag` is set.
- `ApplyPromptPrefix(prompt)` - prepends `PromptPrefix` (skips if the first tag is already present).
- `ComposePrompt(prompt, isExplicit)` - applies the prefix, appends `ExplicitTag` when the switch is
  on, and ensures the PhotoMaker `img` trigger. **This is what the UI sends to the engine and saves
  in metadata.**

`PromptPrefix` / `ExplicitTag` are how tag-based SDXL families work without forcing NSFW: prefixes
are quality tags only (Pony's `score_9, score_8_up, ...`; Illustrious/NoobAI's `masterpiece, best
quality, ...`), and the explicit rating tag (`rating_explicit` / `nsfw` / `explicit`) is only added
when the user flips the "Explicit adult content" switch.

### 7.3 The catalog today

`ModelCatalog` ships these entries (companions are shared, ungated re-hosts so first run needs no
token). Adult models are hidden unless `ShowAdultModels` is enabled in Settings.

| Id | Kind | Notes |
|----|------|-------|
| `flux2-klein-4b` | Flux2Klein | **Default.** Fastest here (~1.8 s/step), ~5 GB. Qwen3 encoder. |
| `flux1-schnell` / `flux1-schnell-q8` | FluxSchnell | Apache-2.0, open, 4 steps, CFG 1. |
| `flux1-dev` / `flux1-dev-q8` | FluxDev | License-gated (needs HF token), 20 steps. |
| `photomaker-keepface` | Sdxl + PhotoMaker | RealVisXL + PhotoMaker v1 identity lock. Not adult. |
| `flux-kontext-edit` | FluxKontext | FLUX.1 image editing (slow, best quality). `IsImageEdit`. |
| `flux2-klein-edit` | Flux2Klein | Same fast klein doing edits. `IsImageEdit`. |
| `flux2-klein-uncensored` (+`-edit`) | Flux2Klein | Ablated Qwen3 encoder. Adult. |
| `lustify-hardcore-sdxl` | Sdxl | Photoreal hardcore checkpoint. Adult. |
| `acorn-hardcore-flux1` | FluxDev | Hardcore FLUX.1 finetune (Q4_K, bf16-free). Adult. |
| `cyberrealistic-pony` (+`-keepface`) | Sdxl | Pony V6 XL; needs score tags. Adult. |
| `realvisxl-v5` | Sdxl | Photoreal SDXL. Adult-flagged. |
| `illustrious-xl` | Sdxl | Anime SDXL, danbooru tags. Adult. |
| `noobai-xl-hardcore` | Sdxl | Explicit anime (eps build), booru tags. Adult. |
| `flux1-modern-anime` | FluxDev | Lightweight anime finetune. Adult-flagged. |

### 7.4 Adding a model (recipe)

1. Add a `ModelSpec` to `ModelCatalog._models`. Reuse the shared companions (`ClipL`, `T5Xxl`,
   `FluxVae`, `Qwen3Encoder`, `Flux2Vae`, `PhotoMakerWeights`) or the `Flux(...)` helper for a
   standard FLUX.1 entry. Give it an `Id`, `DisplayName`, `Kind`, `Quantization`, its `Files`, a
   `ModelLicense`, and `DefaultSteps`/`DefaultCfg`.
2. Set `DefaultWidth`/`DefaultHeight` (all current models are 1024^2). SDXL must stay at ~1 MP.
3. **Set `ApproxMemoryGB` and `ApproxDownloadGB`** - a catalog test fails without them. Memory =
   weights + ~1-2 GB sampling buffer + the conv-direct VAE decode buffer (2.6 GB FLUX / 3.6 GB SDXL
   at 1024^2); calibrate weights from the sd.cpp `total params memory size` log line or file sizes.
   Reference points: klein ~10, SDXL ~11 (+1 with PhotoMaker), FLUX.1 Q4 ~21 (the fp16 T5 is 9.3 GB
   of it), FLUX.1 Q8 ~27. These drive the picker label and the amber "may not fit your GPU" hint.
4. For tag-based families add `PromptPrefix` and, optionally, `ExplicitTag`.
5. **Screen FLUX GGUFs for bf16 before adding them.** The StableDiffusion.NET 7.0.0 Vulkan native
   throws a `DivideByZeroException` in `new_sd_ctx` on FLUX GGUFs with *many* bf16 tensors (hundreds -
   typical of Q4_0 finetunes that keep bf16 norms). A handful of bf16 tensors (norms) is fine. Prefer
   **Q4_K / Q5_K / Q8_0** builds (city96-style) over Q4_0 for finetunes. You can range-download just
   the GGUF header to check the tensor types without a full download.
6. Add/adjust a test in [`ModelCatalogTests`](tests/Gentastic.Tests/ModelCatalogTests.cs) if the
   entry has prefix/explicit/prompt behavior.

If the model is genuinely gated on Hugging Face, set `ModelLicense.Gated = true` so the UI shows a
"Get access" button; the download will 401 without a token.

---

## 8. Model download and cache

[`HuggingFaceModelRepository`](src/Gentastic.Models/HuggingFaceModelRepository.cs) downloads straight
over HTTPS (no hub client, no Python) from `https://huggingface.co/{repo}/resolve/{revision}/{path}`:

- **Cache layout:** `%LOCALAPPDATA%\Gentastic\models\<repo>\<path>` (override via Settings ->
  `AppSettings.CacheDirectory`). Shared encoders/VAE are fetched once and reused across models.
- **Resume:** streams to a `<file>.part`, sends a `Range` header when a partial exists, and appends
  only if the server answers `206 Partial Content` (else it restarts). Atomically `File.Move`d into
  place on success. `HttpClient.Timeout` is set to infinite for multi-GB files.
- **Auth / gating:** the token comes from Settings, falling back to the `HF_TOKEN` env var
  (`HuggingFaceOptions.TokenProvider`, wired in `App.xaml.cs`). A `401`/`403` throws a friendly
  `UnauthorizedAccessException` telling the user to accept the license and set a token.
- **Integrity:** if a `ModelFile.Sha256` is provided, [`Checksum.VerifyAsync`](src/Gentastic.Models/Checksum.cs)
  validates the `.part` and deletes it on mismatch.
- **Delete** removes only the model-specific transformer; shared companions are intentionally kept
  (ref-counted cleanup is a follow-up).

`EnsureInstalledAsync` reports `DownloadProgress(CurrentFile, FileIndex, FileCount, BytesReceived,
TotalBytes)`; `GenerationService` maps it to the `Downloading` stage.

---

## 9. Hardware and backend detection

[`RuntimeDetector.Detect()`](src/Gentastic.Hardware/RuntimeDetector.cs) returns a `HardwareProfile`:

1. **Enumerate GPUs** via DXGI (`CreateDXGIFactory1` from Vortice). Software adapters (WARP /
   Microsoft Basic Render Driver) are skipped. Each becomes a `GpuAdapter(Index, Name, Vendor,
   Dedicated, Shared)`; vendor comes from the PCI id (`0x1002` AMD, `0x10DE` Nvidia, `0x8086` Intel).
   If DXGI fails, detection proceeds with an empty adapter list and falls back to CPU.
2. **Build probes** in a fixed priority order: **CUDA -> ROCm -> Vulkan -> CPU** (the static
   `Priority` array; note this is *not* the `GenerationBackend` enum order). A backend is `Ready`
   only when all three hold: matching hardware present, native lib `IsBundled`, and runtime
   `IsRuntimePresent`. Otherwise it is `NotApplicable` (no hardware) or `NeedsSetup` (missing bundle
   or runtime), with a `Detail` string explaining which.
3. **Recommend** the first `Ready` probe; CPU is always bundled and present, so this never returns
   nothing.

[`StableDiffusionBackendInspector`](src/Gentastic.Hardware/StableDiffusionBackendInspector.cs) does
the availability checks, all wrapped in try/catch so a misconfigured SDK can't crash detection:
- `IsRuntimePresent` delegates to StableDiffusion.NET's `Backends.{Cuda,Rocm,Vulkan,Cpu}Backend.IsAvailable`.
  CUDA reads `CUDA_PATH` + a `version.json`; ROCm reads `HIP_PATH` (the AMD HIP SDK). On the Strix
  Halo target there is no HIP SDK, so ROCm is `NeedsSetup` and **Vulkan wins**.
- `IsBundled` checks for `stable-diffusion.dll` under `runtimes/win-x64/native/<variant>/` (variant
  `cuda12` / `rocm6` / `rocm5`); CPU and Vulkan are always considered bundled.

**The chosen backend is fixed for the process lifetime.** StableDiffusion.NET loads its native
library once, lazily, on the first native call; the engine must enable the backend before that call
(see gotchas). Changing the backend in Settings takes effect on the next launch.

### CUDA without the toolkit

Most users don't have the CUDA Toolkit. [`CudaRuntime`](src/Gentastic.Models/CudaRuntime.cs) lets the
bundled CUDA backend run anyway by downloading NVIDIA's **redistributable** `cudart` + `cuBLAS`
(~567 MB, CUDA 12.8) into `%LOCALAPPDATA%\Gentastic\cuda-runtime`, writing the exact `version.json`
shape `CudaBackend.IsAvailable` parses, and - at startup, in `App.OnStartup`, before anything
resolves the engine - `ActivateIfInstalled()` points `CUDA_PATH` + the DLL search path at that
folder (no-op if a real toolkit is already installed). The driver supplies `nvcuda.dll`. Settings
shows a "Download CUDA runtime" button when an NVIDIA GPU is present but CUDA isn't ready. This path
is wired and detection-verified but has never been exercised on real NVIDIA hardware (the dev machine
is AMD).

---

## 10. The WPF app layer

**DI bootstrap.** [`App.xaml.cs`](src/Gentastic.App/App.xaml.cs) builds a `Host.CreateApplicationBuilder()`
and `ConfigureGentastic()` registers everything: WPF-UI navigation, `AddHttpClient()`, all domain
singletons (settings, presets, detector, catalog, `CudaRuntime`, `HuggingFaceOptions` factory wired
from settings, repository, engine, generation service, updater, `IContentGate`), the shell, the pages
and view models (all singletons), and the `RuntimeDialog` (transient + a `Func<RuntimeDialog>`
factory so Settings can reopen it).

**Startup order** (`App.OnStartup`) is deliberate: start host -> apply theme ->
`CudaRuntime.ActivateIfInstalled()` (must precede the engine) -> optional screenshot/dialog harness
-> first-run `RuntimeDialog` if `!RuntimeConfirmed` -> `MainWindow.Show()`.

**Navigation lives in `MainWindow.xaml.cs`, not the view model.** The sidebar is `RadioButton`s with
a `Tag` string (e.g. `"GeneratePage"`); `OnNavChecked` maps the tag to a `Page` `Type` and calls
`ContentFrame.Navigate(_services.GetRequiredService(pageType))`. Because pages are singletons,
navigating back to a page reuses the same instance and its state. `MainWindowViewModel` only handles
the sidebar's support links (Buy me a coffee, Report an issue) and the GitHub update-check status.

**Pages / view models:**
- `GeneratePage` / `GenerateViewModel` - the heart. Model picker, prompt, size/steps/seed/CFG/sampler,
  presets, image-to-image and reference-input (PhotoMaker/edit) modes, batch, progress/cancel.
- `ModelsPage` / `ModelsViewModel` - download / delete catalog models, open gated license pages.
- `GalleryPage` / `GalleryViewModel` - browse `Pictures\Gentastic`; thumbnails decode off the UI
  thread and are disk-cached (~240 px JPEGs under `%LOCALAPPDATA%\Gentastic\thumbnails`, keyed by
  path+mtime) so reopening is fast; the selected image's embedded PNG metadata is read back.
- `SettingsPage` / `SettingsViewModel` - HF token, backend preference, cache dir, theme, adult toggle
  (age-gated), re-run detection, on-demand CUDA download.
- `AboutPage` / `AboutViewModel` - static info + links.
- `Controls/PageHeader` - reusable title/icon/actions bar atop each page.

**Content gating.** [`IContentGate` / `ContentGate`](src/Gentastic.App/Services/ContentGate.cs) keeps
view models free of `Window` creation. Two modals: `ConfirmAdultAge()` (single checkbox, shown when
the user turns on adult models in Settings) and `ConfirmGenerationAcknowledgement()` (four checkboxes,
shown before every generation with a model whose `RequiresContentAcknowledgement` is true). In
headless runs (`GENTASTIC_AUTOGEN` / `GENTASTIC_SCREENSHOT`) both auto-accept so nothing blocks.

**GenerateViewModel specifics worth knowing:**
- `ApplyModelDefaults()` runs on model switch: resets `Steps`/`Cfg` to the model defaults **and
  rebuilds `SizePresets` per architecture**, then snaps `SelectedSize` to the model's native size.
  SDXL only offers its ~1 MP buckets (`SdxlSizes`); FLUX/klein also get fast 512^2 and wide 1280x720
  (`FluxSizes`). This is the fix for SDXL's "Picasso" distortion off its native resolution.
- `BuildRequest(seed)` calls `SelectedModel.ComposePrompt(...)`, then routes to a reference-input
  `TextToImageRequest` (PhotoMaker/edit), an `ImageToImageRequest` (classic img2img + denoise), or a
  plain `TextToImageRequest`.
- `SaveOutput` writes `gentastic_<timestamp>_<seed>.png` to `Pictures\Gentastic` with iTXt metadata
  (prompt, negative, model, seed, steps, cfg, sampler, size, and mode/denoise for img2img).

**Theming.** [`ThemeApplier`](src/Gentastic.App/ThemeApplier.cs) maps the persisted `ThemePreference`
to WPF-UI's `ApplicationThemeManager`. Window is a `FluentWindow` with Mica backdrop.

**Error containment.** Generation errors are caught per-operation in the view models (with a
targeted out-of-memory hint - the message matcher looks for "memory"/"alloc"). To make that work,
the engine keeps a ring buffer of recent sd.cpp Warn/Error log lines and appends them to thrown
exceptions: the native API reports failures like an OOM'd compute buffer only via the log plus a
null image. `App.OnStartup` also registers global handlers - `DispatcherUnhandledException` (log +
warning dialog, app keeps running), `TaskScheduler.UnobservedTaskException`, and
`AppDomain.UnhandledException` - writing crash files to `%LOCALAPPDATA%\Gentastic\crashes`. Hard
native aborts (GGML_ASSERT / access violations) cannot be caught from managed code; those are
prevented by engine pre-guards (e.g. the PhotoMaker trigger/square-reference checks). The model
picker shows each model's `ApproxMemoryGB` and warns (amber hint) when it exceeds the detected GPU's
usable memory (dedicated VRAM for discrete cards, unified total for APUs) - preventing OOMs before
they happen.

---

## 11. Persistence and file locations

| What | Where |
|------|-------|
| Settings | `%LOCALAPPDATA%\Gentastic\settings.json` (`JsonSettingsService`, corrupt-safe load) |
| Presets | `%LOCALAPPDATA%\Gentastic\presets.json` (`JsonPresetStore`, array of `Preset`) |
| Model cache | `%LOCALAPPDATA%\Gentastic\models\<repo>\<path>` (override in Settings) |
| Gallery thumbnails | `%LOCALAPPDATA%\Gentastic\thumbnails` (~240 px JPEGs) |
| CUDA runtime | `%LOCALAPPDATA%\Gentastic\cuda-runtime` (redistributables + version.json) |
| Generated images | `Pictures\Gentastic` (**note: a different root from the app data above**) |

`AppSettings` fields: `HuggingFaceToken`, `PreferredBackend` (Auto/Cuda/Rocm/Vulkan/Cpu),
`CacheDirectory`, `Theme` (System/Light/Dark), `RuntimeConfirmed`, `ShowAdultModels`. `Preset` fields:
`Name`, `Prompt`, `NegativePrompt`, `ModelId`, `Width`, `Height`, `Steps`, `Seed`, `Cfg`, `Sampler`.

PNG metadata is written/read as spec-correct **iTXt** chunks (UTF-8) by
[`PngMetadata`](src/Gentastic.Core/Imaging/PngMetadata.cs) via direct byte manipulation (WPF's
metadata writer is unreliable). It is pure managed and unit-tested; the App layer's `SavePng`
encodes the pixels and then injects the chunks.

---

## 12. Build, run, test, package, CI

**Local:**
```sh
dotnet restore Gentastic.slnx
dotnet build   Gentastic.slnx -c Release --no-restore
dotnet test    Gentastic.slnx -c Release --no-build
dotnet run     --project src/Gentastic.App
```

**Tests** (`tests/Gentastic.Tests`, xUnit + Shouldly, no GPU needed):
`RuntimeDetectorTests`, `BackendProbeTests`, `ModelCatalogTests`, `ChecksumTests`, `PngMetadataTests`,
`PresetStoreTests`, `SettingsServiceTests`, `UpdateServiceTests`. CI does **not** run the smoke tool
(it needs a GPU + multi-GB cached models).

**Packaging** - [`scripts/publish-portable.ps1`](scripts/publish-portable.ps1) produces the
universal portable zip (`dist/Gentastic-portable-win-x64.zip`, ~309 MB, CPU + Vulkan + CUDA):
1. `dotnet publish ... -r win-x64 --self-contained true -p:IncludeCuda=true`.
2. **Reconstructs** `runtimes/win-x64/native/<variant>/` from the NuGet backend packages. A RID
   self-contained publish flattens/dedupes the identically-named native `stable-diffusion.dll`s, so
   the script copies them back per-variant from `~/.nuget/packages`. This is also why the csproj sets
   `ErrorOnDuplicatePublishOutputFiles=false` (the CPU backend ships the dll in several AVX subfolders).
3. Zips the folder.

**Why CUDA is opt-in.** The CUDA12 backend NuGet carries a ~200 MB native. The `PackageReference` in
[`Gentastic.App.csproj`](src/Gentastic.App/Gentastic.App.csproj) is gated behind
`Condition="'$(IncludeCuda)' == 'true'"` so normal dev/CI builds stay lean; only the publish script
sets `-p:IncludeCuda=true`. CUDA arch targets cover compute 6.1/7.5/8.6/8.9/10.0 (GTX 10-series
through RTX 50-series).

**GitHub Actions:**
- `ci.yml` - on push/PR to `main`: setup .NET 10, restore, build (Release), test. Read-only.
- `release.yml` - on a `v*` tag: run `publish-portable.ps1`, publish a GitHub Release with the zip
  and auto-generated notes.
- `pages.yml` - on changes under `website/`: build the Jekyll site; deploy to GitHub Pages on push
  to `main` (PRs build-only).

---

## 13. Headless / test hooks

The UI is wired for headless verification and screenshotting via env vars (so a change can be
verified end-to-end without a person clicking):

- `GENTASTIC_AUTOGEN=1` - `MainWindow.RunAutoGenAsync` drives a real generation through the actual
  `GenerateViewModel` then shuts down. Extra knobs: `GENTASTIC_AUTOGEN_MODEL=<id>` (resolves any
  catalog model, bypassing the adult gate), `_STEPS`, `_COUNT`, `_EXPLICIT=1`, `_MARKER=<file>`, and
  `_PROMPTS="slug::prompt|slug::prompt"` + `_OUTDIR` for batch marketing-asset generation.
- `GENTASTIC_SCREENSHOT=1` - software render + self-capture the window to `GENTASTIC_SHOT_PATH`.
  `GENTASTIC_SHOT_PAGE` picks the start page; `GENTASTIC_SHOT_H` / `GENTASTIC_SHOT_W` override the
  window size so below-the-fold content is captured; `GENTASTIC_SHOT_RUNTIME` /
  `GENTASTIC_SHOT_DIALOG` capture the runtime and adult modals (`ContentGate.Headless` auto-accepts).
- `tools/Gentastic.Smoke` - console: `dotnet run --project tools/Gentastic.Smoke [outPng] [modelId]`,
  with `GENTASTIC_PROMPT` / `GENTASTIC_STEPS` / `GENTASTIC_CFG` / `GENTASTIC_W` / `GENTASTIC_H`. It
  prints the sd.cpp weight-type stats, which is how new GGUFs are verified to load.

---

## 14. Load-bearing gotchas

Read these before touching the engine or the model list.

1. **Enable the backend in the engine constructor, before `InitializeEvents`.** StableDiffusion.NET
   loads its native lib once, lazily, on the first native call and caches it for the whole process.
   If the backend isn't enabled before that call, generation silently runs on the CPU. The engine
   does this in its ctor, guarded by `Interlocked.Exchange`.
2. **Vulkan and CUDA VAE = `WithVaeConvDirect()` on the GPU** (since 2026-07-07; see section 6).
   Do not revert to plain on-GPU im2col decode - it needs a single 6.6-8.5 GB buffer, fails at
   1024^2 on Vulkan and would OOM <= 8 GB NVIDIA cards on CUDA. Keep both hatches working:
   `GENTASTIC_VAE_CPU=1` (CPU decode) and `GENTASTIC_VAE_NO_CONV_DIRECT=1` (GPU im2col) - upstream
   sd.cpp #1673 reports conv-direct corruption on one RADV device. Do not enable conv-direct on the
   CPU backend (measured slower there). CUDA conv-direct is pending real-hardware verification.
3. **bf16 GGUF crash** - FLUX GGUFs with many bf16 tensors crash the Vulkan native at load. Prefer
   Q4_K/Q5_K/Q8_0; screen headers before adding (section 7.4).
4. **Do not call `GetSystemInfo()`** on the native binding - it heap-corrupts.
5. **Backend is fixed per process** - changing it in Settings needs a restart.
6. **SDXL distorts badly ("Picasso") off its ~1024^2 native resolution.** `ApplyModelDefaults` snaps
   the size on model switch and SDXL never offers 512^2 / 1280x720. Keep that constraint.
7. **NoobAI: use the EPS build**, not v-pred (the v-pred build needs backend v-prediction + ztSNR that
   sd.cpp doesn't force here, giving broken output). Booru-tag prompting only.
8. **Pony needs its score tags** to work at all - handled by `PromptPrefix`.
9. **PhotoMaker needs a class word + `img` trigger and a square reference** or sd.cpp aborts the
   process; both are handled (`ComposePrompt` inserts the trigger, the engine squares the reference).
10. **`ShowAdultModels` changes need a restart** to affect the model lists - the Generate/Models VMs
    read it at construction. The age gate itself fires live.
11. **RID publish flattens native DLLs** - the publish script must reconstruct them (section 12).

Hardware reality: sampling is ~1.8 s/step on FLUX.2 klein, ~6 s/step on FLUX.1, ~10 s/step on SDXL at
1024^2 on this GPU. The FLUX.1 slowness is a known gfx1151 Windows-driver shared-memory limit (32 KB
vs 65 KB on Linux/RADV), not an app bug. klein-4B is the fast default for a reason.

---

## 15. How to continue (extension recipes)

- **Add a model** - section 7.4.
- **Add a sampler** - add a value to `Sampler` (Core `Generation.cs`) and a case to
  `StableDiffusionEngine.MapSampler`.
- **Add a compute backend** - add a value to `GenerationBackend` (Core) and `BackendPreference`;
  extend `RuntimeDetector.Priority`, `MatchingAdapter`, the probe `Detail` helpers,
  `StableDiffusionBackendInspector.IsBundled`/`IsRuntimePresent`, and `RuntimeDialogViewModel`. Add
  the SDK's backend NuGet (mirror the `IncludeCuda` gated `PackageReference` pattern) so the native
  variant folder ships.
- **Add a page** - create `YourPage : Page` (`DataContext = vm`) + `YourViewModel : ObservableObject`;
  register both as singletons in `ConfigureGentastic`; add a `RadioButton` with `Tag="YourPage"` +
  `Checked="OnNavChecked"` in `MainWindow.xaml`; add the `tag -> typeof(YourPage)` case in
  `OnNavChecked` (and `OnLoaded`'s `GENTASTIC_SHOT_PAGE` switch if it should be a screenshot target).
  Use `Controls/PageHeader` for the title bar.
- **Add a setting** - add the field to `AppSettings`, an `[ObservableProperty]` in `SettingsViewModel`
  seeded from `settings.Current` in the ctor, persist in `Save()`, and note the restart requirement
  if it affects already-constructed singletons (engine/repository/model lists).
- **Add a modal a VM must show** - create a `FluentWindow`, add a method to `IContentGate`/`ContentGate`
  wrapping `ShowModal` (with the `Headless` guard), and inject `IContentGate` into the VM. Never
  `new` a `Window` inside a view model.

---

## 16. Where things stand

The app is functional end-to-end (download, load, sample, decode, save) on the AMD Radeon 8060S via
Vulkan: text-to-image, image-to-image, image editing / keep-face, presets, gallery, settings, batch,
progress+cancel, out-of-memory guidance, first-run runtime detection, GitHub update checks, and the
universal portable build. Development happens on branch `feat/foundation`. The GitHub repo is
`ideaconnect/gentastic`. Consult `git log` and the issue tracker for current status rather than
trusting a hardcoded snapshot here.

Known un-exercised area: CUDA generation has never run on real NVIDIA hardware (dev machine is AMD) -
bundling and detection are wired and verified, actual CUDA sampling is not. ROCm is detected but not
bundled (no working Windows/gfx1151 path yet).
