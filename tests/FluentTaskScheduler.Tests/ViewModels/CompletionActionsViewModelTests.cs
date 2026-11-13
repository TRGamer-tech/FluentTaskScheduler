using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public class CompletionActionsViewModelTests
{
    private static readonly CompletionActions Stored = new() { Enabled = true, OnSuccess = [@"\Report"], OnFailure = [@"\Alert"] };

    [Fact]
    public void Untouched_ItBuildsWhatWasStored()
    {
        Assert.Equal(Stored, new CompletionActionsViewModel(Stored, []).Build());
    }

    [Fact]
    public void TasksCanBeAddedAndRemoved_ButNotTwice()
    {
        var actions = new CompletionActionsViewModel(Stored, [@"\Zeta", @"\alpha"]);
        Assert.Equal([@"\alpha", @"\Zeta"], actions.OtherTasks);

        actions.AddOnSuccessCommand.Execute(@"\Cleanup");
        actions.AddOnSuccessCommand.Execute(@"\CLEANUP");
        actions.AddOnSuccessCommand.Execute("  ");
        actions.AddOnSuccessCommand.Execute(null);
        actions.RemoveOnFailureCommand.Execute(@"\Alert");

        Assert.Equal(new CompletionActions { Enabled = true, OnSuccess = [@"\Report", @"\Cleanup"] }, actions.Build());
    }

    [Fact]
    public void StartingTheSameTaskAfterSuccessAndFailure_IsPointedOut()
    {
        var actions = new CompletionActionsViewModel(Stored, []);
        var notified = new List<string?>();
        actions.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        Assert.False(actions.StartsSameTaskEitherWay);

        actions.AddOnFailureCommand.Execute(@"\report");

        Assert.True(actions.StartsSameTaskEitherWay);
        Assert.Contains(nameof(CompletionActionsViewModel.StartsSameTaskEitherWay), notified);
    }
}
