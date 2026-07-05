# Gentastic

A modern, simple, elegant Windows app for running **FLUX** image-generation models locally — a
focused, out-of-the-box alternative to Amuse AI / ComfyUI with sane defaults instead of endless
knobs. Text-to-image and image-to-image, automatic model download, automatic runtime detection.
**No Python.**

<p align="center">
  <img src="docs/images/sample-txt2img.png" width="45%" alt="Text-to-image sample" />
  <img src="docs/images/sample-img2img.png" width="45%" alt="Image-to-image sample" />
</p>
<p align="center"><em>Generated locally on an AMD Radeon 8060S via Vulkan — text-to-image (left),
then image-to-image reusing it as the init image (right).</em></p>

## Features

- **Text-to-image** and **image-to-image** (drag & drop an input image + denoise strength).
- **FLUX.1 schnell & dev** as quantized GGUF, **auto-downloaded** from Hugging Face on demand.
- **Automatic runtime detection** — picks the best backend for your GPU (Vulkan on AMD).
- **Presets** — save & recall prompt + parameter sets.
- **Gallery** — browse past generations with their embedded parameters.
- **Auto-save** with generation metadata embedded in each PNG (prompt, seed, steps, cfg, …).
- **Settings** — Hugging Face token, backend override, cache folder, Dark/Light/System theme.
- Progress + cancel, negative-prompt CFG gating, out-of-memory guidance.

## How it works

| Concern | Choice |
|---|---|
| UI | WPF (.NET 10) + [WPF-UI](https://github.com/lepoco/wpfui) Fluent, MVVM (CommunityToolkit.Mvvm) |
| Inference | [StableDiffusion.NET](https://github.com/DarthAffe/StableDiffusion.NET) over stable-diffusion.cpp |
| Backend | **Vulkan** — the stable, fast path on AMD (incl. Radeon 8060S / Strix Halo). No ROCm, no DirectML, no Python. |
| Models | FLUX.1 schnell + dev as quantized GGUF, downloaded from Hugging Face |

The engine is wrapped behind `IDiffusionEngine`, so a future CUDA / ROCm / Windows-ML backend is a
swap rather than a rewrite.

### Projects

- `Gentastic.App` — WPF-UI shell (Generate / Models / Gallery / Settings), DI bootstrap
- `Gentastic.Core` — domain models, service interfaces, orchestration, settings & presets, PNG metadata
- `Gentastic.Engine` — StableDiffusion.NET wrapper (Vulkan)
- `Gentastic.Hardware` — GPU/runtime detection (DXGI via Vortice)
- `Gentastic.Models` — model catalog + Hugging Face downloader/cache

## Build & run

```sh
dotnet build Gentastic.slnx -c Debug
dotnet run --project src/Gentastic.App
```

Requires the **.NET 10 SDK** on Windows with a **DirectX 12 / Vulkan-capable GPU**. The native
backends ship with the app; no separate install is needed.

## Using it

1. **Models** — download a model (FLUX.1-schnell is open and needs no token). Files cache to
   `%LOCALAPPDATA%\Gentastic\models`.
2. **Generate** — enter a prompt, pick a size/steps/seed, and hit Generate. Toggle **image-to-image**
   to drop in an input image and set a denoise strength. Save the current settings as a **preset**.
3. **Gallery** — browse everything you've made (saved to `Pictures\Gentastic`), with the parameters
   read back from each PNG.
4. **Settings** — Hugging Face token, preferred backend, cache folder, theme.

## Notes

- **Negative prompt:** base FLUX is guidance-distilled, so it only takes effect with **CFG > 1**
  (roughly 2× slower); the field is gated until then.
- **FLUX.1-dev** is license-gated (non-commercial). Set a Hugging Face token in Settings (or the
  `HF_TOKEN` environment variable) to download it. **FLUX.1-schnell** is Apache-2.0 and needs no token.
- **Backend** is fixed for the process lifetime; changing it in Settings takes effect on restart.

## License

BSD 3-Clause © IDCT Bartosz Pachołek. Uses third-party components under their own licenses
(WPF-UI · MIT, StableDiffusion.NET · MIT, Vortice.Windows · MIT). FLUX models carry their own
licenses (schnell: Apache-2.0; dev: non-commercial).
