using Espmon;

using Microsoft.Windows.ApplicationModel.DynamicDependency;

using System.Diagnostics;
using System.Threading;

public class RealProgram
{
    static void Main(string[] args)
    {
        Bootstrap.Initialize(0x00010008);
#if DEBUG
        //Process.Start("Espmon.Service.exe");
        //Thread.Sleep(5000);
        //Bootstrap.Shutdown(); return;
#endif
        global::Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        
        Bootstrap.Shutdown();
    }
}