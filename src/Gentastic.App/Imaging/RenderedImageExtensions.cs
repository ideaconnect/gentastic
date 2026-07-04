using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Gentastic.Core.Models;

namespace Gentastic.App.Imaging;

/// <summary>
/// Converts the engine's backend-agnostic <see cref="RenderedImage"/> (RGB/RGBA byte buffer) into
/// WPF imaging types for display and PNG export, using only the built-in codecs (no external image
/// library / license).
/// </summary>
public static class RenderedImageExtensions
{
    public static BitmapSource ToBitmapSource(this RenderedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var pixelCount = image.Width * image.Height;
        var expected = pixelCount * image.Channels;
        if (image.Pixels.Length < expected)
            throw new ArgumentException(
                $"Pixel buffer too small: got {image.Pixels.Length}, expected {expected}.", nameof(image));

        // Repack to BGRA32, which every WPF surface accepts.
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
        bitmap.Freeze();
        return bitmap;
    }

    public static ImageSource ToImageSource(this RenderedImage image) => image.ToBitmapSource();

    /// <summary>Encodes the image as PNG. Text metadata can be attached by the caller later.</summary>
    public static void SavePng(this RenderedImage image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image.ToBitmapSource()));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
