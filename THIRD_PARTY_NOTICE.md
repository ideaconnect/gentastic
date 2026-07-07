# Third-party notices

Gentastic is licensed under the **BSD 3-Clause License** (see [LICENSE](LICENSE), Copyright (c)
2026 IDCT Bartosz Pachołek). It redistributes and builds on the third-party software listed below.
This file plus the full license texts in [`licenses/`](licenses/) are included in the portable
distribution to satisfy those components' notice and attribution requirements.

> Plain-hyphen style, no em-dashes, matching the rest of the project.

## Compatibility summary

Everything Gentastic ships is compatible with distributing the app under BSD 3-Clause. All bundled
code is permissively licensed (MIT / Apache-2.0) except **HPPH**, which is LGPL-2.1-only weak
copyleft satisfied by dynamic linking (see the note below). The bundled icon font carries
attribution-only obligations (CC BY 4.0 / SIL OFL 1.1). Components fetched at runtime (the NVIDIA
CUDA runtime and the AI models) are obtained by the user from their original vendors under their own
terms and are **not** redistributed inside Gentastic.

| License | Type | Bundled components | Your obligation |
|---|---|---|---|
| MIT | Permissive | .NET runtime + WPF, Microsoft.Extensions.*, StableDiffusion.NET (+ stable-diffusion.cpp, ggml), WPF UI, CommunityToolkit.Mvvm, Vortice.Windows, SharpGen.Runtime, JetBrains.Annotations | Reproduce copyright + license text (this file) |
| Apache-2.0 | Permissive | FontAwesome.Sharp | Include license text; propagate NOTICE if any (none shipped) |
| LGPL-2.1-only | Weak copyleft | HPPH (`HPPH.dll`) | Keep the DLL replaceable, ship LGPL text, offer HPPH source |
| CC BY 4.0 | Attribution | Font Awesome Free icons | Attribute Font Awesome + link the license |
| SIL OFL 1.1 | Font license | Font Awesome Free fonts (embedded in `FontAwesome.Sharp.dll`) | Include OFL text; keep the Reserved Font Name |

Conclusion: **licenses are compatible**; the obligations are attribution/notice inclusion (this
file and `licenses/`) plus keeping `HPPH.dll` a replaceable standalone file, which the portable
build already does (it is self-contained but not single-file).

---

## A. Redistributed inside the Gentastic portable build

### MIT License

The following are used under the MIT License. Full text: [`licenses/MIT.txt`](licenses/MIT.txt).

| Component | Version | Copyright | Source |
|---|---|---|---|
| StableDiffusion.NET | 7.0.0 | (c) Darth Affe | https://github.com/DarthAffe/StableDiffusion.NET |
| StableDiffusion.NET native backends (CPU / Vulkan / CUDA 12), i.e. `stable-diffusion.dll` | 7.0.0 | (c) Darth Affe | https://github.com/DarthAffe/StableDiffusion.NET |
| &nbsp;&nbsp;↳ stable-diffusion.cpp (inside the native DLL) | bundled | (c) 2023 leejet | https://github.com/leejet/stable-diffusion.cpp |
| &nbsp;&nbsp;↳ ggml (inside the native DLL) | bundled | (c) 2022 Georgi Gerganov | https://github.com/ggml-org/ggml |
| WPF UI (`Wpf.Ui`, `Wpf.Ui.Abstractions`, `Wpf.Ui.DependencyInjection`) | 4.3.0 | (c) 2021-2026 Leszek Pomianowski and WPF UI Contributors | https://github.com/lepoco/wpfui |
| CommunityToolkit.Mvvm | 8.4.2 | (c) .NET Foundation and Contributors | https://github.com/CommunityToolkit/dotnet |
| Vortice.Windows (`Vortice.DXGI`, `Vortice.DirectX`, `Vortice.Mathematics`) | 3.8.3 / 2.1.0 | (c) Amer Koleci and Contributors | https://github.com/amerkoleci/Vortice.Windows |
| SharpGen.Runtime (`SharpGen.Runtime`, `SharpGen.Runtime.COM`) | 2.4.2-beta | (c) 2010-2024 Alexandre Mutel, Jeremy Koritzinsky, Amer Koleci | https://github.com/SharpGenTools/SharpGenTools |
| JetBrains.Annotations | 2025.2.0 | (c) 2016-2025 JetBrains s.r.o. | https://github.com/JetBrains/JetBrains.Annotations |
| .NET runtime + WPF (self-contained: `coreclr`, `wpfgfx_cor3.dll`, PresentationCore/Framework, etc.) | 10.0.x | (c) .NET Foundation and Contributors; (c) Microsoft Corporation | https://github.com/dotnet/runtime · https://github.com/dotnet/wpf |
| Microsoft.Extensions.* (Hosting, Http, DependencyInjection, Logging, Configuration, Options, Primitives, …) | 10.0.9 | (c) .NET Foundation and Contributors | https://github.com/dotnet/runtime |

