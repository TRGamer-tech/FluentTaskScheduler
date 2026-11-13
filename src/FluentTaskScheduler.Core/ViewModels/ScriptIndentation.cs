using System.Text;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// A change to a text: <see cref="Replacement"/> takes the place of the <see cref="Length"/>
/// characters at <see cref="Start"/>, and then the given part of the new text is selected.
/// </summary>
public readonly record struct TextEdit(int Start, int Length, string Replacement, int SelectionStart, int SelectionLength);

/// <summary>What Tab does in the text of the script editor, as in a code editor.</summary>
public static class ScriptIndentation
{
    private const string Indent = "    ";

    /// <summary>
    /// A selection across lines, or of one whole line, indents the lines it touches and keeps
    /// them selected. Empty lines stay empty, and a line the selection only reaches at its very
    /// start stays as it is. Otherwise the indent takes the place of the selection, if there is one.
    /// </summary>
    /// <param name="text">The text, with lines ending in CR, LF or both: a text box ends them in CR.</param>
    public static TextEdit Tab(string text, int selectionStart, int selectionLength)
    {
        int start = selectionStart, end = selectionStart + selectionLength;
        int firstLine = LineStart(text, start);
        bool acrossLines = text.AsSpan(start, selectionLength).IndexOfAny('\r', '\n') >= 0;
        bool wholeLine = selectionLength > 0 && start == firstLine && end == LineEnd(text, start);
        if (!acrossLines && !wholeLine) return new TextEdit(start, selectionLength, Indent, start + Indent.Length, 0);

        var indented = new StringBuilder();
        int indents = 0;
        int lineStart = firstLine, lineEnd;
        while (true)
        {
            lineEnd = LineEnd(text, lineStart);
            if (lineEnd > lineStart)
            {
                indented.Append(Indent);
                indents++;
            }
            indented.Append(text, lineStart, lineEnd - lineStart);

            int next = NextLineStart(text, lineEnd);
            if (next == lineEnd || next >= end) break;

            indented.Append(text, lineEnd, next - lineEnd);
            lineStart = next;
        }

        // A selection that began with its line now begins with the indent.
        int newStart = start == firstLine ? start : start + Indent.Length;
        int newEnd = end + indents * Indent.Length;
        return new TextEdit(firstLine, lineEnd - firstLine, indented.ToString(), newStart, newEnd - newStart);
    }

    private static int LineStart(string text, int position) => text.AsSpan(0, position).LastIndexOfAny('\r', '\n') + 1;

    /// <summary>Where the line ends: at its line end, or at the end of the text.</summary>
    private static int LineEnd(string text, int lineStart)
    {
        int length = text.AsSpan(lineStart).IndexOfAny('\r', '\n');
        return length < 0 ? text.Length : lineStart + length;
    }

    /// <summary>Where the line after the one ending at <paramref name="lineEnd"/> starts; the end of the text if there is none.</summary>
    private static int NextLineStart(string text, int lineEnd)
    {
        if (lineEnd == text.Length) return lineEnd;
        return text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? lineEnd + 2 : lineEnd + 1;
    }
}
