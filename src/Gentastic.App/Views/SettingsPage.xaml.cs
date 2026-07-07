using System.Windows.Controls;
using Gentastic.App.ViewModels;

namespace Gentastic.App.Views;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