Note: the native `stable-diffusion.dll` files that ship under `runtimes/win-x64/native/*` embed
**stable-diffusion.cpp** and **ggml** (both MIT). Their verbatim license texts are also carried in
the StableDiffusion.NET backend packages as `stable-diffusion.cpp.txt` and `ggml.txt`.

### Apache License 2.0

Full text: [`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt).

| Component | Version | Copyright | Source |
|---|---|---|---|
| FontAwesome.Sharp (WPF icon wrapper) | 6.6.0 | (c) 2015-2022 Awesome Inc. and FontAwesome.Sharp contributors | https://github.com/awesome-inc/FontAwesome.Sharp |

FontAwesome.Sharp ships no `NOTICE` file, so there are no additional Apache-2.0 attribution notices
to propagate beyond the license text above. The icon font it embeds is covered separately in
section C.

### GNU Lesser General Public License v2.1 (LGPL-2.1-only) - HPPH

Full text: [`licenses/LGPL-2.1.txt`](licenses/LGPL-2.1.txt).

This product includes **HPPH**, Copyright (c) Darth Affe and the HPPH contributors, licensed under
the **GNU Lesser General Public License, version 2.1 (LGPL-2.1-only)**. HPPH provides the
`Image<ColorRGB>` pixel type used at the boundary with stable-diffusion.cpp; it arrives as a
transitive dependency of StableDiffusion.NET.

HPPH is used **unmodified** and is redistributed as a **separate, dynamically-linked .NET assembly
(`HPPH.dll`)**. Under LGPL-2.1 section 6, this "work that uses the Library" may be distributed under
Gentastic's own BSD-3-Clause terms; the rest of Gentastic is **not** covered by the LGPL, and
Gentastic's source is not subject to LGPL copyleft.

- **Replaceability (LGPL-2.1 section 6b):** `HPPH.dll` ships as a standalone file next to the
  application (the portable build is self-contained but **not** single-file, and is not
  ILMerged/trimmed). You may build a modified, interface-compatible HPPH from source and replace
  `HPPH.dll` in the install folder; the application will load your build.
- **Corresponding source:** the complete source for the exact version shipped (**HPPH 1.0.0**) is
  at https://github.com/DarthAffe/HPPH, tag/commit
  `6c9c7ef832df318ddb5babd1e97c1d88d8d46d15` (NuGet package `HPPH` 1.0.0). If you need it by another
  means, a written offer valid for three years is available on request via the project's issue
  tracker.

---

## B. Bundled font and icon assets

### Font Awesome Free 6 - icons (CC BY 4.0) and fonts (SIL OFL 1.1)

Gentastic renders UI icons using **Font Awesome Free 6**, whose font files are embedded inside
`FontAwesome.Sharp.dll` and therefore redistributed with the app.

- **Icons:** Font Awesome Free, Copyright (c) Fonticons, Inc. (https://fontawesome.com), licensed
  under **Creative Commons Attribution 4.0 International (CC BY 4.0)**,
  https://creativecommons.org/licenses/by/4.0/. The icons are used unmodified. Full text:
  [`licenses/CC-BY-4.0.txt`](licenses/CC-BY-4.0.txt).
- **Fonts:** the Font Awesome Free font files are licensed under the **SIL Open Font License 1.1**
  (https://openfontlicense.org). "Font Awesome" is a Reserved Font Name; the fonts are redistributed
  unmodified. Full text: [`licenses/OFL-1.1.txt`](licenses/OFL-1.1.txt).

---

## C. Fetched at runtime (not redistributed inside Gentastic)

These are downloaded by the app, on the user's instruction, directly from their original vendors.
Gentastic does not bundle or mirror them; they remain under their vendors' own terms.

### NVIDIA CUDA runtime (optional, NVIDIA GPUs)

When a user opts to enable CUDA, Gentastic downloads the **NVIDIA CUDA Runtime (`cudart`, 12.8.90)**
and **NVIDIA cuBLAS (`cublas`, 12.8.4.1)** redistributable libraries directly from NVIDIA's official
redistributable server (`developer.download.nvidia.com/compute/cuda/redist`) into the user's local
app data, unmodified. Both are on the CUDA Toolkit EULA's Attachment A redistributable list.

> This product uses the NVIDIA(R) CUDA(R) Runtime (cudart) and NVIDIA cuBLAS libraries,
> redistributed unmodified under the terms of the NVIDIA CUDA Toolkit End User License Agreement
> (https://docs.nvidia.com/cuda/eula/index.html). Copyright (C) NVIDIA Corporation. All rights
> reserved. NVIDIA, CUDA, and cuBLAS are trademarks and/or registered trademarks of NVIDIA
> Corporation in the U.S. and other countries. This product is not sponsored or endorsed by NVIDIA
> Corporation.

The NVIDIA CUDA **driver** (`nvcuda.dll`) is supplied by the user's installed GPU driver and is not
distributed with Gentastic.

### Vulkan

On Vulkan-capable GPUs, Gentastic uses the Vulkan runtime supplied by the user's installed GPU
driver (the ICD/loader is part of the driver, not bundled). **Vulkan** is a registered trademark of
the Khronos Group.

### AI models (from Hugging Face)

Models are downloaded on demand from the Hugging Face Hub at the user's request and cached locally;
Gentastic does **not** redistribute model weights. Each model is governed by its own license, shown
in-app on the Models page and defined in the catalog
([`ModelCatalog.cs`](src/Gentastic.Models/ModelCatalog.cs)). Users are responsible for complying
with each model's license, **in particular the non-commercial terms** of FLUX.1 [dev] and its
finetunes.

| Model (catalog id) | License |
|---|---|
| `flux1-schnell`, `flux1-schnell-q8` | Apache-2.0 |
| `flux1-dev`, `flux1-dev-q8` | FLUX.1 [dev] Non-Commercial License (gated) |
| `flux1-modern-anime`, `acorn-hardcore-flux1` | FLUX.1 [dev] finetune - Non-Commercial |
| `flux-kontext-edit` | FLUX.1 Kontext [dev] - Non-Commercial |
| `flux2-klein-4b`, `-edit`, `-uncensored`, `-uncensored-edit` | FLUX.2 [klein] (+ Apache-2.0 Qwen3 text encoder) |
| `photomaker-keepface` | PhotoMaker v1 (Apache-2.0) + RealVisXL (OpenRAIL++-M) |
| `realvisxl-v5` | RealVisXL V5.0 - CreativeML Open RAIL++-M |
| `illustrious-xl` | Illustrious-XL - Fair AI Public License 1.0-SD |
| `noobai-xl-hardcore` | NoobAI-XL v1.1 - Fair AI Public License 1.0-SD |
| `lustify-hardcore-sdxl` | LUSTIFY! SDXL v2.0 (community re-host) |
| `cyberrealistic-pony`, `-keepface` | CyberRealistic Pony - Pony Diffusion V6 XL (SDXL) |

Shared FLUX text encoders / VAE downloaded alongside FLUX models: CLIP-L and T5-XXL
(`comfyanonymous/flux_text_encoders`) and the FLUX VAE (`Comfy-Org` re-hosts) under their respective
upstream licenses (OpenAI CLIP - MIT; Google T5 - Apache-2.0; FLUX VAE per the FLUX model license).

---

## Full license texts

Verbatim copies are in [`licenses/`](licenses/):

- [`MIT.txt`](licenses/MIT.txt)
- [`Apache-2.0.txt`](licenses/Apache-2.0.txt)
- [`LGPL-2.1.txt`](licenses/LGPL-2.1.txt)
- [`OFL-1.1.txt`](licenses/OFL-1.1.txt) (SIL Open Font License 1.1)
- [`CC-BY-4.0.txt`](licenses/CC-BY-4.0.txt)
- [`BSD-3-Clause.txt`](licenses/BSD-3-Clause.txt) (Gentastic's own license)

_Last reviewed 2026-07-07 against the shipped portable build (`dist/Gentastic-portable-win-x64.zip`)._
