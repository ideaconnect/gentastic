using System.Windows.Controls;
using Gentastic.App.ViewModels;

namespace Gentastic.App.Views;

public partial class GalleryPage : Page
{
    public GalleryPage(GalleryViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
