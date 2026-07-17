using System.Windows.Forms;
namespace Kivi.App;

public static class MessagePump
{
    // Application.Run pumps the Windows message loop on the calling thread.
    // Program entrypoint must be STA (set via [STAThread] on Main).
    public static void Run() => Application.Run();
}
