using System.Windows;
using System.Windows.Controls;
using Gentastic.App.ViewModels;

namespace Gentastic.App.Views;

public partial class GeneratePage : Page
{
    public GeneratePage(GenerateViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnInitImageDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnInitImageDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files
            && DataContext is GenerateViewModel viewModel)
        {
            viewModel.LoadInitImage(files[0]);
        }
    }
}
