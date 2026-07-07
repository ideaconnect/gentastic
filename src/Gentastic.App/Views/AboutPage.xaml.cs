using System.Windows.Controls;
using Gentastic.App.ViewModels;

namespace Gentastic.App.Views;

public partial class AboutPage : Page
{
    public AboutPage(AboutViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
