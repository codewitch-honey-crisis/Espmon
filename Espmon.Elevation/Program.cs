using Espmon;

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Espmon.Elevation;
static class Program
{
    static async Task Main()
    {

        
        try
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null), // Everyone
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            using var pipe = NamedPipeServerStreamAcl.Create(
                "Espmon.Elevation",
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0, 0,
                security);

            while (true)
            {
                await pipe.WaitForConnectionAsync();

                while (pipe.IsConnected)
                {
                    try
                    {
                        var request = await ReadAsync(pipe);
                        if (request.Payload is null || request.Cmd == (byte)ElevatedCommand.Shutdown)
                            return;
                        var response = await Dispatch(request.Cmd, request.Payload);
                        await WriteAsync(pipe, response.Cmd, response.Payload);
                    }
                    catch (EndOfStreamException) { break; }
                }

                pipe.Disconnect();
            }
        }
        catch 
        {
            
        }

    }

    static async Task<(byte Cmd, byte[] Payload)> Dispatch(byte cmd, byte[] payload)
    {
        var resp = new RequestResponse();
        resp.Succeeded = true;
        try
        {
            switch ((ElevatedCommand)cmd)
            {
                case ElevatedCommand.InstallService:
                    {
                        if (InstallRequest.TryRead(payload, out var req, out _))
                        {
                            var obj = new JsonObject();
                            obj.Add("app_path", req.FromAppPath);
                            var writePath = Path.Combine(AppContext.BaseDirectory, "espmon.service.config.json");
                            try
                            {
                                File.Delete(writePath);
                            }
                            catch { }
                            using (var writer = new StreamWriter(File.OpenWrite(writePath),Encoding.UTF8)) {
                                obj.WriteTo(writer);
                            }
                            var svcPath = Path.Combine(AppContext.BaseDirectory, "Espmon.Service.exe");
                            await WindowsServiceManager.InstallAsync(svcPath);
                        }
                        else
                            throw new InvalidDataException("Could not deserialize InstallRequest.");
                    }
                    break;

                case ElevatedCommand.UninstallService:
                    {
                        if (UninstallRequest.TryRead(payload, out _, out _))
                            await WindowsServiceManager.UninstallAsync();
                        else
                            throw new InvalidDataException("Could not deserialize UninstallRequest.");
                    }
                    break;

                case ElevatedCommand.StartService:
                    {
                        if (StartRequest.TryRead(payload, out _, out _))
                        {
                            
                            await WindowsServiceManager.StartAsync();
                        }
                        else
                            throw new InvalidDataException("Could not deserialize StartRequest.");
                    }
                    break;

                case ElevatedCommand.StopService:
                    {
                        if (StopRequest.TryRead(payload, out _, out _))
                            await WindowsServiceManager.StopAsync();
                        else
                            throw new InvalidDataException("Could not deserialize StopRequest.");
                    }
                    break;
                case ElevatedCommand.StartedCheck:
                    {
                        if (StartedCheck.TryRead(payload, out _, out _))
                            if(!WindowsServiceManager.IsStarted)
                            {
                                resp.Succeeded = false;
                            }
                        else
                            throw new InvalidDataException("Could not deserialize StopRequest.");
                    }
                    break;
                case ElevatedCommand.InstalledCheck:
                    {
                        if (InstalledCheck.TryRead(payload, out _, out _))
                            if (!WindowsServiceManager.IsInstalled)
                            {
                                resp.Succeeded = false;
                            }
                            else
                                throw new InvalidDataException("Could not deserialize StopRequest.");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            resp.Succeeded = false;
            resp.ErrorCode = 1; // reserved
            resp.Message = ex.Message;
        }

        var data = new byte[resp.SizeOfStruct];
        if (resp.TryWrite(data, out _))
            return (cmd, data);

        // shouldn't happen:
        throw new IOException("Could not serialize response");
    }

    static async Task<(byte Cmd, byte[] Payload)> ReadAsync(PipeStream pipe)
    {
        // read 1 byte cmd prefix, 4-byte length prefix, then the payload
        var lenBuf = new byte[5];
        await pipe.ReadExactlyAsync(lenBuf);
        var cmd = lenBuf[0];
        var len = BitConverter.ToInt32(lenBuf, 1);
        var buf = new byte[len];
        await pipe.ReadExactlyAsync(buf);
        return (cmd, buf);
    }

    static async Task WriteAsync(PipeStream pipe, byte cmd, byte[]? payload)
    {
        // write 1 byte cmd prefix, 4-byte length prefix, then the payload
        byte[] frameBuf = [cmd, .. BitConverter.GetBytes(payload != null ? payload.Length : 0)];
        await pipe.WriteAsync(frameBuf);
        await pipe.WriteAsync(payload);
        await pipe.FlushAsync();
    }
}