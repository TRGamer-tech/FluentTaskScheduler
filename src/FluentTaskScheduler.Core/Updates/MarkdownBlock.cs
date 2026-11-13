namespace FluentTaskScheduler.Updates;

/// <summary>A stretch of text in one formatting.</summary>
/// <param name="Code">Text that is meant literally: a file name, a command.</param>
/// <param name="Link">The address the text leads to; null for text that is no link.</param>
public sealed record MarkdownRun(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Link = null);

/// <summary>One block of a Markdown text, as <see cref="Markdown.Parse"/> finds them from top to bottom.</summary>
public abstract record MarkdownBlock;

/// <param name="Level">1 for the largest heading, up to 6.</param>
public sealed record MarkdownHeading(int Level, ValueList<MarkdownRun> Text) : MarkdownBlock;

public sealed record MarkdownParagraph(ValueList<MarkdownRun> Text) : MarkdownBlock;

/// <param name="Depth">0 for an item of the list itself, 1 for an item of a list inside it, and so on.</param>
/// <param name="Number">The numbering as written ("1.", "2.") for an item of a numbered list; null for a bullet.</param>
public sealed record MarkdownListItem(ValueList<MarkdownRun> Text, int Depth = 0, string? Number = null) : MarkdownBlock;

/// <summary>A table. Its cells are listed row by row; the first row holds the column headings.</summary>
public sealed record MarkdownTable(int Columns, ValueList<ValueList<MarkdownRun>> Cells) : MarkdownBlock;

/// <summary>Lines that are shown exactly as written, in a fixed-width font.</summary>
public sealed record MarkdownCode(string Text) : MarkdownBlock;
