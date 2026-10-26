using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Elynt.Core.Ipc;
using Microsoft.Win32.SafeHandles;

namespace Elynt.Windows;

/// <summary>
/// Whether the account that opened the service's pipe is an administrator account, read from the token Windows gave
/// it at sign-in: Administrators is among the token's groups, whether enabled or deny-only.
/// </summary>
/// <remarks>
/// Why not "is it elevated" or <see cref="WindowsPrincipal.IsInRole(WindowsBuiltInRole)"/>: the window runs without
/// admin rights (ADR 0001), and under UAC an administrator account's unelevated token keeps Administrators only as a
/// deny-only group. IsInRole, like CheckTokenMembership on that token, skips deny-only groups, so it would refuse every
/// administrator's window. The token's own group list still names the group, and only a sign-in can put a group in a
/// token (a program can only turn one it already has to deny-only), so the group being there at all means the account
/// is a member. That is the answer the account's elevated (linked) token would give, without the extra steps to get
/// that token, and it already counts membership through nested or domain groups, which Windows resolved at sign-in.
/// Like UAC, it sees a change of membership from the account's next sign-in.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsAdministratorCheck : IAdministratorCheck
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public bool IsAdministratorAccount(NamedPipeServerStream client)
    {
        // Windows says who is connected only to a server impersonating the client: its token is then the client's own.
        WindowsIdentity? account = null;
        client.RunAsClient(() => account = WindowsIdentity.GetCurrent(TokenAccessLevels.Query));
        using (account)
            return IsMember(account ?? throw new InvalidOperationException("Couldn't tell which account is connected."));
    }

    /// <summary>Whether the account's token lists Administrators among its groups, enabled or deny-only.</summary>
    public static bool IsMember(WindowsIdentity account) => Groups(account.AccessToken).Contains(Administrators);

    private static List<SecurityIdentifier> Groups(SafeAccessTokenHandle token)
    {
        // The first call only asks how big the list is.
        Native.GetTokenInformation(token, Native.TokenGroups, IntPtr.Zero, 0, out var size);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!Native.GetTokenInformation(token, Native.TokenGroups, buffer, size, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't read the connected account's groups.");
            var count = Marshal.ReadInt32(buffer);
            var first = buffer + Marshal.OffsetOf<Native.TokenGroupsHead>(nameof(Native.TokenGroupsHead.First)).ToInt32();
            var step = Marshal.SizeOf<Native.SidAndAttributes>();
            var groups = new List<SecurityIdentifier>(count);
            for (var i = 0; i < count; i++)
                groups.Add(new SecurityIdentifier(Marshal.PtrToStructure<Native.SidAndAttributes>(first + i * step).Sid));
            return groups;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static class Native
    {
        public const int TokenGroups = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct SidAndAttributes
        {
            public IntPtr Sid;
            public uint Attributes;
        }

        /// <summary>TOKEN_GROUPS: the count, then that many <see cref="SidAndAttributes"/> starting at <see cref="First"/>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct TokenGroupsHead
        {
            public uint GroupCount;
            public SidAndAttributes First;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, IntPtr information, int length, out int returnLength);
    }
}
