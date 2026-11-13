using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Controls;

/// <summary>
/// A settings row that is a button as a whole, for a setting that leads somewhere: to a dialog, a
/// file, a web page. A mark at its right edge says which.
/// </summary>
public sealed partial class SettingsLink : Button
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingsLink), new PropertyMetadata(""));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsLink),
            new PropertyMetadata("", (link, change) => AutomationProperties.SetName(link, (string)change.NewValue)));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsLink), new PropertyMetadata(""));

    public static readonly DependencyProperty ActionGlyphProperty =
        DependencyProperty.Register(nameof(ActionGlyph), typeof(string), typeof(SettingsLink), new PropertyMetadata("\uE76C"));

    public SettingsLink() => InitializeComponent();

    /// <summary>The picture: a character of the Windows icon font.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// The mark at the right edge, a character of the Windows icon font. Unless set it is an
    /// arrow, for a row that opens something inside the app.
    /// </summary>
    public string ActionGlyph
    {
        get => (string)GetValue(ActionGlyphProperty);
        set => SetValue(ActionGlyphProperty, value);
    }
}
