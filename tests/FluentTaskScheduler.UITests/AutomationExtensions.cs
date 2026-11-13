using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace FluentTaskScheduler.UITests;

public static class AutomationExtensions
{
    /// <summary>
    /// Activates a button, menu item or navigation item the way a screen reader does, which works
    /// wherever the mouse pointer is. Use a real click only where the click itself is what is
    /// tested, and <see cref="PressWithKeyboard"/> where the element opens a system dialog.
    /// </summary>
    public static void Press(this AutomationElement element)
    {
        if (element.Patterns.Invoke.IsSupported) element.Patterns.Invoke.Pattern.Invoke();
        else if (element.Patterns.SelectionItem.IsSupported) element.Patterns.SelectionItem.Pattern.Select();
        else element.Click();
    }

    /// <summary>
    /// Activates a button through the keyboard: focus, then Enter. Unlike <see cref="Press"/>, it
    /// does not wait for what the button does, which matters when that is a system dialog that
    /// holds its caller until it closes; a held-up automation call leaves the app unable to answer
    /// the next one. Unlike a click, it also reaches a button in an overflow menu, where clicks
    /// were lost on the ARM64 build machine.
    /// </summary>
    public static void PressWithKeyboard(this AutomationElement element)
    {
        element.Focus();
        Keyboard.Type(VirtualKeyShort.ENTER);
    }
}
