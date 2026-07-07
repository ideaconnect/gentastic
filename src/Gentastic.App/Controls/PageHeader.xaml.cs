using System.Windows;
using System.Windows.Controls;
using FontAwesome.Sharp;

namespace Gentastic.App.Controls;

/// <summary>Consistent page title bar: an icon + title on the left, an optional actions slot on the
/// right (same line), and a horizontal rule underneath. Used at the top of every page.</summary>
public partial class PageHeader : UserControl
{
    public PageHeader() => InitializeComponent();

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(string.Empty));

    /// <summary>The page name shown as the header title.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(IconChar), typeof(PageHeader), new PropertyMetadata(IconChar.None));

    /// <summary>The FontAwesome icon shown before the title.</summary>
    public IconChar Icon
    {
        get => (IconChar)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    /// <summary>Right-aligned content on the title line (buttons, toggles, …).</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
