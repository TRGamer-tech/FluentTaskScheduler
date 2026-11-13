using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace FluentTaskScheduler.UITests;

public static class AutomationExtensions
{
    // Reading a property of an element that has just gone away throws, and not always the same
    // exception: a window of the shell that closes answers with a COM error of its own.

    /// <summary>The element's name, or an empty one when it has none or has just gone away.</summary>
    public static string NameOrEmpty(this AutomationElement element)
    {
        try { return element.Name ?? ""; }
        catch { return ""; }
    }

    /// <summary>The class name of the element's window, or an empty one when it has none or has just gone away.</summary>
    public static string ClassNameOrEmpty(this AutomationElement element)
    {
        try { return element.Properties.ClassName.ValueOrDefault ?? ""; }
        catch { return ""; }
    }

    /// <summary>Whether the element is of the given type; false when it has just gone away.</summary>
    public static bool Is(this AutomationElement element, ControlType type)
    {
        try { return element.ControlType == type; }
        catch { return false; }
    }

    /// <summary>
    /// Selects a tab or list item the way a screen reader does, which works wherever the mouse
    /// pointer is. An element that cannot be selected through UI Automation is clicked.
    /// </summary>
    public static void Select(this AutomationElement element)
    {
        if (element.Patterns.SelectionItem.IsSupported) element.Patterns.SelectionItem.Pattern.Select();
        else element.Click();
    }

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
        element.WaitForTheForeground();
        element.Focus();
        Keyboard.Type(VirtualKeyShort.ENTER);
    }

    /// <summary>Types <paramref name="text"/> into a text box, in place of what it holds.</summary>
    public static void TypeText(this AutomationElement box, string text)
    {
        box.WaitForTheForeground();
        box.AsTextBox().Enter(text);
    }

    /// <summary>
    /// Waits until a window of the element's program is in the foreground. Keys go to whichever
    /// window is, whatever program it belongs to, and on a build machine a window of another
    /// program can come up at any time.
    /// </summary>
    public static void WaitForTheForeground(this AutomationElement element)
    {
        int program = element.Properties.ProcessId.Value;
        if (Retry.WhileFalse(() => TopLevelWindows.ProcessOf(TopLevelWindows.Foreground) == program,
                TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250)).Success) return;

        nint foreground = TopLevelWindows.Foreground;
        throw new InvalidOperationException(
            $"\"{TopLevelWindows.TitleOf(foreground)}\" ({TopLevelWindows.ClassOf(foreground)}) of process " +
            $"{TopLevelWindows.ProcessOf(foreground)} keeps the foreground, so keys would not reach the app.");
    }
}
