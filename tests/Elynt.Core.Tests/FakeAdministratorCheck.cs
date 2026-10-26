using System.IO.Pipes;
using Elynt.Core.Ipc;

namespace Elynt.Core.Tests;

/// <summary>
/// The service's question about whoever connects, answered by the test: an administrator account unless the test
/// says it is a standard one.
/// </summary>
public sealed class FakeAdministratorCheck : IAdministratorCheck
{
    public bool ConnectingAccountIsAdministrator { get; set; } = true;

    public bool IsAdministratorAccount(NamedPipeServerStream client) => ConnectingAccountIsAdministrator;
}
