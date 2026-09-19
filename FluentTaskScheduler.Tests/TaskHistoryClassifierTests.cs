using FluentTaskScheduler.Models;
using FluentTaskScheduler.Services;

namespace FluentTaskScheduler.Tests
{
    // Covers item 1.5: history "Success"/"Failed" counts must come from the raw EventId (and, for
    // 201, the exit code) — never from the localized Result string.
    public class TaskHistoryClassifierTests
    {
        private static TaskHistoryEntry Entry(int eventId, string exitCode = "0") =>
            new() { EventId = eventId, ExitCode = exitCode, Result = "Whatever The Localized String Is" };

        [Theory]
        [InlineData(102)]      // Task Completed
        public void IsSuccess_TrueForCompletedEvent(int eventId)
        {
            Assert.True(TaskHistoryClassifier.IsSuccess(Entry(eventId)));
        }

        [Fact]
        public void IsSuccess_TrueForActionCompletedWithZeroExitCode()
        {
            Assert.True(TaskHistoryClassifier.IsSuccess(Entry(201, "0")));
        }

        [Fact]
        public void IsSuccess_FalseForActionCompletedWithNonZeroExitCode()
        {
            Assert.False(TaskHistoryClassifier.IsSuccess(Entry(201, "1")));
        }

        [Theory]
        [InlineData(103)]  // Task Failed
        [InlineData(111)]  // Task Terminated
        [InlineData(202)]  // Action Failed
        [InlineData(203)]  // Action Launch Failed
        [InlineData(329)]  // Stopped (time limit)
        [InlineData(331)]  // Stopped (on battery)
        public void IsFailure_TrueForKnownFailureEvents(int eventId)
        {
            Assert.True(TaskHistoryClassifier.IsFailure(Entry(eventId)));
        }

        [Fact]
        public void IsFailure_TrueForActionCompletedWithNonZeroExitCode()
        {
            Assert.True(TaskHistoryClassifier.IsFailure(Entry(201, "1")));
        }

        [Fact]
        public void IsFailure_FalseForActionCompletedWithZeroExitCode()
        {
            Assert.False(TaskHistoryClassifier.IsFailure(Entry(201, "0")));
        }

        // Regression: 107 (Task Triggered), 110 (Task Launched), 129/200 (Action Started) used to be
        // miscounted as failures by the old "anything that isn't Completed/Started/Registered" rule.
        [Theory]
        [InlineData(100)] // Task Started
        [InlineData(106)] // Task Registered
        [InlineData(107)] // Task Triggered
        [InlineData(110)] // Task Launched
        [InlineData(129)] // Action Started
        [InlineData(200)] // Action Started
        public void NeutralEvents_AreNeitherSuccessNorFailure(int eventId)
        {
            var entry = Entry(eventId);
            Assert.False(TaskHistoryClassifier.IsSuccess(entry));
            Assert.False(TaskHistoryClassifier.IsFailure(entry));
        }

        [Fact]
        public void Classification_IsIndependentOfLocalizedResultString()
        {
            // Same EventId, wildly different (simulated-localized) Result text — classification must not change.
            var english = new TaskHistoryEntry { EventId = 102, ExitCode = "0", Result = "Task Completed" };
            var german = new TaskHistoryEntry { EventId = 102, ExitCode = "0", Result = "Aufgabe abgeschlossen" };

            Assert.Equal(TaskHistoryClassifier.IsSuccess(english), TaskHistoryClassifier.IsSuccess(german));
            Assert.True(TaskHistoryClassifier.IsSuccess(german));
        }
    }
}
