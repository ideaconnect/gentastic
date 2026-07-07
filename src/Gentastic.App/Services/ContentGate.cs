using System.Windows;
using Gentastic.App.Views;

namespace Gentastic.App.Services;

/// <summary>Shows the adult-content confirmation modals: an age gate when the adult (18+) models are
/// switched on, and a per-generation acknowledgement of the legal guardrails before an NSFW model runs.
/// Kept behind an interface so the view models stay free of direct <see cref="Window"/> creation.</summary>
public interface IContentGate
{
    /// <summary>Age-confirmation modal shown when the user turns on the adult models. Returns true only
    /// if the user confirmed they are of legal age.</summary>
    bool ConfirmAdultAge();

    /// <summary>Acknowledgement modal (four checkboxes) shown before each generation with a model that
    /// requires it - adult models and real-face "keep face" / image-edit models. Returns true only if the
    /// user ticked every box and chose to continue.</summary>
    bool ConfirmGenerationAcknowledgement();
}

/// <inheritdoc />
public sealed class ContentGate : IContentGate
{
    // In headless test/screenshot/auto-gen runs there is no user to click, so auto-accept - otherwise the
    // capture and end-to-end generation hooks would block forever on a modal ShowDialog().
    private static bool Headless =>
        Environment.GetEnvironmentVariable("GENTASTIC_AUTOGEN") == "1"
        || Environment.GetEnvironmentVariable("GENTASTIC_SCREENSHOT") == "1";

    public bool ConfirmAdultAge()
        => Headless || ShowModal(new AgeConfirmationDialog());

    public bool ConfirmGenerationAcknowledgement()
        => Headless || ShowModal(new AdultAcknowledgementDialog());

    private static bool ShowModal(Window dialog)
    {
        dialog.Owner = Application.Current?.MainWindow;
        return dialog.ShowDialog() == true;
    }
}
