using System.Collections.ObjectModel;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class CollectionSyncTests
{
    private sealed record Line(string Name, int Runs);

    /// <summary>Each change the collection reports, e.g. "Move d".</summary>
    private static List<string> Changes<T>(ObservableCollection<T> shown)
    {
        var changes = new List<string>();
        shown.CollectionChanged += (_, e) => changes.Add($"{e.Action} {(e.NewItems ?? e.OldItems)![0]}");
        return changes;
    }

    [Fact]
    public void OnlyWhatDiffers_IsRemovedMovedOrInserted()
    {
        var shown = new ObservableCollection<string>(["a", "b", "c", "d"]);
        List<string> changes = Changes(shown);

        shown.SyncWith(["d", "b", "e"]);

        Assert.Equal(["d", "b", "e"], shown);
        Assert.Equal(["Remove c", "Remove a", "Move d", "Add e"], changes);
    }

    [Fact]
    public void AnEntryThatReadsTheSame_StaysTheInstanceThatIsShown_AndOneThatChangedIsReplaced()
    {
        var first = new Line("a", 1);
        var shown = new ObservableCollection<Line>([first, new Line("b", 2)]);
        List<string> changes = Changes(shown);

        shown.SyncWith([new Line("a", 1), new Line("b", 3)]);

        Assert.Same(first, shown[0]);
        Assert.Equal(new Line("b", 3), shown[1]);
        Assert.Equal(["Remove Line { Name = b, Runs = 2 }", "Add Line { Name = b, Runs = 3 }"], changes);
    }

    [Fact]
    public void EqualEntries_AreMatchedInOrder_AndOnesWantedFewerTimesThanShownAreRemoved()
    {
        var shown = new ObservableCollection<string>(["x", "x", "y", "x"]);
        List<string> changes = Changes(shown);

        shown.SyncWith(["y", "x", "x"]);

        Assert.Equal(["y", "x", "x"], shown);
        Assert.Equal(["Move y", "Remove x"], changes);
    }

    [Fact]
    public void ObjectsWithoutValueEquality_AreMatchedByReference()
    {
        TaskRow kept = new(@"\Kept"), shownBefore = new(@"\Backup");
        var shown = new ObservableCollection<TaskRow>([shownBefore, kept]);
        TaskRow readAnew = new(@"\Backup");

        shown.SyncWith([kept, readAnew]);

        Assert.Equal([kept, readAnew], shown);
    }
}
