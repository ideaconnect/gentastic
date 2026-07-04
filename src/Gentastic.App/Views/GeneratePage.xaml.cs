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
}
