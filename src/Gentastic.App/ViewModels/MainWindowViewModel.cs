using CommunityToolkit.Mvvm.ComponentModel;

namespace Gentastic.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _applicationTitle = "Gentastic";
}
