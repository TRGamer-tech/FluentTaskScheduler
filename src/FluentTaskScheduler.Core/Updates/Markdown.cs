using System.Text;
using System.Text.RegularExpressions;

namespace FluentTaskScheduler.Updates;

/// <summary>
/// Reads the part of Markdown that release notes on GitHub are written in: headings, paragraphs,
/// bullet and numbered lists, tables and code blocks, and within a line bold, italic, code and
/// links. What it does not know stays the text it is.
/// </summary>
public static partial class Markdown
{
    public static ValueList<MarkdownBlock> Parse(string? markdown)
    {
        string[] lines = HtmlComment().Replace(markdown ?? "", "").ReplaceLineEndings("\n").Split('\n');
        var blocks = new List<MarkdownBlock>();
        var listIndents = new List<int>();

        // The paragraph or list item being read. A line that starts nothing new continues it.
        StringBuilder? open = null;
        Func<ValueList<MarkdownRun>, MarkdownBlock> finish = runs => new MarkdownParagraph(runs);

        void Close()
        {
            if (open is null) return;
            blocks.Add(finish(Runs(open.ToString())));
            open = null;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (line.Length == 0)
            {
                Close();
            }
            else if (IsCodeFence(line))
            {
                Close();
                listIndents.Clear();
                var code = new List<string>();
                while (++i < lines.Length && !IsCodeFence(lines[i])) code.Add(lines[i].TrimEnd());
                blocks.Add(new MarkdownCode(string.Join('\n', code)));
            }
            else if (Heading().Match(line) is { Success: true } heading)
            {
                Close();
                listIndents.Clear();
                blocks.Add(new MarkdownHeading(heading.Groups[1].Length, Runs(heading.Groups[2].Value)));
            }
            else if (Rule().IsMatch(line))
            {
                // A dividing line is not drawn; it only ends what came before it.
                Close();
                listIndents.Clear();
            }
            else if (line.Contains('|') && i + 1 < lines.Length && lines[i + 1].Contains('|') && TableDivider().IsMatch(lines[i + 1]))
            {
                Close();
                listIndents.Clear();

                List<string> headings = Cells(line);
                var cells = new List<ValueList<MarkdownRun>>();
                void AddRow(List<string> row)
                {
                    for (int column = 0; column < headings.Count; column++) cells.Add(Runs(column < row.Count ? row[column] : ""));
                }

                AddRow(headings);
                i++;
                while (i + 1 < lines.Length && lines[i + 1].Contains('|')) AddRow(Cells(lines[++i]));
                blocks.Add(new MarkdownTable(headings.Count, cells));
            }
            else if (ListItem().Match(line) is { Success: true } item)
            {
                Close();
                int depth = Depth(listIndents, item.Groups[1].Value);
                string marker = item.Groups[2].Value;
                string? number = char.IsDigit(marker[0]) ? marker : null;
                open = new StringBuilder(item.Groups[3].Value);
                finish = runs => new MarkdownListItem(runs, depth, number);
            }
            else if (open is not null)
            {
                open.Append(' ').Append(line.Trim());
            }
            else
            {
                listIndents.Clear();
                open = new StringBuilder(line.Trim());
                finish = runs => new MarkdownParagraph(runs);
            }
        }

        Close();
        return blocks;
    }

    private static bool IsCodeFence(string line) => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    /// <summary>How deep a list item is nested, from its indentation and that of the items before it.</summary>
    private static int Depth(List<int> openLevels, string indentation)
    {
        int indent = indentation.Sum(c => c == '\t' ? 4 : 1);
        while (openLevels.Count > 0 && openLevels[^1] > indent) openLevels.RemoveAt(openLevels.Count - 1);
        if (openLevels.Count == 0 || openLevels[^1] < indent) openLevels.Add(indent);
        return openLevels.Count - 1;
    }

    private static List<string> Cells(string row)
    {
        string inner = row.Trim();
        if (inner.StartsWith('|')) inner = inner[1..];
        if (inner.EndsWith('|') && !inner.EndsWith("\\|", StringComparison.Ordinal)) inner = inner[..^1];
        return CellDivider().Split(inner).Select(cell => cell.Trim().Replace("\\|", "|")).ToList();
    }

