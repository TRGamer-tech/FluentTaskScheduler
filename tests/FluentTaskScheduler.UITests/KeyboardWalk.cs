using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace FluentTaskScheduler.UITests;

/// <summary>One element the focus stopped at on its way through the window.</summary>
/// <param name="AutomationId">The id of the element that had the focus.</param>
/// <param name="ContainerId">The id of what the element is part of: of the list for a row, of a search box for the text box in it.</param>
/// <param name="Name">What a screen reader announces for it.</param>
/// <param name="Type">Its control type, as UI Automation calls it.</param>
internal sealed record FocusStop(string AutomationId, string ContainerId, string Name, string Type)
{
    public bool IsOrIsIn(string automationId) => AutomationId == automationId || ContainerId == automationId;

    public override string ToString()
    {
        string id = AutomationId.Length > 0 ? " #" + AutomationId : "";
        string container = ContainerId.Length > 0 ? " in #" + ContainerId : "";
        return $"{Type}{id} \"{Name}\"{container}";
    }
}

internal static class KeyboardWalk
{
    // More than a page and the navigation pane have together. A walk that long goes round without passing its start.
    private const int MostStops = 150;

    /// <summary>
    /// Gives the element with the id <paramref name="startId"/> the focus and presses Tab until
    /// the focus is back where it began: through the page and the navigation pane, the way
    /// someone without a mouse goes through them.
    /// </summary>
    /// <param name="backwards">Shift+Tab instead of Tab, for a page with a box that keeps Tab for itself.</param>
    /// <returns>Every stop on the way in the order of the walk, the first included.</returns>
    public static List<FocusStop> TabAround(this AppSession app, string startId, bool backwards = false)
    {
        string key = backwards ? "Shift+Tab" : "Tab";
        app.Window.WaitForTheForeground();
        app.Require(startId).Focus();

        // A box made of parts hands the focus on to one of them, and the walk begins there.
        AutomationElement first = FocusOnceIt(app, focused => Describe(focused).IsOrIsIn(startId))
                                  ?? throw new InvalidOperationException($"'{startId}' did not take the focus.");
        var stops = new List<FocusStop> { Describe(first) };
        AutomationElement current = first;
        while (true)
        {
            // A key can get lost on a build machine, where another program's window may have the
            // keyboard for a moment. So a press that moved nothing is made once more before it counts.
            AutomationElement next = PressAndFollow() ?? PressAndFollow()
                                     ?? throw new InvalidOperationException($"{key} does not move the focus on from {stops[^1]}. The way there: {Listed(stops)}");
            if (Same(next, first)) return stops;
            if (stops.Count == MostStops)
                throw new InvalidOperationException($"The focus did not come back to where it began after {MostStops} presses of {key}: {Listed(stops)}");

            stops.Add(Describe(next));
            current = next;
        }

        AutomationElement? PressAndFollow()
        {
            app.Window.WaitForTheForeground();
            if (backwards) Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.TAB);
            else Keyboard.Type(VirtualKeyShort.TAB);
            return FocusOnceIt(app, focused => !Same(focused, current));
        }
    }

    /// <summary>The stops, one per line, for the message of a failed assertion.</summary>
    public static string Listed(IEnumerable<FocusStop> stops) => Environment.NewLine + string.Join(Environment.NewLine, stops);

    // The element of the app that has the focus, once that is one the caller waits for; null if none comes to have it.
    private static AutomationElement? FocusOnceIt(AppSession app, Func<AutomationElement, bool> isAwaited)
    {
        int program = app.Window.Properties.ProcessId.Value;
        return Retry.WhileNull(() =>
        {
            AutomationElement focused = app.Automation.FocusedElement();
            return focused.Properties.ProcessId.ValueOrDefault == program && isAwaited(focused) ? focused : null;
        }, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(25), ignoreException: true).Result; // on its way from one element to the next, the focus is nowhere
    }

    private static bool Same(AutomationElement one, AutomationElement other)
    {
        try { return one.Equals(other); }
        catch { return false; } // an element that has gone away is no longer any other
    }

    private static FocusStop Describe(AutomationElement element) => new(
        element.Properties.AutomationId.ValueOrDefault ?? "",
        element.Parent?.Properties.AutomationId.ValueOrDefault ?? "",
        element.NameOrEmpty(),
        element.Properties.ControlType.ValueOrDefault.ToString());
}
