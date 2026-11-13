using FluentTaskScheduler.Localization;

namespace FluentTaskScheduler.Scheduling;

/// <summary>Says in one line what an action does, for lists of actions.</summary>
public static class ActionText
{
    public static string Describe(ActionDraft action) =>
        action.UnsupportedSummary is { } kind
            ? Loc.Format("Text.DetailFormat", Loc.Get("Action.Unsupported"), kind)
            : $"{action.Path} {action.Arguments}".Trim();
}
