using System.IO.Pipes;
using Elynt.Core.Repair;

namespace Elynt.Core.Ipc;

/// <summary>The UI's side of the pipe: one async method per <see cref="IEngine"/> command.</summary>
public sealed class EnginePipeClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    private EnginePipeClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _reader = new StreamReader(pipe, leaveOpen: true);
        _writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
    }

    public static async Task<EnginePipeClient> ConnectAsync(
        string pipeName = EngineProtocol.PipeName,
        TimeSpan? timeout = null,
        CancellationToken cancellation = default)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeout ?? TimeSpan.FromSeconds(5), cancellation);
            return new EnginePipeClient(pipe);
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<TweakView>> ListTweaksAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListTweaksRequest(), cancellation)).Tweaks ?? throw Missing("tweaks");

    public async Task<CommandResult> ApplyAsync(string tweakId, CancellationToken cancellation = default) =>
        (await SendAsync(new ApplyRequest(tweakId), cancellation)).Result ?? throw Missing("result");

    public async Task<CommandResult> RevertAsync(string tweakId, CancellationToken cancellation = default) =>
        (await SendAsync(new RevertRequest(tweakId), cancellation)).Result ?? throw Missing("result");

    public async Task<IReadOnlyList<RepairToolView>> ListRepairToolsAsync(CancellationToken cancellation = default) =>
        (await SendAsync(new ListRepairToolsRequest(), cancellation)).RepairTools ?? throw Missing("repair tools");

    /// <summary>
    /// Runs a repair tool; <paramref name="progress"/> gets every progress line as it arrives. Holds this connection
    /// until the tool finishes, so the UI uses a second connection for other commands meanwhile.
    /// </summary>
    public async Task<RepairResult> RunRepairAsync(
        string toolId, bool confirmed, IProgress<RepairProgress>? progress = null, CancellationToken cancellation = default) =>
        (await SendAsync(new RunRepairRequest(toolId, confirmed), cancellation, progress)).RepairResult ?? throw Missing("repair result");

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        await _writer.DisposeAsync();
        await _pipe.DisposeAsync();
    }

    private async Task<EngineResponse> SendAsync(
        EngineRequest request, CancellationToken cancellation, IProgress<RepairProgress>? progress = null)
    {
        await _oneAtATime.WaitAsync(cancellation);
        try
        {
            await _writer.WriteLineAsync(EngineProtocol.Serialize(request).AsMemory(), cancellation);
            EngineResponse response;
            while (true)
            {
                var line = await _reader.ReadLineAsync(cancellation) ?? throw new IOException("The service closed the connection.");
                response = EngineProtocol.Deserialize<EngineResponse>(line);
                if (response.RepairProgress is not { } interim)
                    break;
                progress?.Report(interim);
            }
            if (response.Error is not null)
                throw new EngineServiceException(response.Error);
            return response;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private static EngineServiceException Missing(string what) => new($"The service's reply had no {what}.");
}

public sealed class EngineServiceException(string message) : Exception(message);
