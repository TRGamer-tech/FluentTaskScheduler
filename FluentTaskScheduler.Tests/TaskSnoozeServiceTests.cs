using System;
using System.IO;
using System.Linq;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Services;
using FluentTaskScheduler.Tests.Fakes;

namespace FluentTaskScheduler.Tests
{
    // TaskSnoozeService is a process-wide static singleton like the other services here, so these
    // tests must not run alongside anything else that touches it.
    [Collection("StaticServices")]
    public class TaskSnoozeServiceTests : IDisposable
    {
        private readonly Func<ITaskServiceWrapper> _originalFactory;
        private readonly FakeTaskServiceWrapper _fake = new();
        private readonly string _statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentTaskScheduler", "task_snoozes.json");
        private readonly string? _backup;

        private const string TaskPath = @"\Some\Task";

        public TaskSnoozeServiceTests()
        {
            // The service persists to a fixed LocalAppData path; preserve any real file so running
            // the suite never destroys the developer's actual snoozes.
            if (File.Exists(_statePath)) _backup = File.ReadAllText(_statePath);
            if (File.Exists(_statePath)) File.Delete(_statePath);

            _originalFactory = TaskSnoozeService.TaskServiceFactory;
            TaskSnoozeService.TaskServiceFactory = () => _fake;
            TaskSnoozeService.ResetForTests();

            _fake.Tasks.Add(new ScheduledTaskModel { Path = TaskPath, Name = "Task", IsEnabled = true });
        }

        public void Dispose()
        {
            // Initialize() arms a repeating expiry timer and may kick off a background restore.
            // Both have to be stopped and drained before the factory is swapped back, or a late
            // callback runs against a torn-down fixture and takes the test host down with it.
            TaskSnoozeService.Shutdown();
            try { TaskSnoozeService.LastBackgroundOperation?.Wait(TimeSpan.FromSeconds(5)); } catch { }

            TaskSnoozeService.TaskServiceFactory = _originalFactory;
            TaskSnoozeService.ResetForTests();
            try
            {
                if (_backup != null) File.WriteAllText(_statePath, _backup);
                else if (File.Exists(_statePath)) File.Delete(_statePath);
            }
            catch { }
        }

        [Fact]
        public void Snooze_DisablesOnlyThatTask()
        {
            _fake.Tasks.Add(new ScheduledTaskModel { Path = @"\Other", Name = "Other", IsEnabled = true });

            TaskSnoozeService.Snooze(TaskPath, TimeSpan.FromMinutes(30));

            Assert.Equal(new[] { (TaskPath, false) }, _fake.EnableCalls);
            Assert.True(TaskSnoozeService.IsSnoozed(TaskPath));
            Assert.False(TaskSnoozeService.IsSnoozed(@"\Other"));
        }

