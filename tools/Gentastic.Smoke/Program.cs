using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Gentastic.Core.Abstractions;
using Gentastic.Core.Models;
using Gentastic.Core.Services;
using Gentastic.Core.Settings;
using Gentastic.Engine;
using Gentastic.Hardware;
using Gentastic.Models;
using Microsoft.Extensions.Logging.Abstractions;

// Headless end-to-end smoke test (GitHub #12): resolve the cached FLUX.1-schnell model, load it on
// the detected backend (Vulkan on this machine), generate one image, and save a PNG to inspect.
// Usage: dotnet run --project tools/Gentastic.Smoke [outputPng] [modelId]

var outputPath = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "gentastic-smoke.png");
var modelId = args.Length > 1 ? args[1] : "flux1-schnell";

var cache = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gentastic", "models");

var spec = new ModelCatalog().FindById(modelId);
if (spec is null)
{
    Console.Error.WriteLine($"Unknown model '{modelId}'.");
    return 2;
}

string Local(ModelFile f) => Path.Combine(cache, f.Repo.Replace('/', Path.DirectorySeparatorChar), f.Path);
var files = spec.Files.ToDictionary(f => f.Role, Local);

var missing = files.Values.Where(p => !File.Exists(p)).ToList();
if (missing.Count > 0)
{
    Console.Error.WriteLine("Model not fully downloaded. Missing:");
    missing.ForEach(m => Console.Error.WriteLine("  " + m));
    return 3;
}

StableDiffusion.NET.StableDiffusionCpp.Log += (_, a) => Console.WriteLine($"[sd:{a.Level}] {a.Text}");

var detector = new RuntimeDetector(NullLogger<RuntimeDetector>.Instance);
var hardware = detector.Detect();
Console.WriteLine($"Runtime: {hardware.Summary}");
foreach (var probe in hardware.BackendProbes)
    Console.WriteLine($"  [{probe.Availability,-13}] {probe.Backend,-6} — {probe.Detail}");

// Fast path for verifying runtime detection without a multi-minute generation.
if (Environment.GetEnvironmentVariable("GENTASTIC_DETECT_ONLY") == "1")
    return 0;

// Run the full pipeline through GenerationService (download -> load -> sample) — the same path the
// app uses (GitHub #19). The model is already cached, so EnsureInstalled resolves without downloading.
var httpFactory = new SimpleHttpClientFactory();
var repository = new HuggingFaceModelRepository(httpFactory, NullLogger<HuggingFaceModelRepository>.Instance);
await using var engine = new StableDiffusionEngine(
    NullLogger<StableDiffusionEngine>.Instance, detector, new JsonSettingsService());
var service = new GenerationService(repository, engine, detector, NullLogger<GenerationService>.Instance);

// Size/steps/cfg default to a fast 512² pass but can be overridden to reproduce the app's real
// defaults (1024², see GenerateViewModel) for debugging VAE-decode memory behaviour, e.g.
//   GENTASTIC_W=1024 GENTASTIC_H=1024 dotnet run --project tools/Gentastic.Smoke
static int EnvInt(string name, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;
static float EnvFloat(string name, float fallback) =>
    float.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

var request = new TextToImageRequest
{
    Prompt = "a single red apple on a wooden table, studio photo, sharp focus, soft light",
    Width = EnvInt("GENTASTIC_W", 512),
    Height = EnvInt("GENTASTIC_H", 512),
    Steps = EnvInt("GENTASTIC_STEPS", 4),
    Seed = 42,
    Cfg = EnvFloat("GENTASTIC_CFG", 1.0f),
    Sampler = Sampler.Euler,
};
Console.WriteLine($"Request: {request.Width}x{request.Height}, {request.Steps} steps, cfg {request.Cfg}.");

var sw = Stopwatch.StartNew();
var lastTick = TimeSpan.Zero;
var image = await service.RunAsync(
    request, spec, new Progress<GenerationStatus>(s =>
    {
        var now = sw.Elapsed;
        Console.WriteLine($"  [{now.TotalSeconds,6:F1}s (+{(now - lastTick).TotalSeconds,5:F1})] [{s.Stage}] {s.Message}");
        lastTick = now;
    }));

var distinct = image.Pixels.Distinct().Count();
Console.WriteLine($"Generated {image.Width}x{image.Height} in {sw.Elapsed.TotalSeconds:F1}s on {engine.Backend} "
                + $"({image.Pixels.Length} bytes, {distinct} distinct values).");

SavePng(image, outputPath);
Console.WriteLine($"Saved {outputPath}");

// Optional image-to-image pass (#22/#24): reuse the result as the init image.
if (Environment.GetEnvironmentVariable("GENTASTIC_I2I") == "1")
{
    var i2iRequest = new ImageToImageRequest
    {
        Prompt = "the same apple resting on fresh snow, cold winter light",
        InitImage = image,
        DenoiseStrength = 0.6f,
        Width = 512,
        Height = 512,
        Steps = 4,
        Seed = 7,
        Cfg = 1.0f,
        Sampler = Sampler.Euler,
    };
    var i2iImage = await service.RunAsync(
        i2iRequest, spec, new Progress<GenerationStatus>(s => Console.WriteLine($"  i2i [{s.Stage}] {s.Message}")));
    var i2iPath = Path.Combine(
        Path.GetDirectoryName(outputPath) ?? ".",
        Path.GetFileNameWithoutExtension(outputPath) + "-i2i.png");
    SavePng(i2iImage, i2iPath);
    Console.WriteLine($"i2i saved {i2iPath} ({i2iImage.Pixels.Distinct().Count()} distinct values).");
}

// A real image has plenty of tonal variety; near-uniform output signals a broken pipeline.
return distinct < 16 ? 4 : 0;

static void SavePng(RenderedImage image, string path)
{
    var pixelCount = image.Width * image.Height;
    var bgra = new byte[pixelCount * 4];
    var src = image.Pixels;
    for (int i = 0, p = 0; i < pixelCount; i++)
    {
        byte r = src[p++], g = src[p++], b = src[p++];
        byte a = image.Channels == 4 ? src[p++] : (byte)255;
        var o = i * 4;
        bgra[o + 0] = b;
        bgra[o + 1] = g;
        bgra[o + 2] = r;
        bgra[o + 3] = a;
    }

    var bitmap = BitmapSource.Create(
        image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, bgra, image.Width * 4);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var ms = new MemoryStream();
    encoder.Save(ms);

    // Exercise the #21 metadata path on a real generated image.
    var png = Gentastic.Core.Imaging.PngMetadata.AddTextChunks(ms.ToArray(),
    [
        ("Software", "Gentastic"),
        ("prompt", "smoke-test: red apple on a wooden table"),
        ("model", "flux1-schnell"),
    ]);
    File.WriteAllBytes(path, png);
}

sealed class SimpleHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
