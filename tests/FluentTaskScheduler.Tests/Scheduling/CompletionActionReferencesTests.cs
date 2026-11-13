using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.Support;

namespace FluentTaskScheduler.Tests.Scheduling;

public sealed class CompletionActionReferencesTests
{
    private static readonly IReadOnlyList<TaskMove> NightlyRenamed = [new TaskMove(@"\Jobs\Nightly", @"\Jobs\Daily")];

    private readonly FakeTaskStore _store = new();

    private static TaskDraft Starting(ValueList<string> onSuccess, ValueList<string>? onFailure = null) => new()
    {
        Description = "Sends the report",
        Category = "Reports",
        Tags = ["weekly"],
        Actions = [new ActionDraft { SourceIndex = 0, Path = "report.exe" }],
        CompletionActions = new CompletionActions { Enabled = true, OnSuccess = onSuccess, OnFailure = onFailure ?? [] }
    };

    [Fact]
    public void ATaskThatStartsAMovedTask_StartsItWhereItIsNow_AndStaysAsItWasOtherwise()
    {
        TaskDraft report = Starting([@"\Jobs\Nightly", @"\Jobs\Alert"], onFailure: [@"\JOBS\NIGHTLY"]);
        _store.Add(@"\Jobs\Report", draft: report);

        Assert.True(CompletionActionReferences.Follow(_store, @"\Jobs\Report", NightlyRenamed));

        TaskDraft followed = report with
        {
            Name = "Report",
            CompletionActions = report.CompletionActions with { OnSuccess = [@"\Jobs\Daily", @"\Jobs\Alert"], OnFailure = [@"\Jobs\Daily"] }
        };
        Assert.Equal(followed, _store.Load(@"\Jobs\Report"));
        Assert.Equal([@"Save \Jobs\Report"], _store.Calls);
    }

    [Fact]
    public void ATaskThatStartsNoMovedTask_IsNotSaved()
    {
        _store.Add(@"\Jobs\Report", draft: Starting([@"\Jobs\Alert"]));

        Assert.True(CompletionActionReferences.Follow(_store, @"\Jobs\Report", NightlyRenamed));

        Assert.Empty(_store.Calls);
    }

    [Fact]
    public void ATaskWhoseAccountsPasswordWindowsKeeps_IsLeftForTheEditor()
    {
        _store.Add(@"\Jobs\Report", draft: Starting([@"\Jobs\Nightly"]) with { StoresPassword = true });

        Assert.False(CompletionActionReferences.Follow(_store, @"\Jobs\Report", NightlyRenamed));

        Assert.Empty(_store.Calls);
        Assert.Equal([@"\Jobs\Nightly"], _store.GetCompletionActions(@"\Jobs\Report").OnSuccess);
    }

    [Fact]
    public void ATaskWindowsProtects_IsRefused_AndKeepsItsCompletionActions()
    {
        _store.Add(@"\Jobs\Report", draft: Starting([@"\Jobs\Nightly"])).Protected.Add(@"\Jobs\Report");

        Assert.Throws<TaskAccessDeniedException>(() => CompletionActionReferences.Follow(_store, @"\Jobs\Report", NightlyRenamed));

        Assert.Equal([@"\Jobs\Nightly"], _store.GetCompletionActions(@"\Jobs\Report").OnSuccess);
    }

    [Fact]
    public void MovesAreFollowedInTheOrderTheyHappened_WhateverTheCaseOfThePath()
    {
        IReadOnlyList<TaskMove> moves = [new(@"\Jobs\Nightly", @"\Jobs\Daily"), new(@"\Jobs\Daily", @"\Archive\Daily")];

        Assert.Equal(@"\Archive\Daily", CompletionActionReferences.PathAfter(moves, @"\JOBS\Nightly"));
        Assert.Equal(@"\Jobs\Weekly", CompletionActionReferences.PathAfter(moves, @"\Jobs\Weekly"));
    }

    [Fact]
    public void AFolderNameThatOnlyChangedCase_LeavesTheTasksThatStartItsTasksAlone()
    {
        // Task Scheduler renames such a folder by way of a folder with a name of its own.
        IReadOnlyList<TaskMove> moves = [new(@"\jobs\Nightly", @"\Jobs (1a2b3c4d)\Nightly"), new(@"\Jobs (1a2b3c4d)\Nightly", @"\Jobs\Nightly")];
        _store.Add(@"\Report", draft: Starting([@"\jobs\Nightly"]));

        Assert.True(CompletionActionReferences.Follow(_store, @"\Report", moves));

        Assert.Empty(_store.Calls);
    }
}
