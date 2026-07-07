using System.Windows;
using Gentastic.App.Services;
using Wpf.Ui.Controls;

namespace Gentastic.App.Views;

/// <summary>Age-confirmation modal shown when the user enables the adult (18+) models. Sets
/// <see cref="Window.DialogResult"/> to true only once the user ticks the box and confirms.</summary>
public partial class AgeConfirmationDialog : FluentWindow
{
    public AgeConfirmationDialog()
    {
        InitializeComponent();

        // Screenshot mode: render in software (no Mica) so the self-capture grabs real content, and
        // optionally pre-tick the box ("…-checked") to capture the enabled-button state.
        var shot = Environment.GetEnvironmentVariable("GENTASTIC_SHOT_DIALOG");
        if (!string.IsNullOrEmpty(shot))
        {
            WindowBackdropType = WindowBackdropType.None;
            if (shot.Contains("checked"))
                AgeCheck.IsChecked = true;
        }
        DialogScreenshot.AttachIfRequested(this);
    }

    private void OnCheckChanged(object sender, RoutedEventArgs e)
        => ConfirmButton.IsEnabled = AgeCheck.IsChecked == true;

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