        [Fact]
        public void Snooze_PersistsStateBeforeTouchingTheTask()
        {
            // The record must be durable before the task is disabled, so a crash mid-operation still
            // leaves something Initialize() can restore from.
            TaskSnoozeService.Snooze(TaskPath, TimeSpan.FromHours(1));

            Assert.True(File.Exists(_statePath));
            Assert.Contains(TaskPath.Replace(@"\", @"\\"), File.ReadAllText(_statePath));
        }

        [Fact]
        public void Snooze_WithNonPositiveDuration_IsIgnored()
        {
            TaskSnoozeService.Snooze(TaskPath, TimeSpan.Zero);
            TaskSnoozeService.Snooze(TaskPath, TimeSpan.FromMinutes(-5));

            Assert.Empty(_fake.EnableCalls);
            Assert.False(TaskSnoozeService.IsSnoozed(TaskPath));
        }

        [Fact]
        public void Snooze_ForMissingTask_DoesNothing()
        {
            TaskSnoozeService.Snooze(@"\Does\Not\Exist", TimeSpan.FromMinutes(30));

            Assert.Empty(_fake.EnableCalls);
            Assert.False(TaskSnoozeService.IsSnoozed(@"\Does\Not\Exist"));
        }

        [Fact]
        public void IsSnoozed_ReportsFalseOnceTheWindowElapsed()
        {
            TaskSnoozeService.Snooze(TaskPath, TimeSpan.FromMilliseconds(1));
            System.Threading.Thread.Sleep(30);

            Assert.False(TaskSnoozeService.IsSnoozed(TaskPath));
            Assert.Null(TaskSnoozeService.GetSnooze(TaskPath));
        }

        [Fact]
        public void Cancel_ReEnablesTheTaskAndClearsTheSnooze()
        {
            TaskSnoozeService.Snooze(TaskPath, TimeSpan.FromHours(2));
            _fake.EnableCalls.Clear();

            TaskSnoozeService.Cancel(TaskPath);

            Assert.Equal(new[] { (TaskPath, true) }, _fake.EnableCalls);
            Assert.False(TaskSnoozeService.IsSnoozed(TaskPath));
        }

        [Fact]
        public void Cancel_OnATaskThatIsNotSnoozed_DoesNothing()
        {
            TaskSnoozeService.Cancel(TaskPath);
            Assert.Empty(_fake.EnableCalls);
        }

        [Fact]
        public void Cancel_DoesNotReEnableATaskThatWasAlreadyDisabled()
        {
            // A task the user had disabled themselves must stay disabled when the snooze ends,
            // otherwise snoozing would silently switch it back on.
            _fake.Tasks[0].IsEnabled = false;

            TaskSnoozeService.Snooze(TaskPath, TimeSpan.FromHours(1));
            _fake.EnableCalls.Clear();

            TaskSnoozeService.Cancel(TaskPath);

            Assert.Empty(_fake.EnableCalls);
        }

        [Fact]
        public void Initialize_RestoresTasksWhoseSnoozeElapsedWhileClosed()
        {
            File.WriteAllText(_statePath,
                $$"""
                [{"TaskPath":"{{TaskPath.Replace("\\", "\\\\")}}","UntilUtc":"2000-01-01T00:00:00Z","UntilReboot":false,"BootStamp":"","WasEnabled":true}]
                """);

            TaskSnoozeService.Initialize();
            TaskSnoozeService.LastBackgroundOperation?.Wait(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { (TaskPath, true) }, _fake.EnableCalls);
            Assert.False(TaskSnoozeService.IsSnoozed(TaskPath));

            TaskSnoozeService.Shutdown();
        }

        [Fact]
        public void Initialize_KeepsSnoozesThatAreStillRunning()
        {
            string until = DateTime.UtcNow.AddHours(3).ToString("O");
            File.WriteAllText(_statePath,
                $$"""
                [{"TaskPath":"{{TaskPath.Replace("\\", "\\\\")}}","UntilUtc":"{{until}}","UntilReboot":false,"BootStamp":"","WasEnabled":true}]
                """);

            TaskSnoozeService.Initialize();
            TaskSnoozeService.LastBackgroundOperation?.Wait(TimeSpan.FromSeconds(5));

            Assert.Empty(_fake.EnableCalls);
            Assert.True(TaskSnoozeService.IsSnoozed(TaskPath));

            TaskSnoozeService.Shutdown();
        }

        [Fact]
        public void UntilRebootSnooze_IsConsideredOverAfterTheMachineRestarts()
        {
            // A stale boot stamp is how a reboot is detected, so an entry carrying one must read as
            // elapsed rather than keeping the task disabled forever.
            File.WriteAllText(_statePath,
                $$"""
                [{"TaskPath":"{{TaskPath.Replace("\\", "\\\\")}}","UntilUtc":null,"UntilReboot":true,"BootStamp":"1999-01-01T00:00:00.0000000Z","WasEnabled":true}]
                """);

            TaskSnoozeService.Initialize();
            TaskSnoozeService.LastBackgroundOperation?.Wait(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { (TaskPath, true) }, _fake.EnableCalls);
            Assert.False(TaskSnoozeService.IsSnoozed(TaskPath));

            TaskSnoozeService.Shutdown();
        }

        [Fact]
        public void SnoozeUntilReboot_SurvivesWithinTheSameBoot()
        {
            TaskSnoozeService.SnoozeUntilReboot(TaskPath);

            Assert.Equal(new[] { (TaskPath, false) }, _fake.EnableCalls);
            Assert.True(TaskSnoozeService.IsSnoozed(TaskPath));
            Assert.True(TaskSnoozeService.GetSnooze(TaskPath)!.UntilReboot);
        }

        [Fact]
        public void SnoozeUntilLocalTime_InThePast_RollsToTomorrow()
        {
            var pastToday = DateTime.Now.AddHours(-2);

            TaskSnoozeService.SnoozeUntilLocalTime(TaskPath, pastToday);

            var entry = TaskSnoozeService.GetSnooze(TaskPath);
            Assert.NotNull(entry);
            Assert.True(entry!.UntilUtc > DateTime.UtcNow);
        }

        [Fact]
        public void SnoozingTheSameTaskTwice_ReplacesTheEarlierWindow()
        {
            TaskSnoozeService.Snooze(TaskPath, TimeSpan.FromMinutes(30));
            var first = TaskSnoozeService.GetSnooze(TaskPath)!.UntilUtc;

            TaskSnoozeService.Snooze(TaskPath, TimeSpan.FromHours(5));
            var second = TaskSnoozeService.GetSnooze(TaskPath)!.UntilUtc;

            Assert.True(second > first);
            Assert.Single(_fake.EnableCalls.Where(c => c.Enabled == false).Select(c => c.Path).Distinct());
        }
    }
}
