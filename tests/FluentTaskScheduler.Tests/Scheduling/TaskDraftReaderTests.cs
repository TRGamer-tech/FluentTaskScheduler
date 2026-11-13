using FluentTaskScheduler.Scheduling;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Tests.Scheduling;

/// <summary>
/// Reads triggers that are not part of a registered task. The library hands their boundaries
/// over as they were set, the kind of time included, which makes them stand in for what it
/// reads from a registered one.
/// </summary>
public class TaskDraftReaderTests
{
    [Fact]
    public void ATriggerSynchronisedAcrossTimeZones_IsReadInLocalTime()
    {
        // What the library makes of a trigger stored from 08:00Z to 08:30Z: the start in UTC, the end in local time.
        var start = new DateTime(2030, 1, 2, 8, 0, 0, DateTimeKind.Utc);
        DateTime end = start.AddMinutes(30).ToLocalTime();

        TriggerDraft draft = TaskDraftReader.Read(new TimeTrigger { StartBoundary = start, EndBoundary = end }, 0);

        Assert.Equal(start.ToLocalTime(), draft.Start);
        Assert.Equal(DateTimeKind.Local, draft.Start?.Kind);
        Assert.Equal(end, draft.End);
        Assert.True(draft.Start < draft.End);
    }

    [Fact]
    public void ATriggerInLocalTime_IsReadAsItIs()
    {
        var start = new DateTime(2030, 1, 2, 8, 0, 0);

        TriggerDraft draft = TaskDraftReader.Read(new DailyTrigger { StartBoundary = start }, 0);

        Assert.Equal(start, draft.Start);
        Assert.Equal(DateTimeKind.Unspecified, draft.Start?.Kind);
        Assert.Null(draft.End);
    }
}
