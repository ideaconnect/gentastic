using Gentastic.App.ViewModels;
using Gentastic.App.Views;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace Gentastic.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(
        MainWindowViewModel viewModel,
        INavigationViewPageProvider pageProvider,
        INavigationService navigationService)
    {
        DataContext = viewModel;
        InitializeComponent();

        RootNavigation.SetPageProviderService(pageProvider);
        navigationService.SetNavigationControl(RootNavigation);

        // Navigate once the NavigationView's template is applied; navigating from the constructor
        // races the control's content presenter and throws inside UpdateContent.
        Loaded += (_, _) => RootNavigation.Navigate(typeof(GeneratePage));
    }
}
