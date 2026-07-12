using Espmon;

using Microsoft.Windows.ApplicationModel.DynamicDependency;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class RealProgram
{
    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("AppDomain", e.ExceptionObject as Exception);

#if DEBUG
        Bootstrap.Initialize(0x00010008); // Debug = framework-dependent
#endif

        try
        {
            XamlCheckProcessRequirements();
            global::WinRT.ComWrappersSupport.InitializeComWrappers();

            global::Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);

                var app = new App();

                app.UnhandledException += (s, e) =>
                {
                    if (e != null)
                    {
                        // e.Message carries the DETAILED XAML parser error (missing
                        // resource key / unresolvable type + line & position). The
                        // exception's own Message is just "XAML parsing failed."
                        Log("XAML",e.Exception);
                    }
                };
            });
        }
        catch (Exception ex)
        {
            Log("Main", ex);
            throw;
        }
#if DEBUG
        finally
        {
            Bootstrap.Shutdown();
        }
#endif
    }

   
    static readonly string CrashLogPath = System.IO.Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Espmon", "crash.log");

    static void Log(string source, Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashLogPath)!);
            System.IO.File.AppendAllText(CrashLogPath, $"{DateTime.Now} [{source}] {ex}\n\n");
        }
        catch { /* last-resort: never let logging throw */ }
    }
}