using Velopack;

namespace Kivi.App;

/// <summary>
/// Explicit process entry point. Velopack requires <c>VelopackApp.Build().Run()</c> to execute
/// before any other app logic so it can intercept the install/update/uninstall hook invocations
/// (`--veloapp-install`, `--veloapp-updated`, etc.) that the generated Setup.exe / the Velopack
/// runtime invoke against the installed exe. This must run before WPF's own startup, so the
/// WPF-SDK auto-generated `Main` (normally emitted from App.xaml's x:Class) is suppressed via
/// `&lt;StartupObject&gt;` in Kivi.App.csproj, and this hand-written Main takes over.
/// </summary>
public static class Program
{
    [System.STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
