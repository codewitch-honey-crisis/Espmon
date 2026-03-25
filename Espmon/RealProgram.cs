using Espmon;

using Microsoft.Windows.ApplicationModel.DynamicDependency;

public class RealProgram
{
    static void Main(string[] args)
    {
        //Bootstrap.Initialize(0x00010008, null, new PackageVersion(1, 8, 0, 0));
        Bootstrap.Initialize(0x00010008);
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