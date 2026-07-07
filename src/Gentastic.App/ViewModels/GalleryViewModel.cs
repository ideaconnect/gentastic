using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gentastic.Core.Imaging;

namespace Gentastic.App.ViewModels;

/// <summary>One saved image in the gallery. The thumbnail is filled in asynchronously (off the UI
/// thread) after the item appears, so a large gallery doesn't freeze the UI while it loads.</summary>
public sealed partial class GalleryItem(string path) : ObservableObject
{
    public string FilePath { get; } = path;
    public string FileName { get; } = Path.GetFileName(path);

    [ObservableProperty] private ImageSource? _thumbnail;
}

/// <summary>Caches small gallery thumbnails on disk so opening the gallery doesn't re-decode every
/// full-resolution PNG each time. Entries are ~240px JPEGs keyed by source path + last-write time.</summary>
internal static class ThumbnailCache
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gentastic", "thumbnails");

    /// <summary>Returns a small, frozen thumbnail for <paramref name="sourcePath"/> - from the on-disk
    /// cache when present (fast: a tiny JPEG), else decodes the full image at reduced resolution, writes
    /// it to the cache, and returns it. Safe to call off the UI thread (the result is frozen).</summary>
    public static ImageSource Load(string sourcePath, int width)
    {
        var cachePath = CachePathFor(sourcePath, width);
        if (File.Exists(cachePath))
        {
            try { return Decode(cachePath, 0); }
            catch { /* corrupt/partial cache entry - fall through and regenerate */ }
        }

        var thumb = Decode(sourcePath, width);
        try
        {
            Directory.CreateDirectory(CacheDir);
            var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
            encoder.Frames.Add(BitmapFrame.Create((BitmapSource)thumb));
            var tmp = cachePath + ".tmp";
            using (var fs = File.Create(tmp))
                encoder.Save(fs);
            File.Move(tmp, cachePath, overwrite: true); // atomic publish so a reader never sees a half file
        }
        catch { /* caching is best-effort - a failure just means we decode again next time */ }
        return thumb;
    }

    private static ImageSource Decode(string path, int decodeWidth)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;   // read fully, don't lock the file
        if (decodeWidth > 0)
            bitmap.DecodePixelWidth = decodeWidth;       // decode small = low memory + faster
        bitmap.EndInit();
        bitmap.Freeze();                                 // cross-thread safe
        return bitmap;
    }

    private static string CachePathFor(string sourcePath, int width)
    {
        var ticks = File.GetLastWriteTimeUtc(sourcePath).Ticks;
        var key = $"{sourcePath.ToLowerInvariant()}|{ticks}|{width}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(CacheDir, hash[..24] + ".jpg");
    }
}

/// <summary>Browses previously generated images (Pictures/Gentastic) and shows their embedded
/// PNG metadata (#26).</summary>
public partial class GalleryViewModel : ObservableObject
{
    private const int ThumbnailWidth = 240;

    public ObservableCollection<GalleryItem> Items { get; } = [];

    [ObservableProperty] private GalleryItem? _selectedItem;
    [ObservableProperty] private ImageSource? _selectedImage;
    [ObservableProperty] private string _metadata = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    public GalleryViewModel() => _ = Refresh();

    [RelayCommand]
    private async Task Refresh()
    {
        Items.Clear();
        var dir = GenerateViewModel.OutputDirectory;
        var paths = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.png").OrderByDescending(File.GetLastWriteTimeUtc).ToList()
            : [];

        // Add the items immediately (blank thumbnails) so the grid appears at once instead of freezing.
        var items = paths.Select(p => new GalleryItem(p)).ToList();
        foreach (var item in items)
            Items.Add(item);

        Status = items.Count == 0
            ? $"No images yet - generated images are saved to {dir}."
            : $"{items.Count} image(s) · {dir}";
        SelectedItem = Items.FirstOrDefault();

        if (items.Count == 0)
            return;

        // Decode thumbnails off the UI thread (cached on disk → fast on repeat loads); fill them in
        // progressively so images pop in as they're ready.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                try
                {
                    var thumb = ThumbnailCache.Load(item.FilePath, ThumbnailWidth);
                    if (dispatcher is null)
                        item.Thumbnail = thumb;
                    else
                        dispatcher.BeginInvoke(() => item.Thumbnail = thumb);
                }
                catch { /* skip unreadable files */ }
            }
        }).ConfigureAwait(false);
    }

    partial void OnSelectedItemChanged(GalleryItem? value)
    {
        if (value is null)
        {
            SelectedImage = null;
            Metadata = string.Empty;
            return;
        }

        try
        {
            var full = new BitmapImage();
            full.BeginInit();
            full.UriSource = new Uri(value.FilePath, UriKind.Absolute);
            full.CacheOption = BitmapCacheOption.OnLoad;
            full.EndInit();
            full.Freeze();
            SelectedImage = full;

            var chunks = PngMetadata.ReadTextChunks(File.ReadAllBytes(value.FilePath));
            Metadata = chunks.Count == 0
                ? "(no embedded metadata)"
                : string.Join(Environment.NewLine, chunks.Select(c => $"{c.Keyword}: {c.Text}"));
        }
        catch (Exception ex)
        {
            Metadata = $"Couldn't read image: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(GenerateViewModel.OutputDirectory);
        Process.Start(new ProcessStartInfo { FileName = GenerateViewModel.OutputDirectory, UseShellExecute = true });
    }

    [RelayCommand]
    private void Delete(GalleryItem? item)
    {
        if (item is null)
            return;
        try
        {
            File.Delete(item.FilePath);
            Items.Remove(item);
            Status = $"Deleted {item.FileName}.";
            SelectedItem = Items.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Status = $"Delete failed: {ex.Message}";
        }
    }
}
