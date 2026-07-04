# Gentastic

A modern, simple, elegant Windows app for running **FLUX** image-generation models locally —
a focused, out-of-the-box alternative to Amuse AI / ComfyUI with sane defaults instead of endless
knobs. Text-to-image and image-to-image, with automatic model download and automatic runtime
detection. No Python.

> **Status:** early foundation (milestone **M0**). Hardware detection, the model catalog and
> Hugging Face downloads work today; the native generation call lands in **M1** (see Issues).

## How it works

| Concern | Choice |
|---|---|
| UI | WPF (.NET 10) + [WPF-UI](https://github.com/lepoco/wpfui) Fluent, MVVM (CommunityToolkit.Mvvm) |
| Inference | [StableDiffusion.NET](https://github.com/DarthAffe/StableDiffusion.NET) over stable-diffusion.cpp |
| Backend | **Vulkan** — the stable, fast path on AMD (incl. Radeon 8060S / Strix Halo). No ROCm, no DirectML, no Python. |
| Models | FLUX.1 schnell + dev as quantized GGUF, downloaded from Hugging Face on demand |

The engine is wrapped behind `IDiffusionEngine`, so a future CUDA / ROCm / Windows-ML backend is a
swap rather than a rewrite.

### Projects

- `Gentastic.App` — WPF-UI shell (Generate / Models / Settings), DI bootstrap
- `Gentastic.Core` — domain models, service interfaces, generation orchestration
- `Gentastic.Engine` — StableDiffusion.NET wrapper (Vulkan)
- `Gentastic.Hardware` — GPU/runtime detection (DXGI via Vortice)
- `Gentastic.Models` — model catalog + Hugging Face downloader/cache

## Build & run

```sh
dotnet build Gentastic.slnx -c Debug
dotnet run --project src/Gentastic.App
```

Requires the **.NET 10 SDK** on Windows. Models download to `%LOCALAPPDATA%\Gentastic\models`.

## Notes

- **Negative prompt:** base FLUX is guidance-distilled, so a negative prompt only takes effect with
  CFG > 1 (roughly 2× slower). The UI surfaces this.
- **FLUX.1-dev** is license-gated (non-commercial); set an `HF_TOKEN` environment variable to
  download it. **FLUX.1-schnell** is Apache-2.0 and needs no token.

## License

BSD 3-Clause © IDCT Bartosz Pachołek. Bundles/uses third-party components under their own licenses
(WPF-UI · MIT, StableDiffusion.NET · MIT, Vortice.Windows · MIT).
