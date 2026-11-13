using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.Tests.Snoozing;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.ViewModels;

/// <summary>
/// The snooze of all tasks as a window shows it, and the dialog for a snooze until a moment of
/// the user's choosing. The texts that fill in names and numbers are installed in <see cref="Loc"/>,
/// so these tests run apart from the others; every other text is its key.
/// </summary>
[Collection(LocalizedTextCollection.Name)]
public sealed class SnoozeViewModelTests : IDisposable
{
    /// <summary>Local time is UTC unless a test sets a time zone.</summary>
    private static readonly DateTimeOffset Now = new(2031, 5, 6, 14, 17, 0, TimeSpan.Zero);

    private readonly TempFolder _folder = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeSnoozeService _service = new();
    private readonly ScriptedPrompts _prompts = new();
    private readonly SettingsStore _settings;
    private readonly SnoozeViewModel _snooze;

    public SnoozeViewModelTests()
    {
        Loc.Use(key => key switch
        {
            "Snooze.Error.NotRecorded" => "Not saved. {0}",
            "Snooze.Restore.MessageFormat" => "Still disabled: {0}",
            "Snooze.Restore.MoreFormat" => "{0} and {1} more",
            "Snooze.Unreadable.MessageFormat" => "Kept as {0}",
            _ => null
        });
        _settings = new SettingsStore(_folder.Paths, _time);
        _snooze = new SnoozeViewModel(_service, _settings, _prompts, new InlineUiThread(), _time);
    }

    public void Dispose()
    {
        _snooze.Dispose();
        _settings.Dispose();
        _folder.Dispose();
        Loc.Use(static _ => null);
    }

    private static SnoozeState AllSnoozed(DateTimeOffset? until = null, bool suspendsTriggers = false) => new()
    {
        Global = new GlobalSnooze(new SnoozeWindow(until), suspendsTriggers, [])
    };

    [Fact]
    public void WhileNothingIsSnoozed_TheButtonOffersToSnoozeAllTasks()
    {
        Assert.False(_snooze.IsSnoozed);
        Assert.Equal("", _snooze.StatusText);
        Assert.Equal("Snooze.Toolbar.Idle", _snooze.Caption);
    }

    [Fact]
    public void ASnoozeOfAllTasks_SaysUntilWhen_AndThatWindowsStillStartsTasksByTheirTriggers()
    {
        _service.Publish(AllSnoozed(until: Now.AddMinutes(30)));

        Assert.True(_snooze.IsSnoozed);
        Assert.Equal("Snooze.Status.EndsAt", _snooze.StatusText);
        Assert.Equal("Snooze.Banner.ManualOnly", _snooze.EffectText);
        Assert.Equal("Snooze.Toolbar.Active", _snooze.Caption);
    }

    [Fact]
    public void ASnoozeThatSuspendsTheTriggers_SaysThatItStopsThemToo()
    {
        _service.Publish(AllSnoozed(suspendsTriggers: true));

        Assert.Equal("Snooze.Status.UntilReboot", _snooze.StatusText);
        Assert.Equal("Snooze.Banner.Suspended", _snooze.EffectText);
    }

    [Fact]
    public void WhenTheSnoozeEnds_TheViewSaysSo()
    {
        _service.Publish(AllSnoozed());
        var changed = new List<string?>();
        _snooze.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _service.Publish(new SnoozeState());

        Assert.False(_snooze.IsSnoozed);
        Assert.Equal("Snooze.Toolbar.Idle", _snooze.Caption);
        Assert.Contains(changed, name => string.IsNullOrEmpty(name) || name == nameof(SnoozeViewModel.IsSnoozed));
    }

    [Fact]
    public async Task EachChoice_SnoozesAllTasksForItsTime()
    {
        foreach (SnoozeChoice choice in SnoozeChoice.All) await _snooze.SnoozeCommand.ExecuteAsync(choice);

        Assert.Equal(["SnoozeAll for 00:30:00", "SnoozeAll for 01:00:00", "SnoozeAll for 03:00:00", "SnoozeAll until restart"], _service.Calls);
        Assert.Empty(_prompts.Shown);
    }

