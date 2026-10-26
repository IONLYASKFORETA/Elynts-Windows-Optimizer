using System.IO.Pipes;

namespace Elynt.Core.Ipc;

/// <summary>
/// Whether the Windows account on the other end of a connection is an administrator account (in the Administrators
/// group). The service serves only those (ADR 0007): through it, any other account could make changes Windows itself
/// would stop that account making, like turning off Defender or changing boot settings.
/// </summary>
public interface IAdministratorCheck
{
    /// <summary>
    /// Asked once per connection, when its first request has arrived: only then can Windows tell the service who is
    /// on the other end.
    /// </summary>
    bool IsAdministratorAccount(NamedPipeServerStream client);
}
