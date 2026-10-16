using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elynt.Core.Ipc;

/// <summary>
/// Serves <see cref="IEngine"/> commands over a named pipe. Commands from all clients run one at a time.
/// </summary>
/// <param name="createPipe">Creates the next listening pipe instance. The service passes one restricted to the interactive user.</param>
/// <param name="engineFor">Builds the engine for a connected client, typically for that client's user. Called once per connection, after its first request.</param>
public sealed class EnginePipeServer(
    Func<NamedPipeServerStream> createPipe,
    Func<NamedPipeServerStream, IEngine> engineFor,
    ILogger<EnginePipeServer>? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger<EnginePipeServer>.Instance;
    private readonly SemaphoreSlim _oneCommandAtATime = new(1, 1);

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
            // The engine is built after the first request arrives: Windows only lets the server impersonate the
            // client (to learn who it is) once it has read from the pipe.
            IEngine? engine = null;
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            while (await reader.ReadLineAsync(cancellation) is { } line)
            {
                engine ??= engineFor(pipe);
                var response = await HandleAsync(engine, line, cancellation);
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

    private async Task<EngineResponse> HandleAsync(IEngine engine, string line, CancellationToken cancellation)
    {
        await _oneCommandAtATime.WaitAsync(cancellation);
        try
        {
            var request = EngineProtocol.Deserialize<EngineRequest>(line);
            _log.LogInformation("Command {Request}", request);
            return EngineProtocol.Dispatch(engine, request);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Command failed");
            return new EngineResponse(Error: ex.Message);
        }
        finally
        {
            _oneCommandAtATime.Release();
        }
    }
}
