using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gentastic.Core.Imaging;

namespace Gentastic.App.ViewModels;

/// <summary>One saved image in the gallery, with a lazily-decoded thumbnail.</summary>
public sealed class GalleryItem
{
    public GalleryItem(string path)
    {
        FilePath = path;
        FileName = Path.GetFileName(path);
        Thumbnail = LoadThumbnail(path, 240);
    }

    public string FilePath { get; }
    public string FileName { get; }
    public ImageSource Thumbnail { get; }

    private static ImageSource LoadThumbnail(string path, int decodeWidth)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad; // don't lock the file
        bitmap.DecodePixelWidth = decodeWidth;         // small = low memory
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

/// <summary>Browses previously generated images (Pictures/Gentastic) and shows their embedded
/// PNG metadata (#26).</summary>
public partial class GalleryViewModel : ObservableObject
{
    public ObservableCollection<GalleryItem> Items { get; } = [];

    [ObservableProperty] private GalleryItem? _selectedItem;
    [ObservableProperty] private ImageSource? _selectedImage;
    [ObservableProperty] private string _metadata = string.Empty;
    [ObservableProperty] private string _status = string.Empty;

    public GalleryViewModel() => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        Items.Clear();
        var dir = GenerateViewModel.OutputDirectory;
        if (Directory.Exists(dir))
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*.png")
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    Items.Add(new GalleryItem(path));
                }
                catch
                {
                    // skip unreadable files
                }
            }
        }

        Status = Items.Count == 0
            ? $"No images yet — generated images are saved to {dir}."
            : $"{Items.Count} image(s) · {dir}";
        SelectedItem = Items.FirstOrDefault();
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
