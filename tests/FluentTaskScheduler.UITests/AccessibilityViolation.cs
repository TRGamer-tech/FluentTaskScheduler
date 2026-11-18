using Axe.Windows.Automation;
using Axe.Windows.Core.Enums;

namespace FluentTaskScheduler.UITests;

/// <summary>Where in the app's window an element is. A scan of one page or dialog is only about its own part.</summary>
internal enum WindowPart
{
    /// <summary>The window itself, its title bar and the navigation pane: the same whatever page is showing.</summary>
    Frame,

    /// <summary>The page next to the navigation pane, with its heading.</summary>
    Page,

    /// <summary>A dialog, a menu or another pop-up over the page.</summary>
    Dialog
}

/// <summary>One element of the app's window that breaks one accessibility rule of Axe.Windows.</summary>
internal sealed class AccessibilityViolation
{
    // An element and what is above it, up to the app's window. Each is described by the UI Automation
    // properties that have a value, under the names Axe.Windows gives them.
    private readonly List<IReadOnlyDictionary<string, string>> _path = [];

    /// <param name="scan">The name of the scan that found it, which is also the name of its .a11ytest file.</param>
    public AccessibilityViolation(string scan, ScanResult found)
    {
        Scan = scan;
        Rule = found.Rule.ID;
        Description = found.Rule.Description;
        HowToFix = found.Rule.HowToFix;
        FrameworkIssue = found.Rule.FrameworkIssueLink;
        for (ElementInfo? element = found.Element; element is not null; element = element.Parent)
            _path.Add(element.Properties ?? new Dictionary<string, string>());
    }

    public string Scan { get; }
    public RuleId Rule { get; }
    public string Description { get; }
    public string HowToFix { get; }

    /// <summary>
    /// The page about the shortcoming of a UI framework that the library knows to cause this
    /// violation, or null. It is the only link the library gives; its rules are described
    /// together in docs/RulesDescription.md of its repository.
    /// </summary>
    public string? FrameworkIssue { get; }

    public string ControlType => TypeOf(_path[0]);
    public string AutomationId => Value(_path[0], "AutomationId");
    public string Name => Value(_path[0], "Name");

    /// <summary>Where the element is on the screen, as "[l=...,t=...,r=...,b=...]"; empty for an element without a place.</summary>
    public string BoundingRectangle => Value(_path[0], "BoundingRectangle");

    /// <summary>The UI Automation class names of the element and of what is above it, the element's first.</summary>
    public IEnumerable<string> ClassNames => _path.Select(element => Value(element, "ClassName"));

    /// <summary>Tells the same finding on the same element apart from any other, also between two scans of the window.</summary>
    public string Key => $"{Rule} {Value(_path[0], "RuntimeId")} {Describe(_path[0])}";

    public WindowPart Part
    {
        get
        {
            // To UI Automation a dialog is a window inside the app's window; so is the navigation pane.
            if (_path.SkipLast(1).Any(element => TypeOf(element) == "Window" && Value(element, "AutomationId") != "PaneRoot")) return WindowPart.Dialog;
            bool inNavigationView = _path.Skip(1).Any(element => Value(element, "AutomationId") == "NavView");
            bool inPane = _path.Any(element => Value(element, "AutomationId") is "PaneRoot" or "TogglePaneButton");
            return inNavigationView && !inPane ? WindowPart.Page : WindowPart.Frame;
        }
    }

    /// <summary>The rule, the element and where it is, in one line.</summary>
    public override string ToString()
    {
        // The nearest element above that can be found again, by its id or by what it is called.
        IReadOnlyDictionary<string, string>? container = _path.Skip(1).FirstOrDefault(element => Value(element, "AutomationId").Length > 0 || Value(element, "Name").Length > 0);
        string place = container is null ? "" : $" in {Describe(container)}";
        return $"{Scan}: {Rule} on {Describe(_path[0])}{place} at {(BoundingRectangle.Length > 0 ? BoundingRectangle : "no place on screen")}";
    }

    private static string Describe(IReadOnlyDictionary<string, string> element)
    {
        string id = Value(element, "AutomationId");
        return $"{TypeOf(element)}{(id.Length > 0 ? " #" + id : "")} \"{Value(element, "Name")}\" ({Value(element, "ClassName")})";
    }

    // Axe.Windows writes a control type with its number, as in "Button(50000)".
    private static string TypeOf(IReadOnlyDictionary<string, string> element) => Value(element, "ControlType").Split('(')[0];

    private static string Value(IReadOnlyDictionary<string, string> element, string property) => element.GetValueOrDefault(property, "");
}
