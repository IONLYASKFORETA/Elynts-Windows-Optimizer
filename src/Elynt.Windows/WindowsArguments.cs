using System.Text;

namespace Elynt.Windows;

/// <summary>
/// Builds a Windows command line the way .NET's own Process does (PasteArguments), so the child reads back exactly
/// the arguments given under CommandLineToArgvW rules: backslashes are literal except before a quote or the closing
/// quote, where they are doubled, and quotes inside an argument are escaped.
/// </summary>
public static class WindowsArguments
{
    public static string CommandLine(string program, IEnumerable<string> arguments) =>
        string.Join(' ', new[] { program }.Concat(arguments).Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            return argument;

        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            quoted.Append(c);
            backslashes = 0;
        }
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
