using System.Text.Json;

namespace FluentTaskScheduler.Scheduling;

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
    public sealed record Parts(string Description, string Category, IReadOnlyList<string> Tags, CompletionActions CompletionActions);

    // Property names are the stored format; do not rename.
    private sealed class Block
    {
        public string? Category { get; set; }
        public List<string>? Tags { get; set; }
        public PipelineBlock? Pipeline { get; set; }
    }

    private sealed class PipelineBlock
    {
        public bool IsEnabled { get; set; }
        public List<string>? OnSuccessTasks { get; set; }
        public List<string>? OnFailureTasks { get; set; }
    }

    /// <summary>Separates the visible description from the metadata block.</summary>
    public static Parts Split(string? rawDescription)
    {
        string raw = rawDescription ?? "";
        var plain = new Parts(raw, "", [], CompletionActions.None);

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
            Clean(block.Tags),
            block.Pipeline is { } stored
                ? new CompletionActions { Enabled = stored.IsEnabled, OnSuccess = Clean(stored.OnSuccessTasks), OnFailure = Clean(stored.OnFailureTasks) }
                : CompletionActions.None);
    }

    /// <summary>Builds the raw description to store: the visible text followed by the metadata block, if there is any metadata.</summary>
    public static string Combine(string? description, string? category, IReadOnlyList<string>? tags, CompletionActions? completionActions)
    {
        // Whatever follows an existing marker is replaced, even if it could not be read.
        string text = description ?? "";
        int marker = text.IndexOf(Prefix, StringComparison.Ordinal);
        if (marker >= 0) text = text[..marker].TrimEnd();

        bool hasActions = completionActions is { } actions && (actions.Enabled || actions.HasTargets);
        bool hasTags = tags is { Count: > 0 };
        if (string.IsNullOrEmpty(category) && !hasTags && !hasActions) return text;

        string json = JsonSerializer.Serialize(new Block
        {
            Category = category,
            Tags = tags?.ToList(),
            Pipeline = hasActions
                ? new PipelineBlock
                {
                    IsEnabled = completionActions!.Enabled,
                    OnSuccessTasks = completionActions.OnSuccess.ToList(),
                    OnFailureTasks = completionActions.OnFailure.ToList()
                }
                : null
        });
        return $"{text}\n\n{Prefix}{json}{Suffix}".Trim();
    }

    private static string[] Clean(List<string>? items) =>
        items?.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray() ?? [];
}
