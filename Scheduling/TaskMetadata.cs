using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentTaskScheduler.Models;

namespace FluentTaskScheduler.Scheduling
{
    /// <summary>
    /// Category, tags and completion actions are not Task Scheduler concepts. They are kept in a
    /// JSON comment appended to the task description, so they travel with the task (export, other
    /// machines) without a database of their own.
    /// </summary>
    public static class TaskMetadata
    {
        private const string Prefix = "<!-- FTS_META:";
        private const string Suffix = " -->";

        /// <summary>What a raw task description holds: the text the user wrote, plus the metadata hidden in it.</summary>
        public sealed record Parts(string Description, string Category, IReadOnlyList<string> Tags, TaskPipeline Pipeline);

        // Property names are the stored format; do not rename.
        private sealed class Block
        {
            public string? Category { get; set; }
            public List<string>? Tags { get; set; }
            public TaskPipeline? Pipeline { get; set; }
        }

        /// <summary>Separates the visible description from the metadata block.</summary>
        public static Parts Split(string? rawDescription)
        {
            string raw = rawDescription ?? "";
            var plain = new Parts(raw, "", Array.Empty<string>(), new TaskPipeline());

            int start = raw.IndexOf(Prefix, StringComparison.Ordinal);
            int end = start < 0 ? -1 : raw.IndexOf(Suffix, start, StringComparison.Ordinal);
            if (end < 0) return plain;

            Block? block;
            try { block = JsonSerializer.Deserialize<Block>(raw[(start + Prefix.Length)..end]); }
            catch (JsonException) { return plain; } // damaged: show the description as it is rather than hide text

            if (block is null) return plain;
            return new Parts(
                raw[..start].TrimEnd(),
                block.Category ?? "",
                block.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray() ?? Array.Empty<string>(),
                block.Pipeline ?? new TaskPipeline());
        }

        /// <summary>Builds the raw description to store: the visible text followed by the metadata block, if there is any metadata.</summary>
        public static string Combine(string? description, string? category, IReadOnlyList<string>? tags, TaskPipeline? pipeline)
        {
            // Whatever follows an existing marker is replaced, even if it could not be read.
            string text = description ?? "";
            int marker = text.IndexOf(Prefix, StringComparison.Ordinal);
            if (marker >= 0) text = text[..marker].TrimEnd();

            bool hasPipeline = pipeline is { } p && (p.IsEnabled || p.HasAnyTargets);
            bool hasTags = tags is { Count: > 0 };
            if (string.IsNullOrEmpty(category) && !hasTags && !hasPipeline) return text;

            string json = JsonSerializer.Serialize(new Block
            {
                Category = category,
                Tags = tags?.ToList(),
                Pipeline = hasPipeline ? pipeline : null
            });
            return $"{text}\n\n{Prefix}{json}{Suffix}".Trim();
        }
    }
}
