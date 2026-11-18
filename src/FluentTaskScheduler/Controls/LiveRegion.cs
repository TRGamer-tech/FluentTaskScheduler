using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Controls;

/// <summary>
/// Has a screen reader read out a text whenever it changes: <c>controls:LiveRegion.IsLive="True"</c>
/// on a text block. It is for a text that says how something the user started is going or how it
/// ended. The focus is on the button then, not on the text, and without this the change goes
/// unheard.
/// </summary>
public static class LiveRegion
{
    public static readonly DependencyProperty IsLiveProperty = DependencyProperty.RegisterAttached(
        "IsLive", typeof(bool), typeof(LiveRegion), new PropertyMetadata(false, IsLiveChanged));

    // What following the text of a live text block returned, to stop following it with.
    private static readonly DependencyProperty FollowingProperty = DependencyProperty.RegisterAttached(
        "Following", typeof(long), typeof(LiveRegion), new PropertyMetadata(0L));

    public static bool GetIsLive(TextBlock text) => (bool)text.GetValue(IsLiveProperty);

    public static void SetIsLive(TextBlock text, bool value) => text.SetValue(IsLiveProperty, value);

    private static void IsLiveChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var text = (TextBlock)sender;
        if ((bool)e.NewValue)
        {
            // Polite: the new text waits for the screen reader to finish what it is saying.
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
            text.SetValue(FollowingProperty, text.RegisterPropertyChangedCallback(TextBlock.TextProperty, TextChanged));
        }
        else
        {
            AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Off);
            text.UnregisterPropertyChangedCallback(TextBlock.TextProperty, (long)text.GetValue(FollowingProperty));
        }
    }

    private static void TextChanged(DependencyObject sender, DependencyProperty property)
    {
        var text = (TextBlock)sender;

        // The text a page starts out with is not news, and neither is a text that went away.
        if (!text.IsLoaded || text.Text.Length == 0) return;

        FrameworkElementAutomationPeer.CreatePeerForElement(text)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
