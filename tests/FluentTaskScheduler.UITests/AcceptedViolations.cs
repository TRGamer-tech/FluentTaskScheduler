using Axe.Windows.Core.Enums;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// One rule the accessibility scans accept as broken inside one control of WinUI, because the
/// app does not make that part of the control and so cannot put it right.
/// </summary>
/// <param name="Rule">The rule of Axe.Windows.</param>
/// <param name="ClassName">
/// The UI Automation class name of the control, as a failing scan prints it in brackets: the
/// violation is accepted on an element of that class and on everything inside one.
/// </param>
/// <param name="Reason">Why the app cannot fix it, with the place it is reported at, if it is.</param>
internal sealed record AcceptedViolation(RuleId Rule, string ClassName, string Reason)
{
    public bool Covers(AccessibilityViolation violation) => violation.Rule == Rule && violation.ClassNames.Contains(ClassName);
}

internal static class AcceptedViolations
{
    /// <summary>
    /// Empty until a run on Windows has shown what WinUI's own controls break. An entry is for one
    /// rule in one control; a rule is never accepted for the whole app, where it would also hide
    /// what the app itself gets wrong.
    /// </summary>
    public static readonly IReadOnlyList<AcceptedViolation> All = [];
}
