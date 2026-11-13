using System.Globalization;
using FluentTaskScheduler.Scheduling;
using DaysOfTheWeek = Microsoft.Win32.TaskScheduler.DaysOfTheWeek;
using TaskSessionStateChangeType = Microsoft.Win32.TaskScheduler.TaskSessionStateChangeType;
using WhichWeek = Microsoft.Win32.TaskScheduler.WhichWeek;

namespace FluentTaskScheduler.Tests.Scheduling;

/// <summary>Without resources a text shows its resource names, which is what these tests read.</summary>
public sealed class TriggerTextTests : IDisposable
{
    private static readonly DateTime Start = new(2031, 5, 6, 7, 8, 0);
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public TriggerTextTests() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    public static TheoryData<TriggerDraft, string> Described => new()
    {
        { new TriggerDraft { Type = TriggerType.Once, Start = Start }, "Dialog.Trigger.Once: 05/06/2031 07:08" },
        { new TriggerDraft { Type = TriggerType.Daily, Start = Start }, "Dialog.Trigger.Daily Trigger.At 07:08" },
        { new TriggerDraft { Type = TriggerType.Daily, Start = Start, DaysInterval = 3 }, "Dialog.Trigger.Daily (Editor.Trigger.EveryNDays) Trigger.At 07:08" },
        { new TriggerDraft { Type = TriggerType.Weekly, Start = Start, DaysOfWeek = DaysOfTheWeek.Sunday | DaysOfTheWeek.Tuesday }, "Dialog.Trigger.Weekly (Dialog.Day.Tue, Dialog.Day.Sun) Trigger.At 07:08" },
        { new TriggerDraft { Type = TriggerType.Monthly, Start = Start, DaysOfMonth = 0b100000000000001, LastDayOfMonth = true }, "Dialog.Trigger.Monthly (1, 15, Dialog.Week.Last) Trigger.At 07:08" },
        { new TriggerDraft { Type = TriggerType.Monthly, Start = Start, MonthlyByWeekday = true, Weeks = WhichWeek.FirstWeek | WhichWeek.LastWeek, DaysOfWeek = DaysOfTheWeek.Monday }, "Dialog.Trigger.Monthly (Dialog.Week.First, Dialog.Week.Last: Dialog.Day.Mon) Trigger.At 07:08" },
        { new TriggerDraft { Type = TriggerType.AtLogon }, "Trigger.AtLogon" },
        { new TriggerDraft { Type = TriggerType.AtStartup }, "Trigger.AtStartup" },
        { new TriggerDraft { Type = TriggerType.OnIdle }, "Trigger.OnIdle" },
        { new TriggerDraft { Type = TriggerType.Event, EventSubscription = EventQuery.Build("Application", "VSS", 8224) }, "Dialog.Trigger.Event (Application, VSS, 8224)" },
        { new TriggerDraft { Type = TriggerType.Event, EventSubscription = EventQuery.Build("System", "", null) }, "Dialog.Trigger.Event (System)" },
        { new TriggerDraft { Type = TriggerType.Event, EventSubscription = "<QueryList/>" }, "Dialog.Trigger.Event" },
        { new TriggerDraft { Type = TriggerType.SessionStateChange, SessionChange = TaskSessionStateChangeType.RemoteConnect }, "Trigger.SessionStateChange" },
        { new TriggerDraft { Type = TriggerType.Unsupported, UnsupportedSummary = "Registration" }, "Dialog.Trigger.__Unsupported (Registration)" }
    };

    [Theory]
    [MemberData(nameof(Described))]
    public void ATrigger_IsDescribedByItsTypeAndWhatMattersForIt(TriggerDraft trigger, string expected)
    {
        Assert.Equal(expected, TriggerText.Describe(trigger));
    }

    [Fact]
    public void EverySessionChange_HasAName()
    {
        foreach (TaskSessionStateChangeType change in Enum.GetValues<TaskSessionStateChangeType>())
            Assert.StartsWith("Dialog.Sess.", TriggerText.SessionChangeKey(change));
    }
}
