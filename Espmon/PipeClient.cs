using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace Espmon;

/// <summary>
/// Client side of the Espmon service named-pipe protocol. Owns both pipes
/// (request/response and events) and exposes a serialized RPC method plus
/// a callback for inbound events. Single in-flight call at a time.
/// </summary>
internal sealed class PipeClient : IDisposable
{
    private readonly SemaphoreSlim _rpcLock = new(1, 1);
    private NamedPipeClientStream? _requestPipe;
    private NamedPipeClientStream? _eventPipe;
    private CancellationTokenSource? _eventLoopCts;
    private Task? _eventLoopTask;

    /// <summary>
    /// Raised on the event-loop task. Handlers run on a thread-pool thread;
    /// marshal to a UI sync context inside the handler if needed.
    /// </summary>
    public event Action<byte, byte[]>? EventReceived;

    /// <summary>
    /// Raised when the event pipe disconnects unexpectedly. The owning
    /// PortController can use this to surface a "lost service connection"
    /// state. Not raised on a clean Dispose.
    /// </summary>
    public event Action? Disconnected;

    public bool IsConnected =>
        _requestPipe is { IsConnected: true } && _eventPipe is { IsConnected: true };

    public async Task ConnectAsync(int timeoutMs, CancellationToken ct)
    {
        // Order matters: connect the event pipe first so we don't miss
        // events that the service emits in response to the Subscribe call.
        var ev = new NamedPipeClientStream(".", "Espmon.Service.Events",
            PipeDirection.In, PipeOptions.Asynchronous);
        await ev.ConnectAsync(timeoutMs, ct);
        _eventPipe = ev;

        var req = new NamedPipeClientStream(".", "Espmon.Service",
            PipeDirection.InOut, PipeOptions.Asynchronous);
        await req.ConnectAsync(timeoutMs, ct);
        _requestPipe = req;

        _eventLoopCts = new CancellationTokenSource();
        _eventLoopTask = Task.Run(() => EventLoop(_eventLoopCts.Token));
    }

    /// <summary>
    /// Send a request, return the response payload. Throws on framing
    /// errors or pipe disconnect; the caller should treat those as fatal
    /// for the current controller session.
    /// </summary>
    public async Task<(byte Cmd, byte[] Payload)> RpcAsync(byte cmd, byte[]? payload, CancellationToken ct)
    {
        if (_requestPipe is null) throw new InvalidOperationException("Not connected.");

        await _rpcLock.WaitAsync(ct);
        try
        {
            if (payload != null)
            {
                await WriteFrameAsync(_requestPipe, cmd, payload, ct);
            }
            return await ReadFrameAsync(_requestPipe, ct);
        }
        finally
        {
            _rpcLock.Release();
        }
    }
    public (byte Cmd, byte[] Payload) Rpc(byte cmd, byte[]? payload, CancellationToken ct)
    {
        if (_requestPipe is null) throw new InvalidOperationException("Not connected.");

        _rpcLock.Wait(ct);
        try
        {
            if (payload != null)
            {
                WriteFrame(_requestPipe, cmd, payload);
            }
            return ReadFrame(_requestPipe);
        }
        finally
        {
            _rpcLock.Release();
        }
    }
    private async Task EventLoop(CancellationToken ct)
    {
        var pipe = _eventPipe!;
        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                (byte cmd, byte[] payload) frame;
                try
                {
                    frame = await ReadFrameAsync(pipe, ct);
                }
                catch (EndOfStreamException) { break; }
                catch (IOException) { break; }
                catch (OperationCanceledException) { break; }

                try { EventReceived?.Invoke(frame.cmd, frame.payload); }
                catch { /* never let a handler kill the loop */ }
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                Disconnected?.Invoke();
        }
    }

    // Same wire format as the service: [cmd:1][len:4 LE][payload:len].

    private static async Task<(byte Cmd, byte[] Payload)> ReadFrameAsync(PipeStream pipe, CancellationToken ct)
    {
        var header = new byte[5];
        await pipe.ReadExactlyAsync(header, ct);
        var cmd = header[0];
        var len = BitConverter.ToInt32(header, 1);
        if (len < 0 || len > 64 * 1024 * 1024)
            throw new InvalidDataException($"Invalid frame length {len}");
        var buf = new byte[len];
        if (len > 0)
            await pipe.ReadExactlyAsync(buf, ct);
        return (cmd, buf);
    }

    private static async Task WriteFrameAsync(PipeStream pipe, byte cmd, byte[]? payload, CancellationToken ct)
    {
        var len = payload?.Length ?? 0;
        var header = new byte[5];
        header[0] = cmd;
        BitConverter.GetBytes(len).CopyTo(header, 1);
        await pipe.WriteAsync(header, ct);
        if (len > 0)
            await pipe.WriteAsync(payload!, ct);
        await pipe.FlushAsync(ct);
    }

    private static (byte Cmd, byte[] Payload) ReadFrame(PipeStream pipe)
    {
        var header = new byte[5];
        pipe.ReadExactly(header);
        var cmd = header[0];
        var len = BitConverter.ToInt32(header, 1);
        if (len < 0 || len > 64 * 1024 * 1024)
            throw new InvalidDataException($"Invalid frame length {len}");
        var buf = new byte[len];
        if (len > 0)
            pipe.ReadExactly(buf);
        return (cmd, buf);
    }

    private static void WriteFrame(PipeStream pipe, byte cmd, byte[]? payload)
    {
        var len = payload?.Length ?? 0;
        var header = new byte[5];
        header[0] = cmd;
        BitConverter.GetBytes(len).CopyTo(header, 1);
        pipe.Write(header);
        if (len > 0)
            pipe.Write(payload!);
        pipe.Flush();
    }

    public void Dispose()
    {
        try { _eventLoopCts?.Cancel(); } catch { }
        try { _eventLoopTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _eventLoopCts?.Dispose();
        _requestPipe?.Dispose();
        _eventPipe?.Dispose();
        _rpcLock.Dispose();
    }
}
