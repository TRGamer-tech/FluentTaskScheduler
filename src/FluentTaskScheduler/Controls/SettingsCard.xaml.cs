using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace FluentTaskScheduler.Controls;

/// <summary>
/// One row of a settings page, laid out the way Windows Settings does it: a picture, a heading and
/// an explanation on the left, and on the right the control that changes the setting. That
/// control is the card's content.
/// </summary>
[ContentProperty(Name = nameof(ActionContent))]
public sealed partial class SettingsCard : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard),
            new PropertyMetadata("", (card, _) => ((SettingsCard)card).NameTheControl()));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata(""));

    public static readonly DependencyProperty ActionContentProperty =
        DependencyProperty.Register(nameof(ActionContent), typeof(object), typeof(SettingsCard),
            new PropertyMetadata(null, (card, _) => ((SettingsCard)card).AdoptControl()));

    public static readonly DependencyProperty IsFramedProperty =
        DependencyProperty.Register(nameof(IsFramed), typeof(bool), typeof(SettingsCard),
            new PropertyMetadata(true, (card, _) => ((SettingsCard)card).ShowFrame()));

    private bool _namesTheControl;

    public SettingsCard()
    {
        InitializeComponent();

        // A visual state set before the card is in a window may not take.
        Loaded += (_, _) => ShowFrame();
    }

    /// <summary>The picture: a character of the Windows icon font. May be left empty.</summary>
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

    /// <summary>The control at the right edge.</summary>
    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    /// <summary>
    /// False where something else draws the card around the row: as the header of an expander,
    /// as the content of a button, or as a row inside another card.
    /// </summary>
    public bool IsFramed
    {
        get => (bool)GetValue(IsFramedProperty);
        set => SetValue(IsFramedProperty, value);
    }

    /// <summary>For x:Bind: an explanation that is empty takes no room.</summary>
    public static Visibility VisibilityOf(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    private void ShowFrame() => VisualStateManager.GoToState(this, IsFramed ? "Framed" : "Bare", useTransitions: false);

    private void AdoptControl()
    {
        _namesTheControl = ActionContent is Control control && string.IsNullOrEmpty(AutomationProperties.GetName(control));
        NameTheControl();
        if (ActionContent is not ToggleSwitch toggle) return;

        // A stock switch is as wide as if it had a header and puts its state text after the knob.
        // Windows Settings has the knob at the right edge of the card, with the text before it.
        toggle.MinWidth = 0;
        toggle.Loaded += (_, _) => PutStateTextFirst(toggle);

        // The switch aligns its state text by this. "On" and "Off" differ in width, and the
        // shorter of the two is to stay next to the knob.
        toggle.HorizontalContentAlignment = HorizontalAlignment.Right;
    }

    // A switch or a list in a card has no label of its own. Unless the page gave it a name, it is
    // named after the card, so that a screen reader says what it is for.
    private void NameTheControl()
    {
        if (_namesTheControl) AutomationProperties.SetName((Control)ActionContent!, Header);
    }

    // Swaps the two outer columns of the switch's template, where the knob and the state text live.
    private static void PutStateTextFirst(ToggleSwitch toggle)
    {
        // The parts come into being with the first layout, which a switch that is not showing has not had.
        toggle.ApplyTemplate();

        foreach (FrameworkElement part in PartsOf(toggle))
        {
            if (part.Name is "OffContentPresenter" or "OnContentPresenter") Grid.SetColumn(part, 0);
            else if (part.Name is "OuterBorder" or "SwitchKnobBounds" or "SwitchKnob") Grid.SetColumn(part, 2);
        }
    }

    private static IEnumerable<FrameworkElement> PartsOf(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element) yield return element;
            foreach (FrameworkElement below in PartsOf(child)) yield return below;
        }
    }
}
