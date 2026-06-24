using Espmon.Service;

using HWKit;

using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;


namespace Espmon;

public class Program
{
    // Backing controller. Same instance is used whether we are running as a
    // console (debug) or a Windows service. All RPC handlers funnel here.
    private static LocalPortController? _controller;
    // Cancellation drives shutdown across both pipe loops and the console
    // wait loop.
    private static readonly CancellationTokenSource _cts = new();

    // One writer per pipe, serialized. Reads are unsynchronized because
    // each pipe has only one reader.
    private static readonly SemaphoreSlim _requestPipeWriteLock = new(1, 1);

    // Set when a client has issued Subscribe; cleared on Unsubscribe or
    // event-pipe disconnect. While false the event pump drops events
    // (or buffers them; see PumpEvent).
    private static volatile NamedPipeServerStream? _pipe;

    private static Task? _requestPipeTask;

    // ---- pipe lifecycle ---------------------------------------------------


    static PipeSecurity BuildSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null), // Everyone
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return security;
    }

    static async Task RequestPipeLoop(LocalPortController controller, CancellationToken ct)
    {
        var security = BuildSecurity();
        while (!ct.IsCancellationRequested)
        {
            // Pipe is created fresh per client connection. The previous code
            // declared this with `using var` inside the outer scope and
            // captured it in a Task -- the dispose ran before the task did.
            NamedPipeServerStream pipe;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(
                    "Espmon.Service",
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    0, 0,
                    security);
                _pipe = pipe;
            }
            catch (Exception ex)
            {
                // If we can't even create the pipe, back off briefly and retry.
                Console.Error.WriteLine($"[Espmon.Service] request pipe create failed: {ex.Message}");
                try { await Task.Delay(500, ct); } catch (OperationCanceledException) { }
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(ct);
                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    (byte cmd, byte[] payload) request;
                    try
                    {
                        request = await PipeFrame.ReadFrameAsync(pipe, ct);
                    }
                    catch (EndOfStreamException) { break; }
                    catch (IOException) { break; }
                    catch (OperationCanceledException) { break; }

                    
                    try
                    {
                        await DispatchAsync(controller, request.cmd, request.payload, ct);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Exception: {ex.Message}");
                    }

                   
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                try { if (pipe.IsConnected) pipe.Disconnect(); } catch { }
                pipe.Dispose();
                _pipe = null;
            }
        }
    }

  

    // ---- framing ----------------------------------------------------------

    

    // ---- dispatch ---------------------------------------------------------

    static async Task DispatchAsync(
        LocalPortController controller, byte cmd, byte[] payload, CancellationToken ct)
    {
        switch ((ServiceCommand)cmd)
        {
            case ServiceCommand.AppStart:
                {
                    if (_pipe != null)
                    {
                        ServiceAppStartRequest.TryRead(payload, out var req, out var _);
                        var resp = new ServiceAppStartResponse();
                        for (var i = 0; i < controller.Sessions.Count; ++i)
                        {
                            var session = controller.Sessions[i];
                            if (session.Status != SessionStatus.Closed && session.Status != SessionStatus.RequiresFlash)
                            {
                                var entry = new ServiceDeviceEntry();
                                entry.SerialNumber = session.SerialNumber;
                                entry.ScreenIndex = session.ScreenIndex;
                                resp.Entries.Add(entry);
                            }
                        }
                        payload = new byte[resp.SizeOfStruct];
                        resp.TryWrite(payload, out var _);
                        controller.Stop();
                        await _requestPipeWriteLock.WaitAsync(ct);
                        try
                        {
                            await PipeFrame.WriteFrameAsync(_pipe, cmd, payload, ct);
                        }
                        finally
                        {
                            _requestPipeWriteLock.Release();
                        }
                        
                    }
                }
                break;
            case ServiceCommand.AppEnd:
                {
                    if (_pipe != null)
                    {
                        ServiceAppStopRequest.TryRead(payload, out var req, out var _);
                        controller.Start();
                        var resp = new ServiceAppStopResponse();
                        payload = new byte[resp.SizeOfStruct];
                        resp.TryWrite(payload, out var _);
                        await _requestPipeWriteLock.WaitAsync(ct);
                        try
                        {
                            await PipeFrame.WriteFrameAsync(_pipe, cmd, payload, ct);
                        }
                        finally
                        {
                            _requestPipeWriteLock.Release();
                        }
                    }
                }
                break;

            default:
                throw new InvalidDataException($"Unknown ServiceCommand 0x{cmd:X2}");
        }
    }

   
  
    // ---- main -------------------------------------------------------------

    public static void Main(string[] args)
    {
        if (Environment.UserInteractive)
        {
            // Console / debug mode. Acts like the service will, just with
            // Ctrl-C handling instead of SCM stop.
            var appPath = AppContext.BaseDirectory;
            var readPath = Path.Combine(AppContext.BaseDirectory, "espmon.service.config.json");
            try
            {
                using var reader = File.OpenText(readPath);
                if (JsonObject.ReadFrom(reader) is JsonObject obj
                    && obj.TryGetValue("app_path", out var value)
                    && value is string str)
                {
                    appPath = str;
                } else
                {
                    throw new InvalidProgramException("The service configuration is invalid");
                }
            }
            catch (FileNotFoundException) { /* run with default path */ }

            using var controller = new LocalPortController(appPath);
            _controller = controller;
            controller.Start();

            _requestPipeTask = Task.Run(() => RequestPipeLoop(controller, _cts.Token), _cts.Token);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _cts.Cancel();
            };

            Console.WriteLine("[Espmon.Service] running in console mode. Ctrl-C to stop.");

            // Wait for either Ctrl-C or any keypress (legacy behavior).
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        Console.ReadKey(true);
                        _cts.Cancel();
                        break;
                    }
                    Thread.Sleep(100);
                }
            }
            catch (InvalidOperationException)
            {
                // Console.KeyAvailable throws when stdin is redirected (e.g.
                // when actually running as a service). Fall back to waiting
                // on the cancellation token alone.
                try { Task.Delay(Timeout.Infinite, _cts.Token).Wait(); }
                catch (AggregateException) { }
            }

            try
            {
                (_requestPipeTask ?? Task.CompletedTask).Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException) { }

            controller.Dispose();
            _controller = null;
            return;
        }

        // Real Windows-service path. Worker is still old/stub code per the
        // current state of the project; the pipe servers will move into the
        // worker (or a hosted service) once that gets revamped. Leaving the
        // existing scaffold in place for now.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "Espmon Hardware Monitor Service";
        });
        LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(builder.Services);
        //builder.Services.AddHostedService<Worker>();
        IHost host = builder.Build();
        host.Run();
    }
}
