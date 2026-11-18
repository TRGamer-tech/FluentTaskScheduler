using System.Text;

namespace FluentTaskScheduler.Tests.Library;

/// <summary>
/// Splits a command line into arguments the way programs on Windows do (the rules of
/// CommandLineToArgvW, which powershell.exe follows as well), so a test can see what a program
/// would actually receive from the arguments of a task.
/// </summary>
internal static class WindowsCommandLine
{
    public static List<string> Split(string arguments)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false, hasArgument = false;
        int i = 0;

        while (i < arguments.Length)
        {
            char c = arguments[i];
            if (c == '\\')
            {
                int backslashes = 0;
                while (i < arguments.Length && arguments[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                bool beforeQuote = i < arguments.Length && arguments[i] == '"';
                // In front of a quote, two backslashes stand for one, and an odd one out makes the quote a plain character.
                current.Append('\\', beforeQuote ? backslashes / 2 : backslashes);
                hasArgument = true;
                if (beforeQuote && backslashes % 2 == 1)
                {
                    current.Append('"');
                    i++;
                }
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasArgument = true;
            }
            else if (c is ' ' or '\t' && !inQuotes)
            {
                if (hasArgument) result.Add(current.ToString());
                current.Clear();
                hasArgument = false;
            }
            else
            {
                current.Append(c);
                hasArgument = true;
            }
            i++;
        }

        if (hasArgument) result.Add(current.ToString());
        return result;
    }

    /// <summary>The script in arguments for powershell.exe: everything after -Command, as PowerShell puts it together.</summary>
    public static string? PowerShellScriptIn(string arguments)
    {
        List<string> split = Split(arguments);
        int command = split.FindIndex(argument => argument.Equals("-Command", StringComparison.OrdinalIgnoreCase));
        return command < 0 ? null : string.Join(' ', split.Skip(command + 1));
    }
}
