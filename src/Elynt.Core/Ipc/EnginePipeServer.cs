using System.IO.Pipes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elynt.Core.Ipc;

/// <summary>
/// Serves <see cref="IEngine"/> commands over a named pipe, from all clients, in two lanes. Repairs run for
/// minutes, so they take turns in their own lane and never hold up tweak commands. Every other command takes turns
/// in the main lane. An app install queue takes both lanes, so nothing else runs while apps install and no repair's
/// winget runs alongside it. Only administrator accounts are served; any other account's requests are all refused,
/// and no engine is ever built for it.
/// </summary>
/// <param name="createPipe">Creates the next listening pipe instance. The service passes one restricted to the interactive user.</param>
/// <param name="engineFor">Builds the engine for a connected client, typically for that client's user. Called once per connection, after its first request, and only for an administrator account.</param>
/// <param name="administrators">Tells whether a connected client's account is an administrator account.</param>
/// <param name="commandLane">The main lane, shared with the service's own work (per-game profiles) so they take turns too.</param>
public sealed class EnginePipeServer(
    Func<NamedPipeServerStream> createPipe,
    Func<NamedPipeServerStream, IEngine> engineFor,
    IAdministratorCheck administrators,
    ILogger<EnginePipeServer>? logger = null,
    CommandLane? commandLane = null)
{
    /// <summary>The whole reply to an account that isn't an administrator account. The window shows it as it is.</summary>
    private const string StandardAccountRefusal =
        "Only an administrator account can use Elynt's Windows Optimizer. Sign in with one, or ask whoever looks after this PC. Nothing was changed.";

    private readonly ILogger _log = logger ?? NullLogger<EnginePipeServer>.Instance;
    private readonly SemaphoreSlim _commandLane = (commandLane ?? new CommandLane()).Semaphore;
    private readonly SemaphoreSlim _repairLane = new(1, 1);

    public async Task RunAsync(CancellationToken cancellation)
    {
        var clients = new List<Task>();
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var pipe = createPipe();
                try
                {
                    await pipe.WaitForConnectionAsync(cancellation);
                }
                catch
                {
                    await pipe.DisposeAsync();
                    throw;
                }
                clients.RemoveAll(t => t.IsCompleted);
                clients.Add(ServeAsync(pipe, cancellation));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        await Task.WhenAll(clients);
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken cancellation)
    {
        await using var _ = pipe;
        try
        {
            // The account is checked and the engine built after the first request arrives: Windows only lets the
            // server impersonate the client (to learn who it is) once it has read from the pipe.
            bool? administrator = null;
            IEngine? engine = null;
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            while (await reader.ReadLineAsync(cancellation) is { } line)
            {
                administrator ??= IsAdministratorAccount(pipe);
                if (administrator == false)
                {
                    // Not even read: nothing runs for this account.
                    await writer.WriteLineAsync(EngineProtocol.Serialize(new EngineResponse(Refused: StandardAccountRefusal)).AsMemory(), cancellation);
                    continue;
                }
                engine ??= engineFor(pipe);
                var response = await HandleAsync(engine, line, writer, cancellation);
                await writer.WriteLineAsync(EngineProtocol.Serialize(response).AsMemory(), cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (IOException ex)
        {
            _log.LogInformation(ex, "A client disconnected");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Serving a client failed");
        }
    }

    private bool IsAdministratorAccount(NamedPipeServerStream pipe)
    {
        var administrator = administrators.IsAdministratorAccount(pipe);
        if (!administrator)
            _log.LogWarning("Refused a connection: the account isn't an administrator account");
        return administrator;
    }

    /// <summary>
    /// Runs one command. Events it raises (progress) are written to the client as they happen, before the response.
    /// A command keeps running to the end even if the client goes away, so commands in a lane never overlap.
    /// </summary>
    private async Task<EngineResponse> HandleAsync(IEngine engine, string line, StreamWriter writer, CancellationToken cancellation)
    {
        EngineRequest request;
        try
        {
            request = EngineProtocol.Deserialize<EngineRequest>(line);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Couldn't read a command");
            return new EngineResponse(Error: ex.Message);
        }

        SemaphoreSlim[] lanes =
            EngineProtocol.IsLongRunning(request) ? [_repairLane] :
            EngineProtocol.IsExclusive(request) ? [_repairLane, _commandLane] :
            [_commandLane];
        var entered = 0;
        try
        {
            foreach (var lane in lanes)
            {
                await lane.WaitAsync(cancellation);
                entered++;
            }
            _log.LogInformation("Command {Request}", request);

            var events = Channel.CreateUnbounded<EngineEvent>(new UnboundedChannelOptions { SingleReader = true });
            var work = Task.Run(async () =>
            {
                try
                {
                    return await EngineProtocol.DispatchAsync(engine, request, e => events.Writer.TryWrite(e), cancellation);
                }
                finally
                {
                    events.Writer.TryComplete();
                }
            }, CancellationToken.None);

            var clientListening = true;
            await foreach (var e in events.Reader.ReadAllAsync(CancellationToken.None))
            {
                if (!clientListening)
                    continue;
                try
                {
                    await writer.WriteLineAsync(EngineProtocol.Serialize(new EngineResponse(Event: e)).AsMemory(), cancellation);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    _log.LogInformation(ex, "The client stopped listening to progress; the command carries on");
                    clientListening = false;
                }
            }
            return await work;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Command failed");
            return new EngineResponse(Error: ex.Message);
        }
        finally
        {
            for (var i = entered - 1; i >= 0; i--)
                lanes[i].Release();
        }
    }
}
