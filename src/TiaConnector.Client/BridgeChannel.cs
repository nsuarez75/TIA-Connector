using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TiaConnector.Contracts;

namespace TiaConnector.Client;

/// <summary>
/// Conversación con el puente por dos flujos de texto (en uso real, su entrada y salida estándar). Manda peticiones con
/// un id y reparte lo que llega a la petición de ese id. El puente las atiende en orden, de una en una.
/// </summary>
internal sealed class BridgeChannel : IDisposable
{
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<long, PendingRequest> _pending = new();
    private readonly TaskCompletionSource<Response> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _lastId;
    private volatile bool _closed;

    public BridgeChannel(TextReader input, TextWriter output)
    {
        _input = input;
        _output = output;
        Reader = Task.Run(ReadLoop);
    }

    /// <summary>Termina cuando el puente cierra su salida.</summary>
    public Task Reader { get; }

    /// <summary>Avisos del puente que no son de ninguna petición concreta.</summary>
    public event Action<string>? Warning;

    /// <summary>Espera el mensaje de arranque y comprueba que el puente habla el mismo protocolo.</summary>
    public async Task<Response> WaitReadyAsync(TimeSpan timeout)
    {
        var finished = await Task.WhenAny(_ready.Task, Task.Delay(timeout)).ConfigureAwait(false);
        if (finished != _ready.Task)
            throw new TiaConnectorException(ErrorCode.BridgeExited, "The bridge did not start in time.");
        var ready = await _ready.Task.ConfigureAwait(false);
        if (ready.Protocol != Protocol.Version)
            throw new TiaConnectorException(ErrorCode.ProtocolMismatch,
                $"The bridge speaks protocol {ready.Protocol}; this client speaks {Protocol.Version}.");
        return ready;
    }

    public async Task<T> SendAsync<T>(string operation, object? args, IProgress<ProgressInfo>? progress, CancellationToken cancellation)
    {
        if (_closed)
            throw new TiaConnectorException(ErrorCode.BridgeExited, "The bridge is not running.");

        long id = Interlocked.Increment(ref _lastId);
        var pending = new PendingRequest(progress);
        _pending[id] = pending;
        var request = new Request { Id = id, Op = operation, Args = args == null ? null : Protocol.ToElement(args) };

        await _writeLock.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(Protocol.ToLine(request) + "\n").ConfigureAwait(false);
            await _output.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException e)
        {
            _pending.TryRemove(id, out _);
            throw new TiaConnectorException(ErrorCode.BridgeExited, "The bridge is not running.", e);
        }
        finally
        {
            _writeLock.Release();
        }

        // Cancelar solo deja de esperar: el puente terminará la operación igualmente (Openness no se puede parar).
        using (cancellation.Register(() => pending.Completion.TrySetCanceled()))
        {
            var data = await pending.Completion.Task.ConfigureAwait(false);
            return Protocol.FromElement<T>(data);
        }
    }

    private async Task ReadLoop()
    {
        try
        {
            string? line;
            while ((line = await _input.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                if (line.Trim().Length > 0)
                    Dispatch(line);
            }
        }
        catch (Exception e) when (e is IOException || e is ObjectDisposedException)
        {
            // El puente se ha ido: se trata igual que el final de la salida.
        }
        finally
        {
            _closed = true;
            var exited = new TiaConnectorException(ErrorCode.BridgeExited, "The bridge has stopped.");
            _ready.TrySetException(exited);
            foreach (var pending in _pending.Values)
                pending.Completion.TrySetException(exited);
            _pending.Clear();
        }
    }

    private void Dispatch(string line)
    {
        Response response;
        try
        {
            response = Protocol.FromLine<Response>(line);
        }
        catch (JsonException)
        {
            Warning?.Invoke($"Unreadable message from the bridge: {line}");
            return;
        }

        if (response.Id == null)
        {
            // Sin id: el arranque, o un error del puente que no es de ninguna petición (no pudo leerla, no hay TIA…).
            if (response.Kind == MessageKind.Ready)
                _ready.TrySetResult(response);
            else if (response.Kind == MessageKind.Error && !_ready.Task.IsCompleted)
                _ready.TrySetException(ToException(response));
            else
                Warning?.Invoke(response.Text ?? response.Kind.ToString());
            return;
        }

        if (!_pending.TryGetValue(response.Id.Value, out var pending))
            return;
        switch (response.Kind)
        {
            case MessageKind.Progress when response.Progress != null:
                pending.Progress?.Report(response.Progress);
                break;
            case MessageKind.Warning:
                Warning?.Invoke(response.Text ?? "");
                break;
            case MessageKind.Result:
                _pending.TryRemove(response.Id.Value, out _);
                pending.Completion.TrySetResult(response.Data);
                break;
            case MessageKind.Error:
                _pending.TryRemove(response.Id.Value, out _);
                pending.Completion.TrySetException(ToException(response));
                break;
        }
    }

    private static TiaConnectorException ToException(Response response) =>
        new(response.Code ?? ErrorCode.Internal, response.Text ?? "");

    public void Dispose()
    {
        _closed = true;
        _output.Dispose();
        _writeLock.Dispose();
    }

    private sealed class PendingRequest
    {
        public PendingRequest(IProgress<ProgressInfo>? progress)
        {
            Progress = progress;
        }

        public IProgress<ProgressInfo>? Progress { get; }
        public TaskCompletionSource<JsonElement?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
