using HWKit;

using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;

namespace Espmon.Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length == 1 && args[0].Equals("/test", StringComparison.OrdinalIgnoreCase))
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
                using var _dispatcher = new PortDispatcher(appPath);
                var hwInfo = _dispatcher.HardwareInfo;
                hwInfo.Providers.Add(new CoreTempCpuProvider());
                hwInfo.Providers.Add(new CimCpuProvider());
                hwInfo.Providers.Add(new CimRamProvider());
                hwInfo.Providers.Add(new CimDiskProvider());
                hwInfo.Providers.Add(new AmdAdlGpuProvider());
                hwInfo.Providers.Add(new NvidiaNvmlGpuProvider());
                _dispatcher.Start();
                while (!Console.KeyAvailable)
                {
                    Thread.Sleep(100);
                }
                _dispatcher.Stop();
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
}

