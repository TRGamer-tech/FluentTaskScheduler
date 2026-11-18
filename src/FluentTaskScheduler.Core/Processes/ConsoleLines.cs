using System.Text;

namespace FluentTaskScheduler.Processes;

/// <summary>
/// Reads what a console program writes the way a console window shows it: text appears when it
/// arrives, not only once its line is finished, and a carriage return without a line feed goes
/// back to the start of the line, which is how programs redraw a progress display.
/// </summary>
internal static class ConsoleLines
{
    /// <summary>A line that grows beyond this is broken up, so a program that never ends its line cannot fill the memory.</summary>
    internal const int MaxLineLength = 16 * 1024;

    /// <param name="onLine">
    /// Called with a line as it stands and whether it is complete. A line that is not complete
    /// is replaced by what is reported next: the same line with more text, or what the program
    /// wrote over it.
    /// </param>
    public static async Task ReadAsync(TextReader reader, Action<string, bool> onLine)
    {
        var line = new StringBuilder();
        var buffer = new char[4096];
        bool isReported = true;       // whether the line as it stands has been passed on
        bool afterReturn = false;     // the last character was a carriage return; what it means depends on the next

        void Complete()
        {
            onLine(line.ToString(), true);
            line.Clear();
            isReported = true;
        }

        void ReportUnfinished()
        {
            if (isReported) return;
            onLine(line.ToString(), false);
            isReported = true;
        }

        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            for (int i = 0; i < count; i++)
            {
                char c = buffer[i];
                if (afterReturn)
                {
                    // Another carriage return changes nothing: the line is back at its start already.
                    // A program that writes CR LF through a C runtime stream in text mode, which
                    // turns every LF into CR LF, ends its lines in CR CR LF.
                    if (c == '\r') continue;

                    afterReturn = false;
                    if (c == '\n')
                    {
                        Complete();
                        continue;
                    }

                    // Back at the start of the line: what follows is written over what stands there.
                    ReportUnfinished();
                    line.Clear();
                }

                if (c == '\r')
                {
                    afterReturn = true;
                }
                else if (c == '\n' || line.Length == MaxLineLength)
                {
                    Complete();
                    if (c != '\n') line.Append(c);
                    isReported = c == '\n';
                }
                else
                {
                    line.Append(c);
                    isReported = false;
                }
            }

            // The program has stopped writing for the moment; a console shows what is there.
            ReportUnfinished();
        }

        if (line.Length > 0) Complete();
    }
}
