namespace Elynt.Windows.Tests;

/// <summary>
/// Building a command line for CreateProcessAsUser so the child's argv (CommandLineToArgvW rules) is exactly the
/// arguments given: backslashes are literal except before a quote, where they are doubled.
/// </summary>
public sealed class WindowsArgumentsTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\Program Files\", "\"C:\\Program Files\\\\\"")]
    [InlineData(@"C:\Temp\", @"C:\Temp\")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"a\""b", "\"a\\\\\\\"b\"")]
    [InlineData("tab\there", "\"tab\there\"")]
    public void Each_argument_is_quoted_so_Windows_reads_it_back_unchanged(string argument, string expected)
    {
        Assert.Equal(expected, WindowsArguments.Quote(argument));
    }

    [Fact]
    public void A_command_line_joins_the_quoted_program_and_arguments()
    {
        Assert.Equal(
            "\"C:\\Program Files\\x.exe\" -a \"b c\\\\\"",
            WindowsArguments.CommandLine(@"C:\Program Files\x.exe", ["-a", @"b c\"]));
    }
}
