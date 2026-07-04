using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Gentastic.Core.Models;
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

await using var engine = new StableDiffusionEngine(NullLogger<StableDiffusionEngine>.Instance, detector);

var sw = Stopwatch.StartNew();
await engine.LoadModelAsync(new ModelInstallation(spec, files), hardware);
Console.WriteLine($"Loaded {spec.DisplayName} in {sw.Elapsed.TotalSeconds:F1}s on {engine.Backend}. Generating…");

sw.Restart();
var request = new TextToImageRequest
{
    Prompt = "a single red apple on a wooden table, studio photo, sharp focus, soft light",
    Width = 512,
    Height = 512,
    Steps = 4,
    Seed = 42,
    Cfg = 1.0f,
    Sampler = Sampler.Euler,
};
var image = await engine.TextToImageAsync(
    request, new Progress<GenerationProgress>(p => Console.WriteLine($"  step {p.Step}/{p.TotalSteps}")));

var distinct = image.Pixels.Distinct().Count();
Console.WriteLine($"Generated {image.Width}x{image.Height} in {sw.Elapsed.TotalSeconds:F1}s "
                + $"({image.Pixels.Length} bytes, {distinct} distinct values).");

SavePng(image, outputPath);
Console.WriteLine($"Saved {outputPath}");

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
