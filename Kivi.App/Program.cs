using Microsoft.UI.Xaml;

namespace Kivi.App;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.Start(_ =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
