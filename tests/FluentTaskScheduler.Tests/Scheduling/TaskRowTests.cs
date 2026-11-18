using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.Tests.Scheduling;

public class TaskRowTests
{
    [Fact]
    public void ARow_IsNamedAndFiledAfterItsPath()
    {
        var nested = new TaskRow(@"\Backups\Nightly");
        var top = new TaskRow(@"\Cleanup");

        Assert.Equal(("Nightly", @"\Backups"), (nested.Name, nested.FolderPath));
        Assert.Equal(("Cleanup", @"\"), (top.Name, top.FolderPath));
    }

    [Fact]
    public void TheSwitch_AsksInsteadOfChangingTheRow()
    {
        var row = new TaskRow(@"\Backup") { IsEnabled = true };
        var requests = new List<(string Path, bool Enabled)>();
        row.EnabledChangeRequested += (asked, enabled) => requests.Add((asked.Path, enabled));

        row.EnabledSwitch = false;

        Assert.Equal([(@"\Backup", false)], requests);
        Assert.True(row.IsEnabled);
    }

    [Fact]
    public void ASwitchNobodyActsOn_GoesBack()
    {
        var row = new TaskRow(@"\Backup") { IsEnabled = true };
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.EnabledSwitch = false;

        Assert.True(row.EnabledSwitch);
        Assert.Equal([nameof(TaskRow.EnabledSwitch)], changed);
    }

    [Fact]
    public void SettingTheSwitchToWhatItShows_AsksForNothing()
    {
        var row = new TaskRow(@"\Backup") { IsEnabled = true };
        int requests = 0;
        row.EnabledChangeRequested += (_, _) => requests++;

        row.EnabledSwitch = true;

        Assert.Equal(0, requests);
    }

    [Fact]
    public void WhenTheTaskChanges_TheSwitchFollows()
    {
        var row = new TaskRow(@"\Backup");
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.IsEnabled = true;

        Assert.True(row.EnabledSwitch);
        Assert.Contains(nameof(TaskRow.EnabledSwitch), changed);
    }
}
