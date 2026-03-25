using HWKit;

using Microsoft.Diagnostics.Tracing.Analysis.GC;

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
namespace Espmon;

public class Worker : BackgroundService
{
  
    private readonly ILogger<Worker> _logger;
    private PortDispatcher? _dispatcher;
    private StreamWriter _log;
    
    Timer? _timer;
    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _logger.Log(LogLevel.Information, "Initialized provider");
        var logPath = Path.Combine(AppContext.BaseDirectory, "espmon.service.log.txt");
        _log = new StreamWriter(File.OpenWrite(logPath), Encoding.UTF8);
    }
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_timer == null)
        {
            _timer = new Timer(_timer_Tick, null, 0, 100);
        }
        await base.StartAsync(cancellationToken);

    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        await base.StopAsync(cancellationToken);

    }
    private void _timer_Tick(object? state)
    {
        _dispatcher?.Refresh();
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.WriteLine("Execute");
        var appPath = AppContext.BaseDirectory;
        try
        {
            
            _logger.Log(LogLevel.Information, "Executing provider");
            //log.Write("Executing provider");
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
            _dispatcher = new PortDispatcher(appPath);
            var hwInfo = _dispatcher.HardwareInfo;
            hwInfo.Providers.Add(new CoreTempCpuProvider());
            hwInfo.Providers.Add(new CimCpuProvider());
            hwInfo.Providers.Add(new CimRamProvider());
            hwInfo.Providers.Add(new CimDiskProvider());
            hwInfo.Providers.Add(new AmdAdlGpuProvider());
            hwInfo.Providers.Add(new NvidiaNvmlGpuProvider());
            hwInfo.Providers.Add(new DxgiProvider());
            _dispatcher.Start();
            stoppingToken.WaitHandle.WaitOne();
        }
        catch (OperationCanceledException)
        {
        }
    }
    public override void Dispose()
    {
        _logger.Log(LogLevel.Information, "Disposing provider");
        _dispatcher?.Dispose();
        _log.Dispose();
        base.Dispose();

    }
}
