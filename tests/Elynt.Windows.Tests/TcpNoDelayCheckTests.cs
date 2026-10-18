using System.Text;

namespace Elynt.Windows.Tests;

/// <summary>
/// Checks, on a real Windows 11, whether the TCP/IP driver still names the per-interface registry values the Nagle
/// tweak sets. A driver reads a registry value by its name, stored in the driver as UTF-16 text; a name that isn't in
/// tcpip.sys can't be read by it. The result is written to the test output on CI.
/// </summary>
public sealed class TcpNoDelayCheckTests
{
    private static readonly string TcpipDriver =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "tcpip.sys");

    [Fact]
    public void Reports_whether_tcpip_sys_still_names_TcpAckFrequency_and_TCPNoDelay()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows' own tcpip.sys.");
        var driver = File.ReadAllBytes(TcpipDriver);

        var ackFrequency = ContainsUtf16(driver, "TcpAckFrequency");
        // The search only means something if it finds the value Microsoft documents (KB 328890).
        Assert.SkipUnless(ackFrequency, "tcpip.sys doesn't name TcpAckFrequency as UTF-16 text on this build, so the check can't tell.");

        var noDelay = ContainsUtf16(driver, "TcpNoDelay");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{Environment.OSVersion}: tcpip.sys names TcpAckFrequency: {ackFrequency}; names TCPNoDelay: {noDelay}.");
        TestContext.Current.SendDiagnosticMessage($"tcpip.sys names TCPNoDelay: {noDelay}");
    }

    /// <summary>Case-insensitive search for an ASCII name stored as UTF-16LE.</summary>
    private static bool ContainsUtf16(byte[] haystack, string name)
    {
        var lower = Encoding.Unicode.GetBytes(name.ToLowerInvariant());
        var upper = Encoding.Unicode.GetBytes(name.ToUpperInvariant());
        for (var start = 0; start <= haystack.Length - lower.Length; start++)
        {
            var i = 0;
            while (i < lower.Length && (haystack[start + i] == lower[i] || haystack[start + i] == upper[i]))
                i++;
            if (i == lower.Length)
                return true;
        }
        return false;
    }
}
