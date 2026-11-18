using System.Diagnostics;
using System.Drawing;
using Axe.Windows.Automation;
using Axe.Windows.Automation.Data;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Patterns;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// Checks the app's window against the accessibility rules of Axe.Windows, the engine of
/// Accessibility Insights for Windows: that what a screen reader lands on has a name, a control
/// type that fits and the patterns the type promises, that names are not repeated among
/// neighbours, and more. A scan reads every element of the window through UI Automation, across
/// to the app's process, and so takes a second or more.
/// </summary>
/// <remarks>
/// The first scan makes the test process DPI aware, as UI Automation asks of its clients and the
/// library does unless told otherwise. On a display at 100 %, which the build machines have,
/// nothing changes by that.
/// </remarks>
internal static class AccessibilityScan
{
    private const string RulesPage = "https://github.com/microsoft/axe-windows/blob/main/docs/RulesDescription.md";

    // More than any page or dialog of the app has.
    private const int MostScreenfuls = 20;

    /// <summary>
    /// Scans the window as it is and returns what breaks a rule in <paramref name="part"/> of it.
    /// The scan leaves <paramref name="name"/>.a11ytest in the artifacts folder for Accessibility
    /// Insights for Windows to open: every element, what was found on it, and a picture of the window.
    /// </summary>
    public static List<AccessibilityViolation> Scan(this AppSession app, WindowPart part, string name)
    {
        // Wherever the last click left the pointer, a tool tip may be showing, and would be scanned
        // with the window. Over the strip that moves the window there is none.
        Rectangle window = app.Window.BoundingRectangle;
        Mouse.Position = new Point(window.Left + window.Width / 2, window.Top + 12);
        Thread.Sleep(300); // what has just opened or scrolled is still moving into place

        Config config = Config.Builder.ForProcessId(app.Window.Properties.ProcessId.Value)
            .WithOutputFileFormat(OutputFileFormat.A11yTest)
            .WithOutputDirectory(app.ArtifactsDir)
            .WithAlwaysSaveTestFile()
            .Build();
        var clock = Stopwatch.StartNew();
        ScanOutput output = ScannerFactory.CreateScanner(config).Scan(new ScanOptions(scanId: name));
        if (output.WindowScanOutputs.Count == 0) throw new InvalidOperationException("Axe.Windows found no window of the app to scan.");

        // An element that went away while the scan was reading it has no properties left, and to
        // the rules it looks as if it had neither a name nor a place. That says nothing about it.
        List<AccessibilityViolation> inTheWindow = output.WindowScanOutputs
            .SelectMany(scanned => scanned.Errors)
            .Where(error => error.Element.Properties is { Count: > 0 })
            .Select(error => new AccessibilityViolation(name, error))
            .ToList();
        List<AccessibilityViolation> found = inTheWindow.Where(violation => violation.Part == part).ToList();
        int accepted = found.RemoveAll(violation => AcceptedViolations.All.Any(entry => entry.Covers(violation)));

        app.Log($"Accessibility scan {name}: {found.Count} violation(s) in the {part.ToString().ToLowerInvariant()} " +
                $"and {accepted} accepted, of {inTheWindow.Count} in the whole window, in {clock.ElapsedMilliseconds} ms");
        if (found.Count > 0) app.Snapshot(name);
        return found;
    }

    /// <summary>
    /// Scans the window once for every screenful of what the element with the automation id
    /// <paramref name="scrollerId"/> scrolls, from its top to its end. WinUI gives an element that
    /// is scrolled out of view no place on the screen, and most rules pass over an element
    /// without one: a single scan would only judge what shows first.
    /// </summary>
    public static List<AccessibilityViolation> ScanPageByPage(this AppSession app, WindowPart part, string name, string scrollerId)
    {
        IScrollPattern scroll = app.Require(scrollerId).Patterns.Scroll.Pattern;
        if (scroll.VerticallyScrollable.Value) scroll.SetScrollPercent(ScrollPatternConstants.NoScroll, 0);

        var found = new Dictionary<string, AccessibilityViolation>();
        for (int screenful = 1; ; screenful++)
        {
            WaitUntilStill(scroll);

            // What is cut off at the edge of one screenful is found again, whole, in the next.
            foreach (AccessibilityViolation violation in app.Scan(part, $"{name}-{screenful}")) found.TryAdd(violation.Key, violation);

            if (!scroll.VerticallyScrollable.Value || scroll.VerticalScrollPercent.Value >= 99.9) return found.Values.ToList();
            if (screenful == MostScreenfuls) throw new InvalidOperationException($"'{scrollerId}' has not come to its end after {MostScreenfuls} screenfuls.");
            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
        }
    }

    /// <summary>
    /// Fails the test if a scan of <paramref name="what"/> found anything. What it then says is a
    /// line for each violation, with the scan it is from, the rule, the element and where it is,
    /// and after those each rule in words.
    /// </summary>
    public static void AssertNothingFound(string what, IReadOnlyCollection<AccessibilityViolation> found) =>
        Assert.True(found.Count == 0, Report(what, found));

    // A scroll viewer may glide to where it was told to go. A scan made on the way would pass
    // over what had not come into view yet when the scan read it.
    private static void WaitUntilStill(IScrollPattern scroll)
    {
        for (int look = 0; look < 30; look++)
        {
            double before = scroll.VerticalScrollPercent.Value;
            Thread.Sleep(100);
            if (scroll.VerticalScrollPercent.Value.Equals(before)) return;
        }
    }

    private static string Report(string what, IReadOnlyCollection<AccessibilityViolation> found)
    {
        var lines = new List<string> { $"{what} breaks accessibility rules in {found.Count} place(s):" };
        lines.AddRange(found.Select(violation => violation.ToString()));
        lines.AddRange(found.DistinctBy(violation => violation.Rule).Select(Explain));
        lines.Add($"Each scan left a file of its name ending in .a11ytest in the artifacts, for Accessibility Insights for Windows. The rules: {RulesPage}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Explain(AccessibilityViolation violation)
    {
        string knownIssue = violation.FrameworkIssue is { } link ? $" A known shortcoming of the UI framework: {link}" : "";
        return $"{violation.Rule}: {violation.Description} How to fix: {violation.HowToFix.ReplaceLineEndings(" ")}{knownIssue}";
    }
}
