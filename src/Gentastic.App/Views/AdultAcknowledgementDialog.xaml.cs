using System.Windows;
using Gentastic.App.Services;
using Wpf.Ui.Controls;

namespace Gentastic.App.Views;

/// <summary>Acknowledgement modal shown before each generation with a model that requires it - adult
/// (18+) models and real-face "keep face" / image-edit models (see
/// <c>ModelSpec.RequiresContentAcknowledgement</c>). All four boxes must be ticked before
/// <see cref="Window.DialogResult"/> is set to true.</summary>
public partial class AdultAcknowledgementDialog : FluentWindow
{
    public AdultAcknowledgementDialog()
    {
        InitializeComponent();

        // Screenshot mode: render in software (no Mica) so the self-capture grabs real content, and
        // optionally pre-tick every box ("…-checked") to capture the enabled-button state.
        var shot = Environment.GetEnvironmentVariable("GENTASTIC_SHOT_DIALOG");
        if (!string.IsNullOrEmpty(shot))
        {
            WindowBackdropType = WindowBackdropType.None;
            if (shot.Contains("checked"))
                Check1.IsChecked = Check2.IsChecked = Check3.IsChecked = Check4.IsChecked = true;
            else if (shot.Contains("partial")) // 3 of 4 ticked - the continue button must stay disabled
                Check1.IsChecked = Check2.IsChecked = Check3.IsChecked = true;
        }
        DialogScreenshot.AttachIfRequested(this);
    }

    private void OnCheckChanged(object sender, RoutedEventArgs e)
        => ContinueButton.IsEnabled = Check1.IsChecked == true && Check2.IsChecked == true
                                      && Check3.IsChecked == true && Check4.IsChecked == true;

    private void OnContinue(object sender, RoutedEventArgs e)
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
