using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Elynt.Core.Ipc;
using Microsoft.Win32.SafeHandles;

namespace Elynt.Windows.Tests;

/// <summary>
/// The service's real administrator check, through a real pipe opened the way the window opens it. An administrator
/// account's window runs unelevated, and its token then has Administrators only as a deny-only group: that must
/// still count as an administrator account.
/// </summary>
public sealed class RealAdministratorCheckTests
{
    public RealAdministratorCheckTests() =>
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows' own tokens and pipes.");

    [Fact]
    public async Task An_administrator_accounts_unelevated_window_counts_as_an_administrator_account()
    {
        using var account = WindowsIdentity.GetCurrent(TokenAccessLevels.MaximumAllowed);
        Assert.SkipUnless(new WindowsPrincipal(account).IsInRole(WindowsBuiltInRole.Administrator),
            "Needs an elevated administrator account, as CI's runners have.");
        using var unelevated = Unelevated(account);
        // Filtered the way UAC filters an administrator's window: asking "is it in the role" says no.
        Assert.False(new WindowsPrincipal(unelevated).IsInRole(WindowsBuiltInRole.Administrator));

        Assert.True(await AskAboutAsync(unelevated));
    }

    [Fact]
    public async Task An_elevated_administrator_window_counts_too()
    {
        using var account = WindowsIdentity.GetCurrent(TokenAccessLevels.MaximumAllowed);
        Assert.SkipUnless(new WindowsPrincipal(account).IsInRole(WindowsBuiltInRole.Administrator),
            "Needs an elevated administrator account, as CI's runners have.");

        Assert.True(await AskAboutAsync(account));
    }

    /// <summary>
    /// Opens a fresh pipe as <paramref name="window"/> and asks the real check about whoever opened it, as the
    /// service does once a connection's first request has arrived.
    /// </summary>
    private static async Task<bool> AskAboutAsync(WindowsIdentity window)
    {
        var name = $"elynt-test-{Guid.NewGuid():N}";
        // Open to this account by name. Windows' default rules let in Administrators and the pipe's owner, which is
        // often the Administrators group: neither lets in a token where that group is deny-only.
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            window.User ?? throw new InvalidOperationException("The token has no user."),
            PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        await using var service = NamedPipeServerStreamAcl.Create(
            name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
        var connected = service.WaitForConnectionAsync(TestContext.Current.CancellationToken);

        // Opened and written to as that account, so whichever way Windows tracks the client, it sees that token.
        await using var pipe = WindowsIdentity.RunImpersonated(window.AccessToken, () =>
        {
            var opened = EnginePipeClient.NewPipe(name);
            opened.Connect(5000);
            opened.WriteByte(1);
            return opened;
        });
        await connected;
        // Windows lets a server impersonate its client only once it has read from it.
        Assert.Equal(1, service.ReadByte());

        return new WindowsAdministratorCheck().IsAdministratorAccount(service);
    }

    /// <summary>
    /// The account's token with Administrators turned deny-only, as UAC hands an administrator's unelevated window.
    /// CI's runners have UAC off, so there is no split token to take; this makes the same group the same way.
    /// </summary>
    private static WindowsIdentity Unelevated(WindowsIdentity account)
    {
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var sid = new byte[administrators.BinaryLength];
        administrators.GetBinaryForm(sid, 0);
        var pinned = GCHandle.Alloc(sid, GCHandleType.Pinned);
        try
        {
            SidAndAttributes[] disable = [new() { Sid = pinned.AddrOfPinnedObject() }];
            if (!CreateRestrictedToken(account.AccessToken, 0, disable.Length, disable, 0, IntPtr.Zero, 0, IntPtr.Zero, out var restricted))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't make an unelevated token.");
            using (restricted)
                return new WindowsIdentity(restricted.DangerousGetHandle());
        }
        finally
        {
            pinned.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CreateRestrictedToken(
        SafeAccessTokenHandle existing, uint flags,
        int disableSidCount, SidAndAttributes[] sidsToDisable,
        int deletePrivilegeCount, IntPtr privilegesToDelete,
        int restrictedSidCount, IntPtr sidsToRestrict,
        out SafeAccessTokenHandle restricted);
}
