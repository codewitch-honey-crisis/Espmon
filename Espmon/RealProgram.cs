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
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("AppDomain", Dump(e.ExceptionObject as Exception));

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
                    // e.Message carries the DETAILED XAML parser error (missing
                    // resource key / unresolvable type + line & position). The
                    // exception's own Message is just "XAML parsing failed."
                    Log("XAML", $"e.Message: {e.Message}\n{Dump(e.Exception)}");
                };
            });
        }
        catch (Exception ex)
        {
            Log("Main", Dump(ex));
            throw;
        }
#if DEBUG
        finally
        {
            Bootstrap.Shutdown();
        }
#endif
    }

    static string Dump(Exception ex)
    {
        var sb = new StringBuilder();
        for (var cur = ex; cur != null; cur = cur.InnerException)
            sb.AppendLine($"{cur.GetType().FullName} (HResult 0x{cur.HResult:X8}): {cur.Message}")
              .AppendLine(cur.StackTrace);
        return sb.ToString();
    }

    static void Log(string source, string text)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "espmon-startup-crash.log"),
                $"{DateTime.Now:o} [{source}]\n{text}\n\n");
        }
        catch { }
    }
}