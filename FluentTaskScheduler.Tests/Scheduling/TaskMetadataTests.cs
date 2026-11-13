using System.Collections.Generic;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Scheduling;
using Xunit;

namespace FluentTaskScheduler.Tests.Scheduling
{
    public class TaskMetadataTests
    {
        [Fact]
        public void Split_APlainDescription_HasNoMetadata()
        {
            var parts = TaskMetadata.Split("Backs up the database");

            Assert.Equal("Backs up the database", parts.Description);
            Assert.Equal("", parts.Category);
            Assert.Empty(parts.Tags);
            Assert.False(parts.Pipeline.HasAnyTargets);
        }

        [Fact]
        public void Split_Null_IsAnEmptyDescription()
        {
            Assert.Equal("", TaskMetadata.Split(null).Description);
        }

        [Fact]
        public void Combine_WithoutMetadata_LeavesTheDescriptionAlone()
        {
            Assert.Equal("Just text", TaskMetadata.Combine("Just text", "", new List<string>(), new TaskPipeline()));
        }

        [Fact]
        public void Combine_ThenSplit_RoundTrips()
        {
            var pipeline = new TaskPipeline { IsEnabled = true, OnSuccessTasks = { @"\Next" }, OnFailureTasks = { @"\Alert" } };

            string raw = TaskMetadata.Combine("Nightly job", "Maintenance", new[] { "db", "nightly" }, pipeline);
            var parts = TaskMetadata.Split(raw);

            Assert.Equal("Nightly job", parts.Description);
            Assert.Equal("Maintenance", parts.Category);
            Assert.Equal(new[] { "db", "nightly" }, parts.Tags);
            Assert.True(parts.Pipeline.IsEnabled);
            Assert.Equal(new[] { @"\Next" }, parts.Pipeline.OnSuccessTasks);
            Assert.Equal(new[] { @"\Alert" }, parts.Pipeline.OnFailureTasks);
        }

        [Fact]
        public void Combine_ReplacesAnExistingBlockInsteadOfStackingAnother()
        {
            string first = TaskMetadata.Combine("Text", "Old", null, null);
            string second = TaskMetadata.Combine(first, "New", null, null);

            Assert.Equal(1, CountOccurrences(second, "FTS_META"));
            Assert.Equal("New", TaskMetadata.Split(second).Category);
        }

        [Fact]
        public void Combine_RemovingAllMetadata_RestoresThePlainDescription()
        {
            string tagged = TaskMetadata.Combine("Text", "Work", new[] { "a" }, null);

            Assert.Equal("Text", TaskMetadata.Combine(tagged, "", new List<string>(), null));
        }

        [Fact]
        public void Split_ReadsTheBlockWrittenByEarlierVersions()
        {
            const string stored = "Old task\n\n<!-- FTS_META:{\"Category\":\"Personal\",\"Tags\":[\"urgent\",\"sync\"],\"Pipeline\":{\"IsEnabled\":true,\"OnSuccessTasks\":[\"\\\\B\"],\"OnFailureTasks\":[],\"HasAnyTargets\":true,\"IsActive\":true}} -->";

            var parts = TaskMetadata.Split(stored);

            Assert.Equal("Old task", parts.Description);
            Assert.Equal("Personal", parts.Category);
            Assert.Equal(new[] { "urgent", "sync" }, parts.Tags);
            Assert.Equal(new[] { @"\B" }, parts.Pipeline.OnSuccessTasks);
        }

        [Fact]
        public void Split_ADamagedBlock_KeepsTheWholeTextVisible()
        {
            const string damaged = "Text\n\n<!-- FTS_META:{not json -->";

            var parts = TaskMetadata.Split(damaged);

            Assert.Equal(damaged, parts.Description);
            Assert.Equal("", parts.Category);
        }

        [Fact]
        public void Combine_OverADamagedBlock_ReplacesIt()
        {
            string repaired = TaskMetadata.Combine("Text\n\n<!-- FTS_META:{not json -->", "Work", null, null);

            Assert.Equal("Work", TaskMetadata.Split(repaired).Category);
            Assert.Equal("Text", TaskMetadata.Split(repaired).Description);
        }

        [Fact]
        public void Split_DropsBlankTags()
        {
            const string stored = "<!-- FTS_META:{\"Category\":\"\",\"Tags\":[\"a\",\"\",\"  \"]} -->";

            Assert.Equal(new[] { "a" }, TaskMetadata.Split(stored).Tags);
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            for (int i = text.IndexOf(value, System.StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, System.StringComparison.Ordinal)) count++;
            return count;
        }
    }
}