    [Fact]
    public async Task ASnoozeThatCannotBeSaved_IsReported_WithTheReason()
    {
        _service.Fails = new IOException("There is not enough space on the disk.");

        await _snooze.SnoozeCommand.ExecuteAsync(SnoozeChoice.OneHour);

        Assert.Equal(["Error: Not saved. There is not enough space on the disk."], _prompts.Shown);
    }

    [Fact]
    public async Task Resuming_EndsTheSnoozeOfAllTasks()
    {
        await _snooze.ResumeCommand.ExecuteAsync(null);

        Assert.Equal(["ResumeAll"], _service.Calls);
    }

    [Fact]
    public void WhileTasksAreBeingDisabledOrEnabled_TheSnoozeIsBusy()
    {
        _service.Publish(AllSnoozed(suspendsTriggers: true) with { IsBusy = true });
        Assert.True(_snooze.IsBusy);

        _service.Publish(AllSnoozed(suspendsTriggers: true));
        Assert.False(_snooze.IsBusy);
    }

    [Fact]
    public async Task TasksASnoozeCouldNotEnableAgain_AreNamed_AndTriedAgainOnRequest()
    {
        Assert.False(_snooze.HasPendingRestores);

        _service.Publish(new SnoozeState { PendingRestores = [new PendingRestore(@"\Backup", RestoreFailure.Error, "The task image is corrupt.")] });
        await _snooze.RetryRestoresCommand.ExecuteAsync(null);

        Assert.True(_snooze.HasPendingRestores);
        Assert.Equal(@"Still disabled: \Backup", _snooze.PendingRestoresText);
        Assert.Equal(["RetryRestores"], _service.Calls);
    }

    [Fact]
    public void ManyTasksLeftDisabled_AreCountedBeyondTheFirstFew_AndAnyRefusalOfWindowsIsExplained()
    {
        ValueList<PendingRestore> restores = Enumerable.Range(1, SnoozeViewModel.NamedRestores + 3)
            .Select(i => new PendingRestore($@"\Task{i}", i == 7 ? RestoreFailure.AccessDenied : RestoreFailure.Error, "Access is denied."))
            .ToValueList();

        _service.Publish(new SnoozeState { PendingRestores = restores });

        Assert.Equal(@"Still disabled: \Task1, \Task2, \Task3, \Task4, \Task5 and 3 more" + Environment.NewLine + "Snooze.Restore.AccessDenied",
            _snooze.PendingRestoresText);
    }

    [Fact]
    public void ARecordOfEarlierSnoozesThatCouldNotBeRead_IsReported_WithWhereItWasKept()
    {
        Assert.False(_snooze.HasUnreadableStateFile);

        _service.Publish(new SnoozeState { UnreadableStateFile = @"C:\Data\snooze.unreadable-20310506-141700.json" });

        Assert.True(_snooze.HasUnreadableStateFile);
        Assert.Equal(@"Kept as C:\Data\snooze.unreadable-20310506-141700.json", _snooze.UnreadableStateText);
    }

    [Fact]
    public void TheToolbarsOptionToSuspendTriggers_IsTheSetting()
    {
        _snooze.SuspendsTriggers = true;

        Assert.True(_settings.Current.SnoozeSuspendsScheduledTasks);
    }

    [Fact]
    public void NoLongerSuspendingTriggers_LeavesTheTasksOfWindowsOutOfTheNextSuspension()
    {
        _settings.Update(s => s with { SnoozeSuspendsScheduledTasks = true, SnoozeIncludeMicrosoftTasks = true });

        _snooze.SuspendsTriggers = false;
        _snooze.SuspendsTriggers = true;

        Assert.False(_settings.Current.SnoozeIncludeMicrosoftTasks);
    }

    [Fact]
    public void OptionsChangedElsewhere_ShowHereToo()
    {
        var changed = new List<string?>();
        _snooze.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _settings.Update(s => s with { SnoozeSuspendsScheduledTasks = true });

        Assert.True(_snooze.SuspendsTriggers);
        Assert.Contains(nameof(SnoozeViewModel.SuspendsTriggers), changed);
    }

