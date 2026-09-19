using System;
using System.Collections.ObjectModel;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Tests
{
    // Covers item 3.4: refreshing the task list must update existing ScheduledTaskModel instances
    // in place (preserving identity for scroll/selection) rather than the diff silently degenerating
    // into remove-everything/insert-everything because LoadTasksAsync always builds new instances.
    public class ScheduledTaskModelUpdateFromTests
    {
        [Fact]
        public void UpdateFrom_CopiesStateAndIsEnabled()
        {
            var existing = new ScheduledTaskModel { Path = @"\A", Name = "A", State = "Disabled", IsEnabled = false };
            var fresh = new ScheduledTaskModel { Path = @"\A", Name = "A", State = "Ready", IsEnabled = true };

            existing.UpdateFrom(fresh);

            Assert.Equal("Ready", existing.State);
            Assert.True(existing.IsEnabled);
        }

        [Fact]
        public void UpdateFrom_PreservesInstanceIdentity()
        {
            var existing = new ScheduledTaskModel { Path = @"\A", Name = "A" };
            var fresh = new ScheduledTaskModel { Path = @"\A", Name = "A", Description = "changed" };

            existing.UpdateFrom(fresh);

            // The point of UpdateFrom is that callers keep using the SAME object (for
            // ObservableCollection identity / scroll position), just with refreshed data.
            Assert.Equal("changed", existing.Description);
        }

        [Fact]
        public void UpdateFrom_DoesNotOverwriteIsSelected()
        {
            var existing = new ScheduledTaskModel { Path = @"\A", Name = "A", IsSelected = true };
            var fresh = new ScheduledTaskModel { Path = @"\A", Name = "A", IsSelected = false };

            existing.UpdateFrom(fresh);

            // IsSelected is UI-only state, not part of the underlying task — a background refresh
            // must not silently deselect a row the user has checked.
            Assert.True(existing.IsSelected);
        }

        [Fact]
        public void UpdateFrom_DoesNotOverwriteNameOrPath()
        {
            var existing = new ScheduledTaskModel { Path = @"\Original", Name = "OriginalName" };
            var fresh = new ScheduledTaskModel { Path = @"\Different", Name = "DifferentName" };

            existing.UpdateFrom(fresh);

            Assert.Equal(@"\Original", existing.Path);
            Assert.Equal("OriginalName", existing.Name);
        }

        [Fact]
        public void UpdateFrom_RefreshesLastAndNextRunTimes()
        {
            var existing = new ScheduledTaskModel { Path = @"\A", Name = "A" };
            var newLastRun = new DateTime(2026, 1, 1, 10, 0, 0);
            var newNextRun = new DateTime(2026, 1, 2, 10, 0, 0);
            var fresh = new ScheduledTaskModel { Path = @"\A", Name = "A", LastRunTime = newLastRun, NextRunTime = newNextRun };

            bool raisedLastRun = false, raisedNextRun = false;
            existing.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ScheduledTaskModel.LastRunTime)) raisedLastRun = true;
                if (e.PropertyName == nameof(ScheduledTaskModel.NextRunTime)) raisedNextRun = true;
            };

            existing.UpdateFrom(fresh);

            Assert.Equal(newLastRun, existing.LastRunTime);
            Assert.Equal(newNextRun, existing.NextRunTime);
            Assert.True(raisedLastRun);
            Assert.True(raisedNextRun);
        }

        [Fact]
        public void UpdateFrom_CopiesActionsAndTags()
        {
            var existing = new ScheduledTaskModel { Path = @"\A", Name = "A" };
            var fresh = new ScheduledTaskModel
            {
                Path = @"\A",
                Name = "A",
                Tags = new ObservableCollection<string> { "x", "y" },
                Actions = new ObservableCollection<TaskActionModel> { new() { Command = "notepad.exe" } }
            };

            existing.UpdateFrom(fresh);

            Assert.Equal(new[] { "x", "y" }, existing.Tags);
            Assert.Single(existing.Actions);
            Assert.Equal("notepad.exe", existing.Actions[0].Command);
        }
    }
}
