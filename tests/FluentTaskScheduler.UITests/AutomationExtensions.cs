using FlaUI.Core.AutomationElements;

namespace FluentTaskScheduler.UITests;

public static class AutomationExtensions
{
    /// <summary>
    /// Activates a button, menu item or navigation item the way a screen reader does, which works
    /// wherever the mouse pointer is. Use a real click only where the click itself is what is
    /// tested, or where the element opens a system dialog that holds its caller until it closes.
    /// </summary>
    public static void Press(this AutomationElement element)
    {
        if (element.Patterns.Invoke.IsSupported) element.Patterns.Invoke.Pattern.Invoke();
        else if (element.Patterns.SelectionItem.IsSupported) element.Patterns.SelectionItem.Pattern.Select();
        else element.Click();
    }
}
