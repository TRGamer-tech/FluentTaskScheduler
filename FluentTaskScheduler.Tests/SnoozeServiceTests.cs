using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Services;
using FluentTaskScheduler.Tests.Fakes;

namespace FluentTaskScheduler.Tests
{
    // Covers item 1.6: global snooze must exclude \Microsoft\ tasks by default, persist the
    // disabled-path list before touching any task, and recover cleanly from a crash mid-sweep.
    [Collection("StaticServices")]
    public class SnoozeServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly Func<ITaskServiceWrapper> _originalFactory;

        public SnoozeServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "FTS_Tests_" + Guid.NewGuid().ToString("N"));
            SettingsService.UseStorageForTests(_tempDir);
            SettingsService.ShowNotifications = false; // avoid real toast activation during tests
            _originalFactory = SnoozeService.TaskServiceFactory;
        }

        public void Dispose()
        {
            SnoozeService.TaskServiceFactory = _originalFactory;
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private static ScheduledTaskModel Task(string path, bool enabled = true, bool readOnly = false) =>
            new() { Path = path, Name = System.IO.Path.GetFileName(path), IsEnabled = enabled, IsReadOnlyFallback = readOnly };

        [Fact]
        public void GetSuspendCandidates_ExcludesMicrosoftTasksByDefault()
        {
            var fake = new FakeTaskServiceWrapper();
            fake.Tasks.Add(Task(@"\Microsoft\Windows\Defender\Scan"));
            fake.Tasks.Add(Task(@"\MyApp\Backup"));
            SnoozeService.TaskServiceFactory = () => fake;
            SettingsService.SnoozeIncludeMicrosoftTasks = false;

            var candidates = SnoozeService.GetSuspendCandidates();

            Assert.DoesNotContain(@"\Microsoft\Windows\Defender\Scan", candidates);
            Assert.Contains(@"\MyApp\Backup", candidates);
        }

        [Fact]
        public void GetSuspendCandidates_IncludesMicrosoftTasksWhenExplicitlyOptedIn()
        {
            var fake = new FakeTaskServiceWrapper();
            fake.Tasks.Add(Task(@"\Microsoft\Windows\Defender\Scan"));
            SnoozeService.TaskServiceFactory = () => fake;
            SettingsService.SnoozeIncludeMicrosoftTasks = true;

            var candidates = SnoozeService.GetSuspendCandidates();

            Assert.Contains(@"\Microsoft\Windows\Defender\Scan", candidates);
        }

        [Fact]
        public void GetSuspendCandidates_SkipsAlreadyDisabledAndReadOnlyTasks()
        {
            var fake = new FakeTaskServiceWrapper();
            fake.Tasks.Add(Task(@"\MyApp\AlreadyOff", enabled: false));
            fake.Tasks.Add(Task(@"\MyApp\ReadOnly", readOnly: true));
            fake.Tasks.Add(Task(@"\MyApp\Normal"));
            SnoozeService.TaskServiceFactory = () => fake;

            var candidates = SnoozeService.GetSuspendCandidates();

            Assert.DoesNotContain(@"\MyApp\AlreadyOff", candidates);
            Assert.DoesNotContain(@"\MyApp\ReadOnly", candidates);
            Assert.Contains(@"\MyApp\Normal", candidates);
        }

        [Fact]
        public void DisableCandidates_DisablesEachTaskAndToleratesProtectedTasks()
        {
            var fake = new FakeTaskServiceWrapper();
            fake.Tasks.Add(Task(@"\MyApp\A"));
            fake.Tasks.Add(Task(@"\MyApp\Protected"));
            fake.ProtectedPaths.Add(@"\MyApp\Protected");
            SnoozeService.TaskServiceFactory = () => fake;

            SnoozeService.DisableCandidates(new List<string> { @"\MyApp\A", @"\MyApp\Protected" });

            Assert.Contains((@"\MyApp\A", false), fake.EnableCalls);
            // The protected task's failure must not throw out of DisableCandidates.
            Assert.False(fake.Tasks.Single(t => t.Path == @"\MyApp\A").IsEnabled);
        }

        [Fact]
        public void RestoreSuspendedTasks_ReEnablesEveryGivenPath()
        {
            var fake = new FakeTaskServiceWrapper();
            fake.Tasks.Add(Task(@"\MyApp\A", enabled: false));
            fake.Tasks.Add(Task(@"\MyApp\B", enabled: false));
            SnoozeService.TaskServiceFactory = () => fake;

            SnoozeService.RestoreSuspendedTasks(new List<string> { @"\MyApp\A", @"\MyApp\B" });

            Assert.True(fake.Tasks[0].IsEnabled);
            Assert.True(fake.Tasks[1].IsEnabled);
        }

        [Fact]
        public void CandidateList_IsPersistedBeforeTheBackgroundSweepDisablesAnything()
        {
            // Covers 1.6c: the candidate list must be durable on disk before disabling starts, so a
            // crash mid-sweep is recoverable. This drives the same GetSuspendCandidates ->
            // SaveSnoozeState -> DisableCandidates sequence SnoozeService.Apply() uses internally
            // (rather than going through Apply()/Snooze() themselves, which also fire toast
            // notifications and localized strings that need a real packaged app context to run).
            var fake = new GatedFakeTaskServiceWrapper();
            fake.Tasks.Add(Task(@"\MyApp\A"));
            fake.Tasks.Add(Task(@"\MyApp\B"));
            SnoozeService.TaskServiceFactory = () => fake;

            var candidates = SnoozeService.GetSuspendCandidates();
            SettingsService.SaveSnoozeState(true, DateTime.UtcNow.AddMinutes(30), false, "", candidates);
            var sweepTask = System.Threading.Tasks.Task.Run(() => SnoozeService.DisableCandidates(candidates));

            // The candidate list must already be durable on disk while the sweep is still gated.
            var persisted = SettingsService.SnoozeDisabledTaskPaths;
            Assert.Contains(@"\MyApp\A", persisted);
            Assert.Contains(@"\MyApp\B", persisted);
            Assert.True(SettingsService.IsSnoozed);
            Assert.Empty(fake.EnableCalls); // nothing disabled yet — still gated

            fake.Gate.Set();
            sweepTask.Wait(TimeSpan.FromSeconds(5));

            Assert.Equal(2, fake.EnableCalls.Count);
        }

        [Fact]
        public void Initialize_ResumesAnInterruptedRestore_WhenNotSnoozedButPathsAreLeftOver()
        {
            // Simulates: a Cancel() persisted "not snoozed" + the disabled-path list, then the app
            // was killed before the background restore finished (see item 1.6c / verification #5).
            var fake = new FakeTaskServiceWrapper();
            fake.Tasks.Add(Task(@"\MyApp\A", enabled: false));
            SnoozeService.TaskServiceFactory = () => fake;

            SettingsService.SaveSnoozeState(false, null, false, "", new List<string> { @"\MyApp\A" });

            SnoozeService.Initialize();
            SnoozeService.LastBackgroundOperation?.Wait(TimeSpan.FromSeconds(5));

            Assert.True(fake.Tasks.Single(t => t.Path == @"\MyApp\A").IsEnabled);

            SnoozeService.Shutdown();
        }

        private sealed class GatedFakeTaskServiceWrapper : FakeTaskServiceWrapper
        {
            public ManualResetEventSlim Gate { get; } = new(initialState: false);

            public override void SetTaskEnabled(string path, bool enabled)
            {
                Gate.Wait(TimeSpan.FromSeconds(5));
                base.SetTaskEnabled(path, enabled);
            }
        }
    }
}
