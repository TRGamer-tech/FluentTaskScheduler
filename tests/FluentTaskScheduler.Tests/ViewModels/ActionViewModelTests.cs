using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public class ActionViewModelTests
{
    [Fact]
    public void TheListShows_WhatWasTyped()
    {
        var action = new ActionViewModel(new ActionDraft { SourceIndex = 0, Path = "old.exe" });
        var changed = new List<string?>();
        action.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        action.Path = "new.exe";
        action.Arguments = "--now";

        Assert.Equal("new.exe --now", action.Summary);
        Assert.Contains(nameof(ActionViewModel.Summary), changed);
    }

    [Fact]
    public void AnActionTheEditorCannotChange_IsListedAsUnsupported_AndBuildsIntoWhatItWas()
    {
        var stored = new ActionDraft { SourceIndex = 1, UnsupportedSummary = "ShowMessage" };
        var action = new ActionViewModel(stored);

        Assert.False(action.IsEditable);
        Assert.Equal("Action.Unsupported (ShowMessage)", action.Summary);
        Assert.Equal(stored, action.Build());
    }
}