    private static ValueList<MarkdownRun> Runs(string text)
    {
        var runs = new List<MarkdownRun>();
        AddRuns(text, new MarkdownRun(""), runs);
        return runs;
    }

    /// <param name="style">The formatting <paramref name="text"/> stands in; what is found inside adds to it.</param>
    private static void AddRuns(string text, MarkdownRun style, List<MarkdownRun> runs)
    {
        var plain = new StringBuilder();
        void AddPlain()
        {
            if (plain.Length == 0) return;
            runs.Add(style with { Text = plain.ToString() });
            plain.Clear();
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && (char.IsPunctuation(text[i + 1]) || char.IsSymbol(text[i + 1])))
            {
                plain.Append(text[i + 1]);
                i += 2;
            }
            else if (c == '`')
            {
                int end = CodeEnd(text, i, out string code);
                if (end < 0)
                {
                    int ticks = RunLength(text, i);
                    plain.Append(c, ticks);
                    i += ticks;
                }
                else
                {
                    AddPlain();
                    runs.Add(style with { Text = code, Code = true });
                    i = end;
                }
            }
            else if (c is '*' or '_')
            {
                int width = RunLength(text, i);
                int close = width <= 3 ? ClosingDelimiter(text, i, width) : -1;
                if (close < 0)
                {
                    plain.Append(c, width);
                    i += width;
                }
                else
                {
                    AddPlain();
                    AddRuns(text[(i + width)..close], style with { Bold = style.Bold || width >= 2, Italic = style.Italic || width != 2 }, runs);
                    i = close + width;
                }
            }
            else if (LinkEnd(text, c == '!' ? i + 1 : i, out string label, out string address) is var linkEnd and > 0)
            {
                // An image cannot be shown in running text; its description links to it instead.
                AddPlain();
                AddRuns(label.Length > 0 ? label : address, style with { Link = address }, runs);
                i = linkEnd;
            }
            else if (c == '<' && IsAddress(text, i + 1) && text.IndexOf('>', i) is var bracket and > 0 && !text[i..bracket].Any(char.IsWhiteSpace))
            {
                AddPlain();
                AddAddress(text[(i + 1)..bracket], style, runs);
                i = bracket + 1;
            }
            else if (style.Link is null && IsAddress(text, i) && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
            {
                AddPlain();
                int end = AddressEnd(text, i);
                AddAddress(text[i..end], style, runs);
                i = end;
            }
            else
            {
                plain.Append(c);
                i++;
            }
        }

        AddPlain();
    }

    private static int RunLength(string text, int start)
    {
        int end = start;
        while (end < text.Length && text[end] == text[start]) end++;
        return end - start;
    }

    /// <summary>Where the code that opens at <paramref name="start"/> ends (just after its closing backticks); -1 if it is never closed.</summary>
    private static int CodeEnd(string text, int start, out string code)
    {
        int ticks = RunLength(text, start);
        for (int i = start + ticks; i < text.Length;)
        {
            if (text[i] != '`')
            {
                i++;
                continue;
            }

            int length = RunLength(text, i);
            if (length == ticks)
            {
                code = text[(start + ticks)..i];
                // Code that itself starts or ends with a backtick is written with a space of padding.
                if (code.Length > 2 && code[0] == ' ' && code[^1] == ' ' && !string.IsNullOrWhiteSpace(code)) code = code[1..^1];
                return i + length;
            }
            i += length;
        }

        code = "";
        return -1;
    }

