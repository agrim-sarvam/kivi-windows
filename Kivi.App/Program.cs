using Microsoft.UI.Xaml;
using Velopack;

namespace Kivi.App;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Must run first: Velopack re-launches this exe with special hook arguments during
        // install/update/uninstall, and this handles those hooks before any normal startup
        // logic runs. Without this call, vpk pack requires --skipVeloAppCheck and Velopack's
        // own install/update lifecycle hooks never fire (surfaced as "Install Partially
        // Succeeded" after Setup.exe runs).
        VelopackApp.Build().Run();

        Application.Start(_ =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
