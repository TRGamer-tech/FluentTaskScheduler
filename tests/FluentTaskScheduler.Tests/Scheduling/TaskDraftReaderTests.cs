using FluentTaskScheduler.Scheduling;
using Microsoft.Win32.TaskScheduler;

namespace FluentTaskScheduler.Tests.Scheduling;

public class TaskDraftReaderTests
{
    /// <summary>The library hands out a start that was written in UTC ("...Z") as a UTC time.</summary>
    [Fact]
    public void AStartWrittenInUtc_IsReadAsTheLocalTimeItStandsFor()
    {
        var utc = new DateTime(2031, 5, 6, 7, 0, 0, DateTimeKind.Utc);

        TriggerDraft draft = TaskDraftReader.Read(new DailyTrigger { StartBoundary = utc }, 0);

        Assert.Equal(utc.ToLocalTime(), draft.Start);
        Assert.Equal(DateTimeKind.Local, draft.Start!.Value.Kind);
    }
}
