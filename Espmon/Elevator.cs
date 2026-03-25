using Espmon;

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;


sealed class Elevator : IAsyncDisposable, IDisposable
{
    bool _launched = false;
    private NamedPipeClientStream? _pipe;
    private readonly SemaphoreSlim _lock = new(1, 1);
    public bool IsConnected
    {
        get
        {
            return _launched && _pipe!=null && _pipe.IsConnected;
        }
    }
    public Task ConnectAsync()
    {
        return EnsureConnectedAsync();
    }
    public void Connect()
    {
        EnsureConnected();
    }
    private async Task<RequestResponse> SendAsync(InstallRequest req)
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureConnectedAsync();
            var len = req.SizeOfStruct;
            var ba = new byte[len];
            if (req.TryWrite(ba, out _))
            {
                await WriteAsync(_pipe!, (byte)ElevatedCommand.InstallService,ba);
                var res = await ReadAsync(_pipe!);
                if(RequestResponse.TryRead(res.Payload,out var resp, out _))
                {
                    return resp;
                }
            }
            throw new InvalidDataException("The operation could not be transmitted or received");

        }
        finally
        {
            _lock.Release();
        }
    }
  
    private async Task<RequestResponse> SendAsync(UninstallRequest req)
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureConnectedAsync();
            var len = req.SizeOfStruct;
            var ba = new byte[len];
            if (req.TryWrite(ba, out _))
            {
                await WriteAsync(_pipe!, (byte)ElevatedCommand.UninstallService, ba);
                var res = await ReadAsync(_pipe!);
                if (RequestResponse.TryRead(res.Payload, out var resp, out _))
                {
                    return resp;
                }
            }
            throw new InvalidDataException("The operation could not be transmitted or received");
        }
        finally
        {
            _lock.Release();
        }
    }
    private async Task<RequestResponse> SendAsync(StartRequest req)
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureConnectedAsync();
            var len = req.SizeOfStruct;
            var ba = new byte[len];
            if (req.TryWrite(ba, out _))
            {
                await WriteAsync(_pipe!, (byte)ElevatedCommand.StartService, ba);
                var res = await ReadAsync(_pipe!);
                if (RequestResponse.TryRead(res.Payload, out var resp, out _))
                {
                    return resp;
                }
            }
            throw new InvalidDataException("The operation could not be transmitted or received");

        }
        finally
        {
            _lock.Release();
        }
    }
    private async Task<RequestResponse> SendAsync(StopRequest req)
    {
        await _lock.WaitAsync();
        try
        {
            await EnsureConnectedAsync();
            var len = req.SizeOfStruct;
            var ba = new byte[len];
            if (req.TryWrite(ba, out _))
            {
                await WriteAsync(_pipe!, (byte)ElevatedCommand.StopService, ba);
                var res = await ReadAsync(_pipe!);
                if (RequestResponse.TryRead(res.Payload, out var resp, out _))
                {
                    return resp;
                }
            }
            throw new InvalidDataException("The operation could not be transmitted or received");

        }
        finally
        {
            _lock.Release();
        }
    }

    private RequestResponse Send(InstallRequest req)
    {
        _lock.Wait();
        try
        {
            EnsureConnected();
            var len = req.SizeOfStruct;
            var ba = new byte[len];
            if (req.TryWrite(ba, out _))
            {
                Write(_pipe!, (byte)ElevatedCommand.InstallService, ba);
                var res = Read(_pipe!);
                if (RequestResponse.TryRead(res.Payload, out var resp, out _))
                {
                    return resp;
                }
            }
            throw new InvalidDataException("The operation could not be transmitted or received");

        }
        finally
        {
            _lock.Release();
        }
    }

    private RequestResponse Send(UninstallRequest req)
    {
        _lock.Wait();
        try
        {
            EnsureConnected();
            var len = req.SizeOfStruct;
            var ba = new byte[len];
            if (req.TryWrite(ba, out _))
            {
                Write(_pipe!, (byte)ElevatedCommand.UninstallService, ba);
                var res = Read(_pipe!);
                if (RequestResponse.TryRead(res.Payload, out var resp, out _))
                {
                    return resp;
                }
            }
            throw new InvalidDataException("The operation could not be transmitted or received");
        }
        finally
        {
            _lock.Release();
        }
    }
    private RequestResponse Send(StartRequest req)
    {
        _lock.Wait();
        try
        {
            EnsureConnected();
            var len = req.SizeOfStruct;
            var ba = new byte[len];
            if (req.TryWrite(ba, out _))
            {
                Write(_pipe!, (byte)ElevatedCommand.StartService, ba);
                var res = Read(_pipe!);
                if (RequestResponse.TryRead(res.Payload, out var resp, out _))
                {
                    return resp;
                }
            }
            throw new InvalidDataException("The operation could not be transmitted or received");

        }
        finally
        {
            _lock.Release();
        }
    }
    private RequestResponse Send(StopRequest req)
    {
        _lock.Wait();
        try
        {
            EnsureConnected();
            var len = req.SizeOfStruct;
            var ba = new byte[len];
            if (req.TryWrite(ba, out _))
            {
                Write(_pipe!, (byte)ElevatedCommand.StopService, ba);
                var res = Read(_pipe!);
                if (RequestResponse.TryRead(res.Payload, out var resp, out _))
                {
                    return resp;
                }
            }
            throw new InvalidDataException("The operation could not be transmitted or received");

        }
        finally
        {
            _lock.Release();
        }
    }
    public async Task InstallServiceAsync(string appPath)
    {
        var req = new InstallRequest();
        req.FromAppPath = appPath;
        var resp = await SendAsync(req);
        if(!resp.Succeeded)
        {
            throw new InvalidOperationException(resp.Message);
        }
    }
    public bool IsStarted => WindowsServiceManager.IsStarted;
    
    public bool IsInstalled => WindowsServiceManager.IsInstalled;
    public async Task UninstallServiceAsync()
    {
        var req = new UninstallRequest();
        var resp = await SendAsync(req);
        if (!resp.Succeeded)
        {
            throw new InvalidOperationException(resp.Message);
        }
    }
    public async Task StartServiceAsync()
    {
        var req = new StartRequest();
        var resp = await SendAsync(req);
        if (!resp.Succeeded)
        {
            throw new InvalidOperationException(resp.Message);
        }
    }
    public async Task StopServiceAsync()
    {
        var req = new StopRequest();
        var resp = await SendAsync(req);
        if (!resp.Succeeded)
        {
            throw new InvalidOperationException(resp.Message);
        }
    }
    public void InstallService(string appPath)
    {
        var req = new InstallRequest();
        req.FromAppPath = Path.Combine(AppContext.BaseDirectory, "Espmon.Service.exe");
        var resp = Send(req);
        if (!resp.Succeeded)
        {
            throw new InvalidOperationException(resp.Message);
        }
    }
    public void UninstallService()
    {
        var req = new UninstallRequest();
        var resp = Send(req);
        if (!resp.Succeeded)
        {
            throw new InvalidOperationException(resp.Message);
        }
    }
    public void StartService()
    {
        var req = new StartRequest();
        var resp = Send(req);
        if (!resp.Succeeded)
        {
            throw new InvalidOperationException(resp.Message);
        }
    }
    public void StopService()
    {
        var req = new StopRequest();
        var resp = Send(req);
        if (!resp.Succeeded)
        {
            throw new InvalidOperationException(resp.Message);
        }
    }
    private async Task EnsureConnectedAsync()
    {
        if (_launched) return;

        Process.Start(new ProcessStartInfo(Path.Combine(System.AppContext.BaseDirectory,"Espmon.Elevation.exe"))
        {
            Verb = "runas",
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        });

        _pipe = new NamedPipeClientStream(
            ".",
            "Espmon.Elevation",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        Exception? ex = null;
        // retry loop — give the elevated process time to start
        for (int i = 0; i < 10; i++)
        {
            try { await _pipe.ConnectAsync(500); _launched = true; return; }
            catch (Exception e) { ex = e; await Task.Delay(200); }
        }

        if (ex != null) { throw ex; }
        if(_pipe==null || !_pipe.IsConnected) { throw new InvalidOperationException("The pipe could not be created"); }
    }

    private void EnsureConnected()
    {
        if (_launched) return;

        Process.Start(new ProcessStartInfo(Path.Combine(System.AppContext.BaseDirectory, "Espmon.Elevation.exe"))
        {
            Verb = "runas",
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        });

        _pipe = new NamedPipeClientStream(
            ".",
            "Espmon.Elevation",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        Exception? ex = null;
        // retry loop — give the elevated process time to start
        for (int i = 0; i < 10; i++)
        {
            try { _pipe.Connect(500); _launched = true; return; }
            catch (Exception e) { ex = e; Thread.Sleep(200); }
        }

        if (ex != null) { throw ex; }
        if (_pipe == null || !_pipe.IsConnected) { throw new InvalidOperationException("The pipe could not be created"); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pipe?.IsConnected == true)
        {
            
            try { 
                await WriteAsync(_pipe,(byte)ElevatedCommand.Shutdown,null);
            }
            catch { /* best effort */ }
        }
        _pipe?.Dispose();
        _lock.Dispose();
        _pipe = null;
        _launched = false;
    }

    static async Task<(byte Cmd, byte[] Payload)> ReadAsync(PipeStream pipe)
    {
        // read 1 byte cmd prefix, 4-byte length prefix, then the payload
        var lenBuf = new byte[5];
        await pipe.ReadExactlyAsync(lenBuf);
        var cmd = lenBuf[0];
        var len = BitConverter.ToInt32(lenBuf, 1);
        if (len > 0)
        {
            var buf = new byte[len];
            await pipe.ReadExactlyAsync(buf);
            return (cmd, buf);
        } else
        {
            return (cmd, Array.Empty<byte>());
        }
    }
    static (byte Cmd, byte[] Payload) Read(PipeStream pipe)
    {
        // read 1 byte cmd prefix, 4-byte length prefix, then the payload
        Span<byte> lenBuf = stackalloc byte[5];
        pipe.ReadExactly(lenBuf);
        var cmd = lenBuf[0];
        var len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf.Slice(1,4));
        if (len > 0)
        {
            var buf = new byte[len];
            pipe.ReadExactly(buf);
            return (cmd, buf);
        } else
        {
            return (cmd,Array.Empty<byte>());
        }
    }

    static async Task WriteAsync(PipeStream pipe, byte cmd, byte[]? payload)
    {
        // write 1 byte cmd prefix, 4-byte length prefix, then the payload
        byte[] frameBuf = [cmd, .. BitConverter.GetBytes(payload != null ? payload.Length : 0)];
        await pipe.WriteAsync(frameBuf);
        if (payload != null && payload.Length > 0)
        {
            await pipe.WriteAsync(payload);
        }
        await pipe.FlushAsync();
    }
    static void Write(PipeStream pipe, byte cmd, byte[]? payload)
    {
        // write 1 byte cmd prefix, 4-byte length prefix, then the payload
        byte[] frameBuf = [cmd, .. BitConverter.GetBytes(payload != null ? payload.Length : 0)];
        pipe.Write(frameBuf);
        if (payload != null && payload.Length > 0)
        {
            pipe.Write(payload);
        }
        pipe.Flush();
    }
    public void Dispose()
    {
        if (_pipe?.IsConnected == true)
        {

            try
            {
                Write(_pipe, (byte)ElevatedCommand.Shutdown, null);
            }
            catch { /* best effort */ }
        }
        _pipe?.Dispose();
        _lock.Dispose();
    }
}