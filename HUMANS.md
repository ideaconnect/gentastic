# HUMANS.md - Gentastic for people

A plain-language guide to what Gentastic is, how to use it, and how image generation actually works
under the hood. No prior AI-art knowledge assumed. If you are an engineer (or an AI) about to change
the code, read [AGENTS.md](AGENTS.md) instead - this file is the friendly tour.

> Style note: plain hyphens throughout, no em-dashes.

---

## What is Gentastic?

Gentastic is a Windows app that turns text into images, entirely on your own PC. You type a
description ("a red fox in a snowy forest, golden hour"), pick a model, and it generates a picture.
No cloud, no subscription, no account, no Python to install. Your prompts and images never leave your
machine.

It is built for people who want good results without a control panel of a hundred sliders. It ships
with a curated set of models and sensible defaults, downloads what you need automatically the first
time you use it, and figures out how to use your graphics card on its own.

What it can do:
- **Text to image** - describe something, get a picture.
- **Image to image** - start from an existing image and let the model reinterpret it.
- **Editing / keep-face** - give it a photo and an instruction ("put them on a beach", "change the
  suit to grey"), or a reference face to keep across new scenes.
- **Presets** - save a prompt + settings combo and recall it later.
- **Gallery** - browse everything you've made; each image remembers the settings that made it.

---

## How to use it

1. **Models.** Open the Models page and download a model. The default, **FLUX.2 klein**, is fast and
   needs no account. Files are cached under your local app data, so you only download once.
2. **Generate.** On the Image generation page, type a prompt, choose a size, and click Generate. The
   first run of a new model spends a moment downloading and loading it; after that it's quick. You'll
   see progress per step, then a short "Decoding image" stage, then your picture. You can cancel any
   time.
   - Toggle **image to image** to drop in a starting picture and set how much freedom the model has
     to change it (denoise strength).
   - Some models offer **keep-face** or **edit** modes - give them a photo instead of starting blank.
   - Save your current setup as a **preset** to reuse it.
3. **Gallery.** Every image is auto-saved to your `Pictures\Gentastic` folder, with all its settings
   embedded inside the PNG file. The Gallery reads those back so you can always see how an image was
   made.
4. **Settings.** Set a Hugging Face token (only needed for a couple of license-gated models), choose
   your preferred graphics backend, change where models are cached, pick a Light/Dark theme, and
   opt in to the adult (18+) models if you want them.

---

## How image generation actually works

You don't need any of this to use the app, but it helps to know what's happening.

### The short version

A modern image model doesn't "draw." It **starts from pure random static and gradually cleans it up**
into a picture that matches your words. Do that cleanup step by step, guided by the prompt, and a
coherent image emerges from the noise. That process is called **diffusion**.

### Step by step

1. **Start with noise.** The model begins with a field of random numbers (think TV static, but in a
   compressed internal form). A **seed** is just the starting point for that randomness - the same
   seed with the same settings gives the same image every time, which is why you can reproduce a
   result or make small variations by nudging the seed.

2. **Sample.** The model then runs a fixed number of **steps** (often just 4 for the fast models, or
   ~25-30 for the detailed ones). At each step it looks at the current noisy state, predicts what
   should be removed to move closer to a clean image that matches your prompt, and removes a bit of
   it. Repeat, and the static resolves into shapes, then detail. The **sampler** is the specific
   algorithm used to do this cleanup (Euler, DPM++, and so on) - different samplers trade speed for
   quality. More steps generally means more refinement, up to a point of diminishing returns.

3. **Guidance (CFG).** "Classifier-free guidance" controls how strongly the model sticks to your
   prompt versus wandering creatively. Higher guidance = more literal, but too high looks harsh.
   - The fast **FLUX** models are "guidance-distilled": they were trained to need almost none, so
     they run at guidance 1 and are quick. A side effect: a **negative prompt** (things to avoid)
     does nothing on these unless you turn guidance above 1, which also makes them slower. The app
     greys out the negative-prompt box until you do, so you're never surprised.
   - The **SDXL** models (including the anime and photoreal ones) use normal guidance (~5) and always
     respect negative prompts.

4. **Decode (the VAE).** All that sampling happens in a small, compressed "latent" space, not in
   actual pixels. Once the latent is clean, a component called the **VAE** (variational autoencoder)
   expands it back into a full-resolution image you can see. This is a single final stage - that's
   the "Decoding image... almost there" message. It runs on the graphics card and takes a few
   seconds after the steps finish.

That's the whole pipeline: **noise -> sample for N steps guided by your prompt -> decode to pixels ->
save**.

### Image to image, and editing

- **Image to image** skips the "start from pure noise" part. It starts from *your* image with some
  noise mixed in, then samples as usual. The **denoise strength** decides how much noise: low keeps
  your image mostly intact, high nearly ignores it. It's a way to restyle or reinterpret a picture.
- **Editing / keep-face** models take your image (or a face photo) as a *reference* and change the
  scene from an instruction while preserving the parts you care about, like the person's identity.

### The models, in plain terms

Different models are trained on different data and have different strengths:
- **FLUX.2 klein** - the fast, modern default. Great all-rounder, quick.
- **FLUX.1 schnell / dev** - the previous FLUX generation. `dev` is higher quality but needs a free
  Hugging Face account to download (license terms); `schnell` is fully open.
- **SDXL family** (Illustrious, RealVisXL, Pony, NoobAI, and others) - a broad, mature ecosystem.
  Some are photoreal, some are anime. The anime ones often use "tag" prompting (comma-separated
  keywords) rather than sentences, and the app helps by adding the quality tags they expect.

Bigger, more detailed models are slower; the fast ones trade a little fidelity for speed. The app
picks sane defaults (size, steps, guidance) for whichever model you choose.

---

## Where your files live

- **Generated images:** `Pictures\Gentastic` (each PNG carries its own settings inside it).
- **Downloaded models:** your local app data (`%LOCALAPPDATA%\Gentastic\models`) - the big files,
  cached so you download once. You can change this location in Settings.
- **Settings and presets:** `%LOCALAPPDATA%\Gentastic` as small JSON files.

Nothing is uploaded anywhere. The only network activity is downloading models from Hugging Face (and
an optional check for app updates).

---

## Troubleshooting

- **"Ran out of memory."** Try a smaller image size, fewer steps, or a smaller model. Each model
  shows roughly how much GPU memory it needs right in the model picker (e.g. "~10 GB"), and the app
  warns you when a model likely won't fit your GPU.
- **A model won't download / says it's gated.** A few models require accepting a license on Hugging
  Face and setting a token in Settings. Most don't.
- **The progress bar sits at the last step for a moment.** That's the final decode stage finishing -
  it's normal and takes a few seconds.
- **It feels slow.** Speed depends heavily on your GPU and the model. The fast FLUX.2 klein model is
  the best starting point; the big photoreal/anime models are much slower per image.
- **Changed the backend or cache folder and nothing happened.** Those take effect after you restart
  the app.

---

## Adult (18+) content

Gentastic can run adult models, but they are **hidden by default**. Turning them on requires
confirming you're of legal age, and generating with any model that can depict a real person (adult
models, or the keep-face / edit models) asks you to acknowledge the terms first. This is deliberate:
the technology to reproduce a specific person's face carries real responsibility, and the app makes
you pause on it. Please use it lawfully and respect other people's consent and likeness.

---

## Contributing / picking up development

If you want to change the app itself, start with [AGENTS.md](AGENTS.md) - it explains the code
architecture, the generation pipeline in engineering detail, and how to add models, backends, pages,
and settings. Build and run with the .NET 10 SDK on Windows:

```sh
dotnet build Gentastic.slnx
dotnet run --project src/Gentastic.App
```

Support the project: [Buy me a coffee](https://buymeacoffee.com/idct). Report issues on
[GitHub](https://github.com/ideaconnect/gentastic/issues).