    /// <summary>
    /// Where the emphasis that opens with the <paramref name="width"/> characters at
    /// <paramref name="open"/> is closed; -1 if those characters do not open any.
    /// </summary>
    private static int ClosingDelimiter(string text, int open, int width)
    {
        char mark = text[open];
        int content = open + width;
        if (content >= text.Length || char.IsWhiteSpace(text[content])) return -1;

        // An underscore inside a word is part of the word: snake_case, file_name.txt.
        if (mark == '_' && open > 0 && char.IsLetterOrDigit(text[open - 1])) return -1;

        for (int i = content; i < text.Length;)
        {
            if (text[i] == '\\')
            {
                i += 2;
            }
            else if (text[i] == '`' && CodeEnd(text, i, out _) is var codeEnd and > 0)
            {
                i = codeEnd;
            }
            else if (text[i] != mark)
            {
                i++;
            }
            else
            {
                int length = RunLength(text, i);
                bool wordGoesOn = mark == '_' && i + length < text.Length && char.IsLetterOrDigit(text[i + length]);
                if (length == width && !char.IsWhiteSpace(text[i - 1]) && !wordGoesOn) return i;
                i += length;
            }
        }
        return -1;
    }

    /// <summary>Reads "[label](address)" at <paramref name="open"/>; returns where it ends, or -1 if that is not what is there.</summary>
    private static int LinkEnd(string text, int open, out string label, out string address)
    {
        label = address = "";
        if (open >= text.Length || text[open] != '[') return -1;

        int labelEnd = MatchingBracket(text, open, '[', ']');
        if (labelEnd < 0 || labelEnd + 1 >= text.Length || text[labelEnd + 1] != '(') return -1;

        int addressEnd = MatchingBracket(text, labelEnd + 1, '(', ')');
        if (addressEnd < 0) return -1;

        // An address can be followed by a title in quotes, which is not shown.
        address = text[(labelEnd + 2)..addressEnd].Trim().Split(' ', 2)[0].Trim('<', '>');
        if (address.Length == 0) return -1;

        label = text[(open + 1)..labelEnd];
        return addressEnd + 1;
    }

    private static int MatchingBracket(string text, int open, char opening, char closing)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == opening) depth++;
            else if (text[i] == closing && --depth == 0) return i;
        }
        return -1;
    }

    private static bool IsAddress(string text, int start)
    {
        ReadOnlySpan<char> rest = text.AsSpan(Math.Min(start, text.Length));
        return rest.StartsWith("https://", StringComparison.Ordinal) || rest.StartsWith("http://", StringComparison.Ordinal);
    }

    private static int AddressEnd(string text, int start)
    {
        int end = start;
        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '<') end++;

        // Punctuation at the end belongs to the sentence, and so does a closing bracket that the address has no opening one for.
        while (end > start)
        {
            ReadOnlySpan<char> address = text.AsSpan(start, end - start);
            char last = address[^1];
            bool ofSentence = ".,;:!?'\"*_".Contains(last) || (last == ')' && address.Count('(') < address.Count(')'));
            if (!ofSentence) break;
            end--;
        }
        return end;
    }

    private static void AddAddress(string address, MarkdownRun style, List<MarkdownRun> runs) =>
        runs.Add(style with { Text = IssueReference(address) ?? address, Link = address });

    /// <summary>
    /// The short name GitHub gives the issue or pull request at <paramref name="address"/>: "#12"
    /// in this project, "owner/name#12" in another; null for any other address. The notes GitHub
    /// generates write out the address of every pull request they list.
    /// </summary>
    private static string? IssueReference(string address)
    {
        if (IssueAddress().Match(address) is not { Success: true } issue) return null;

        string repository = issue.Groups["repository"].Value;
        string number = issue.Groups["number"].Value;
        // GitHub does not tell owners and names apart by case.
        return repository.Equals(AppInfo.Repository, StringComparison.OrdinalIgnoreCase) ? "#" + number : repository + "#" + number;
    }

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();

    [GeneratedRegex(@"^ {0,3}(#{1,6})\s+(.*?)(?:\s+#+)?$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^ {0,3}([-*_])(?:\s*\1){2,}$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"^\s*\|?\s*:?-+:?\s*(?:\|\s*:?-+:?\s*)*\|?\s*$")]
    private static partial Regex TableDivider();

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex CellDivider();

    [GeneratedRegex(@"^(\s*)([-*+]|\d{1,9}[.)])\s+(\S.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^https://github\.com/(?<repository>[^/]+/[^/]+)/(?:pull|issues)/(?<number>\d+)$")]
    private static partial Regex IssueAddress();
}
