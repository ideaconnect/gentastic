using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Gentastic.App.Services;

/// <summary>Env-gated self-capture used to screenshot dialogs headlessly: after a short settle it renders
/// the window's own visual tree to a PNG (reliable under software rendering, unlike screen-grabbing a
/// Mica/GPU-composited window) and shuts the app down. Mirrors the capture in MainWindow/RuntimeDialog.</summary>
internal static class DialogScreenshot
{
    /// <summary>When GENTASTIC_SHOT_DIALOG is set, arm the capture for this window.</summary>
    public static void AttachIfRequested(Window window)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENTASTIC_SHOT_DIALOG")))
            return;

        window.ContentRendered += (_, _) =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var path = Environment.GetEnvironmentVariable("GENTASTIC_SHOT_PATH");
                if (!string.IsNullOrWhiteSpace(path))
                    CaptureToPng(window, path);
                Application.Current.Shutdown();
            };
            timer.Start();
        };
    }

    private static void CaptureToPng(Window window, string path)
    {
        var width = (int)window.ActualWidth;
        var height = (int)window.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