    [Fact]
    public void AChange_ReachesTheView_OnTheUiThread()
    {
        var ui = new BusyUiThread();
        using var snooze = new SnoozeViewModel(_service, _settings, _prompts, ui, _time);

        _service.Publish(AllSnoozed());
        Assert.False(snooze.IsSnoozed);

        ui.RunPosted();
        Assert.True(snooze.IsSnoozed);
    }

    [Fact]
    public void AClosedWindow_NoLongerFollowsTheSnoozeOrTheSettings_EvenWithChangesPostedBeforeItClosed()
    {
        int listening = _service.Subscribers;
        var ui = new BusyUiThread();
        var snooze = new SnoozeViewModel(_service, _settings, _prompts, ui, _time);
        int changes = 0;
        snooze.PropertyChanged += (_, _) => changes++;

        _service.Publish(AllSnoozed());
        _settings.Update(s => s with { SnoozeSuspendsScheduledTasks = true });
        snooze.Dispose();
        ui.RunPosted();
        _settings.Update(s => s with { SnoozeSuspendsScheduledTasks = false });

        Assert.False(snooze.IsSnoozed);
        Assert.Equal(0, changes);
        Assert.Equal(0, ui.PostedCount);
        Assert.Equal(listening, _service.Subscribers);
    }

    [Fact]
    public void TheDialog_StartsAnHourFromNow_WithTheOptionsAsTheyAreSet()
    {
        _settings.Update(s => s with { SnoozeSuspendsScheduledTasks = true, SnoozeIncludeMicrosoftTasks = true });

        SnoozeUntilViewModel dialog = _snooze.PickMoment();

        Assert.Equal(new DateTime(2031, 5, 6), dialog.Date.Date);
        Assert.Equal(new TimeSpan(15, 17, 0), dialog.TimeOfDay);
        Assert.Equal("Snooze.Dialog.Title", dialog.Title);
        Assert.Equal("Snooze.Dialog.Until", dialog.UntilText);
        Assert.True(dialog.IsForAllTasks);
        Assert.True(dialog.SuspendsTriggers);
        Assert.True(dialog.IncludesMicrosoftTasks);
        Assert.True(dialog.ShowsMicrosoftTasksWarning);
    }

    [Fact]
    public void InTheDialog_NoLongerSuspendingTriggers_UnticksTheTasksOfWindows_AndTakesTheWarningAway()
    {
        SnoozeUntilViewModel dialog = _snooze.PickMoment();
        dialog.SuspendsTriggers = true;
        dialog.IncludesMicrosoftTasks = true;
        Assert.True(dialog.ShowsMicrosoftTasksWarning);

        dialog.SuspendsTriggers = false;

        Assert.False(dialog.IncludesMicrosoftTasks);
        Assert.False(dialog.ShowsMicrosoftTasksWarning);
    }

    [Fact]
    public void OptionsChosenInADialogThatIsNotConfirmed_AreNotSaved()
    {
        SnoozeUntilViewModel dialog = _snooze.PickMoment();

        dialog.SuspendsTriggers = true;
        dialog.IncludesMicrosoftTasks = true;

        Assert.False(_settings.Current.SnoozeSuspendsScheduledTasks);
        Assert.False(_settings.Current.SnoozeIncludeMicrosoftTasks);
        Assert.False(_snooze.SuspendsTriggers);
    }

    [Fact]
    public async Task AConfirmedDialog_SavesItsOptions_BeforeTheSnoozeStarts()
    {
        SnoozeUntilViewModel dialog = _snooze.PickMoment();
        dialog.SuspendsTriggers = true;
        dialog.IncludesMicrosoftTasks = true;
        int requestsWhenSaved = -1;
        _settings.Changed += (_, _) => requestsWhenSaved = _service.Calls.Count;

        Assert.True(await dialog.SnoozeAsync());

        Assert.Equal(0, requestsWhenSaved);
        Assert.Single(_service.Calls);
        Assert.True(_settings.Current.SnoozeSuspendsScheduledTasks);
        Assert.True(_settings.Current.SnoozeIncludeMicrosoftTasks);
    }

