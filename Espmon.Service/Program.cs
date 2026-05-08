using Espmon.Service;

using HWKit;

using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Espmon;

public class Program
{
    private static Task? _pipeTask, _eventPipeTask;
    private static bool _closing = false;
    static void Init()
    {

        if(_closing)
        {
            return;
        }
        try
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null), // Everyone
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            using var pipe = NamedPipeServerStreamAcl.Create(
                "Espmon.Service",
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0, 0,
                security);
            
            _pipeTask = Task.Factory.StartNew(async () =>
            {
                while (!_closing)
                {
                    await pipe.WaitForConnectionAsync();

                    while (!_closing&& pipe.IsConnected)
                    {
                        try
                        {
                            var request = await ReadAsync(pipe);
                            if (request.Payload is null || request.Cmd == (byte)ServiceCommand.StopService)
                                return;
                        }
                        catch (EndOfStreamException) { break; }
                    }

                    pipe.Disconnect();
                }
            }, default, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            _eventPipeTask = Task.Run(async () =>
            {
                using var eventPipe = NamedPipeServerStreamAcl.Create(
                    "Espmon.Service.Events",
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0, 0,
                    security);

                while (!_closing)
                {
                    await eventPipe.WaitForConnectionAsync();

                    while (!_closing && eventPipe.IsConnected)
                    {
                        await Task.Delay(1000);
                    }

                    eventPipe.Disconnect();
                }
            });

        }
        catch
        {

        }

    }
    static void Teardown()
    {
        _closing = true;
        Thread.MemoryBarrier();
        if (_pipeTask != null && _eventPipeTask != null)
        {
            var tasks = new Task[2];
            int taskCount = 0;
            if (_pipeTask.Status == TaskStatus.Running)
            {
                tasks[taskCount++] = _pipeTask;
            }
            if (_eventPipeTask.Status == TaskStatus.Running)
            {
                tasks[taskCount++] = _eventPipeTask;
            }
            if (taskCount > 0)
            {
                Task.WaitAll(tasks.AsSpan(0, taskCount));
            }
        }
    }

    static async Task<(byte Cmd, byte[] Payload)> Dispatch(byte cmd, byte[] payload)
    {
        //var resp = new RequestResponse();
        //resp.Succeeded = true;
        //try
        //{
        //    switch ((ElevatedCommand)cmd)
        //    {
        //        case ElevatedCommand.InstallService:
        //            {
        //                if (InstallRequest.TryRead(payload, out var req, out _))
        //                {
        //                    var obj = new JsonObject();
        //                    obj.Add("app_path", req.FromAppPath);
        //                    var writePath = Path.Combine(AppContext.BaseDirectory, "espmon.service.config.json");
        //                    try
        //                    {
        //                        File.Delete(writePath);
        //                    }
        //                    catch { }
        //                    using (var writer = new StreamWriter(File.OpenWrite(writePath), Encoding.UTF8))
        //                    {
        //                        obj.WriteTo(writer);
        //                    }
        //                    var svcPath = Path.Combine(AppContext.BaseDirectory, "Espmon.Service.exe");
        //                    await WindowsServiceManager.InstallAsync(svcPath);
        //                }
        //                else
        //                    throw new InvalidDataException("Could not deserialize InstallRequest.");
        //            }
        //            break;

        //        case ElevatedCommand.UninstallService:
        //            {
        //                if (UninstallRequest.TryRead(payload, out _, out _))
        //                    await WindowsServiceManager.UninstallAsync();
        //                else
        //                    throw new InvalidDataException("Could not deserialize UninstallRequest.");
        //            }
        //            break;

        //        case ElevatedCommand.StartService:
        //            {
        //                if (StartRequest.TryRead(payload, out _, out _))
        //                {

        //                    await WindowsServiceManager.StartAsync();
        //                }
        //                else
        //                    throw new InvalidDataException("Could not deserialize StartRequest.");
        //            }
        //            break;

        //        case ElevatedCommand.StopService:
        //            {
        //                if (StopRequest.TryRead(payload, out _, out _))
        //                    await WindowsServiceManager.StopAsync();
        //                else
        //                    throw new InvalidDataException("Could not deserialize StopRequest.");
        //            }
        //            break;
        //        case ElevatedCommand.StartedCheck:
        //            {
        //                if (StartedCheck.TryRead(payload, out _, out _))
        //                    if (!WindowsServiceManager.IsStarted)
        //                    {
        //                        resp.Succeeded = false;
        //                    }
        //                    else
        //                        throw new InvalidDataException("Could not deserialize StopRequest.");
        //            }
        //            break;
        //        case ElevatedCommand.InstalledCheck:
        //            {
        //                if (InstalledCheck.TryRead(payload, out _, out _))
        //                    if (!WindowsServiceManager.IsInstalled)
        //                    {
        //                        resp.Succeeded = false;
        //                    }
        //                    else
        //                        throw new InvalidDataException("Could not deserialize StopRequest.");
        //            }
        //            break;
        //    }
        //}
        //catch (Exception ex)
        //{
        //    resp.Succeeded = false;
        //    resp.ErrorCode = 1; // reserved
        //    resp.Message = ex.Message;
        //}

        //var data = new byte[resp.SizeOfStruct];
        //if (resp.TryWrite(data, out _))
        //    return (cmd, data);

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
    public static void Main(string[] args)
    {
        
        if (!Environment.UserInteractive)
        {
            
            var appPath = AppContext.BaseDirectory;
            var readPath = Path.Combine(AppContext.BaseDirectory, "espmon.service.config.json");
            using (var reader = File.OpenText(readPath))
            {
                var obj = JsonObject.ReadFrom(reader) as JsonObject;
                if (obj != null)
                {
                    if (obj.TryGetValue("app_path", out var value) && value is string str)
                    {
                        appPath = str;
                    }
                }
            }
            using var _controller = new LocalPortController(appPath);
            _controller.Start();
            while (!Console.KeyAvailable)
            {
                Thread.Sleep(100);
            }
            _controller.Stop();
            return;
        }
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "Espmon Hardware Monitor Service";
        });
        
        LoggerProviderOptions.RegisterProviderOptions<
            EventLogSettings, EventLogLoggerProvider>(builder.Services);
        builder.Services.AddHostedService<Worker>();

        IHost host = builder.Build();
        host.Run();
    }
}

