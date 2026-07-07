using System.Windows.Controls;
using Gentastic.App.ViewModels;

namespace Gentastic.App.Views;

public partial class ModelsPage : Page
{
    public ModelsPage(ModelsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
