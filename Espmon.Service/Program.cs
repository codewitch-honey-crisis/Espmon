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
    public static void Main(string[] args)
    {
        // One code path for both modes. AddWindowsService() inspects the
        // environment at runtime and wires up the right lifetime:
        //   * launched by the SCM     -> WindowsServiceLifetime (SCM stop/start)
        //   * launched from a console -> ConsoleLifetime (Ctrl-C to stop)
        // The hosted service below runs identically in either case, so running
        // "as a service" behaves exactly like the old console mode. The only
        // thing that goes away is the legacy "press any key to stop" behavior,
        // since a service can't read keystrokes -- Ctrl-C still works when you
        // run the exe directly for debugging.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "Espmon Hardware Monitor Service";
        });
        //LoggerProviderOptions.RegisterProviderOptions<EventLogSettings, EventLogLoggerProvider>(builder.Services);
        
        builder.Services.AddHostedService<PortControllerService>();

        IHost host = builder.Build();
        host.Run();
    }
}

// The actual work, hosted by the generic host. Owns the LocalPortController and
// the request named pipe. A single instance is created and managed by the host
// in both console and service mode.
public sealed class PortControllerService : BackgroundService
{
    // Backing controller. All RPC handlers funnel here.
    private LocalPortController? _controller;

    // One writer per pipe, serialized. Reads are unsynchronized because each
    // pipe has only one reader.
    private readonly SemaphoreSlim _requestPipeWriteLock = new(1, 1);

    // The currently connected request pipe, or null when no client is attached.
    private volatile NamedPipeServerStream? _pipe;

    // ---- lifecycle --------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Get off the host's start thread so a slow controller startup can't
        // delay the service from reporting "Running" to the SCM.
        await Task.Yield();

        var appPath = ResolveAppPath();

        var controller = new LocalPortController(appPath);
        _controller = controller;

        //Console.Error.Write("Starting port controller");
        controller.Start();

        try
        {
            await RequestPipeLoop(controller, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cancels the stoppingToken passed to ExecuteAsync and waits for the
        // pipe loop to drain before we tear down the controller.
        await base.StopAsync(cancellationToken);

        var controller = _controller;
        if (controller != null)
        {
            //Console.Error.Write("Stopping port controller");
            try { controller.Stop(); } catch { }
            controller.Dispose();
            _controller = null;
        }
    }

    public override void Dispose()
    {
        _controller?.Dispose();
        _requestPipeWriteLock.Dispose();
        base.Dispose();
    }

    // ---- config -----------------------------------------------------------

    static string ResolveAppPath()
    {
        // BaseDirectory (not the current working dir) is used deliberately: a
        // service's working directory is C:\Windows\System32, but the config
        // file sits next to the exe.
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
            }
            else
            {
                throw new InvalidProgramException("The service configuration is invalid");
            }
        }
        catch (FileNotFoundException) { /* run with default path */ }
        return appPath;
    }

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

    async Task RequestPipeLoop(LocalPortController controller, CancellationToken ct)
    {
        var security = BuildSecurity();
        while (!ct.IsCancellationRequested)
        {
            // Pipe is created fresh per client connection. maxNumberOfServerInstances
            // is 1, so only a single client can be connected at a time.
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
                        Console.Error.WriteLine($"Exception: {ex.Message} {ex.StackTrace}");
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

    // ---- dispatch ---------------------------------------------------------

    async Task DispatchAsync(
        LocalPortController controller, byte cmd, byte[] payload, CancellationToken ct)
    {
        switch ((ServiceCommand)cmd)
        {
            case ServiceCommand.AppStart:
                {
                    var pipe = _pipe;
                    if (pipe != null)
                    {
                        //Console.Error.WriteLine("Dispatch app start");
                        ServiceAppStartRequest.TryRead(payload, out var req, out var _);
                        var resp = new ServiceAppStartResponse();
                        var entries = new List<ServiceDeviceEntry>();
                        for (var i = 0; i < controller.Sessions.Count; ++i)
                        {
                            var session = controller.Sessions[i];
                            if (session.Status != SessionStatus.Closed && session.Status != SessionStatus.RequiresFlash)
                            {
                                var entry = new ServiceDeviceEntry();
                                entry.SerialNumber = session.SerialNumber;
                                entry.ScreenIndex = session.ScreenIndex;
                                entries.Add(entry);
                            }
                        }
                        resp.Entries = entries.ToArray();
                        payload = new byte[resp.SizeOfStruct];
                        resp.TryWrite(payload, out var _);
                        //Console.Error.Write("Stopping port controller");
                        controller.Stop();
                        await _requestPipeWriteLock.WaitAsync(ct);
                        try
                        {
                            await PipeFrame.WriteFrameAsync(pipe, cmd, payload, ct);
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
                    var pipe = _pipe;
                    if (pipe != null)
                    {
                        //Console.Error.WriteLine("Dispatch app end");
                        ServiceAppStopRequest.TryRead(payload, out var req, out var _);
                        //onsole.Error.Write("Starting port controller");
                        controller.Start();
                        var resp = new ServiceAppStopResponse();
                        payload = new byte[resp.SizeOfStruct];
                        resp.TryWrite(payload, out var _);
                        await _requestPipeWriteLock.WaitAsync(ct);
                        try
                        {
                            await PipeFrame.WriteFrameAsync(pipe, cmd, payload, ct);
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
}