    [Fact]
    public void LateInTheEvening_TheDialogStartsOnTheNextDay()
    {
        // 14:17 UTC is 23:17 here.
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(9), "Test", "Test"));

        SnoozeUntilViewModel dialog = _snooze.PickMoment();

        Assert.Equal(new DateTime(2031, 5, 7), dialog.Date.Date);
        Assert.Equal(new TimeSpan(0, 17, 0), dialog.TimeOfDay);
    }

    [Fact]
    public async Task AMomentThatHasPassed_IsRefused_AndNeitherSnoozesNorSavesTheOptions()
    {
        SnoozeUntilViewModel dialog = _snooze.PickMoment();
        dialog.TimeOfDay = new TimeSpan(14, 17, 0);
        dialog.SuspendsTriggers = true;

        Assert.False(await dialog.SnoozeAsync());

        Assert.Equal("Snooze.Error.PastTime", dialog.Problem);
        Assert.True(dialog.HasProblem);
        Assert.Empty(_service.Calls);
        Assert.False(_settings.Current.SnoozeSuspendsScheduledTasks);
    }

    [Fact]
    public async Task AMomentAhead_SnoozesAllTasksUntilThen()
    {
        SnoozeUntilViewModel dialog = _snooze.PickMoment();
        dialog.Date = new DateTimeOffset(2031, 5, 8, 0, 0, 0, TimeSpan.Zero);
        dialog.TimeOfDay = new TimeSpan(9, 30, 0);

        Assert.True(await dialog.SnoozeAsync());

        Assert.Equal(["SnoozeAll until 2031-05-08 09:30 +00:00"], _service.Calls);
        Assert.False(dialog.HasProblem);
    }

    [Fact]
    public async Task TheMoment_IsOneInTheTimeOfThisComputer()
    {
        // 14:17 UTC is 16:17 here.
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(2), "Test", "Test"));
        SnoozeUntilViewModel dialog = _snooze.PickMoment();
        dialog.TimeOfDay = new TimeSpan(18, 0, 0);

        Assert.True(await dialog.SnoozeAsync());

        Assert.Equal(["SnoozeAll until 2031-05-06 18:00 +02:00"], _service.Calls);
    }

    [Fact]
    public async Task ASnoozeThatCannotBeSaved_KeepsTheDialogOpen_AndSaysWhyThere()
    {
        _service.Fails = new UnauthorizedAccessException("Access to the path is denied.");
        SnoozeUntilViewModel dialog = _snooze.PickMoment();

        Assert.False(await dialog.SnoozeAsync());

        Assert.Equal("Not saved. Access to the path is denied.", dialog.Problem);
        Assert.Empty(_prompts.Shown);
        Assert.True(dialog.CanSnooze);
    }

    [Fact]
    public async Task WhileTheSnoozeIsBeingStarted_TheDialogShowsIt_AndCannotBeConfirmedAgain()
    {
        _service.Hold = new TaskCompletionSource();
        SnoozeUntilViewModel dialog = _snooze.PickMoment();
        Assert.True(dialog.CanSnooze);

        Task<bool> snoozing = dialog.SnoozeAsync();

        Assert.True(dialog.IsSnoozing);
        Assert.False(dialog.CanSnooze);

        _service.Hold.SetResult();

        Assert.True(await snoozing);
        Assert.False(dialog.IsSnoozing);
    }

    [Fact]
    public async Task AnotherAttempt_TakesTheProblemOfTheLastOneAway()
    {
        SnoozeUntilViewModel dialog = _snooze.PickMoment();
        dialog.TimeOfDay = new TimeSpan(14, 17, 0);
        Assert.False(await dialog.SnoozeAsync());

        _service.Hold = new TaskCompletionSource();
        dialog.TimeOfDay = new TimeSpan(15, 0, 0);
        Task<bool> snoozing = dialog.SnoozeAsync();

        Assert.False(dialog.HasProblem);
        _service.Hold.SetResult();
        Assert.True(await snoozing);
    }
}
