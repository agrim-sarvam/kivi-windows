using Microsoft.Extensions.DependencyInjection;

namespace Kivi.App.Services;

/// <summary>
/// Tiny static bridge from the DI container to the workspace pages.
///
/// The main-window pages (RecordPage/HistoryPage) are created via <c>new()</c> in
/// <see cref="MainWindow.CreatePage"/> (not DI-resolved), so they can't take constructor
/// dependencies. This holder exposes the two DI singletons they need — the shared dictation
/// history store and the app-icon resolver — after <see cref="Init"/> is called once from the
/// composition root, before any page is created.
///
/// CRITICAL: <see cref="History"/> is pulled from the SAME container the DictationOrchestrator
/// reads from, so the store the orchestrator writes takes into is the exact instance the pages
/// read from. Never <c>new</c> a second store.
/// </summary>
public static class AppServices
{
    public static IDictationHistoryStore History { get; private set; } = null!;
    public static IAppIconResolver Icons { get; private set; } = null!;

    public static void Init(System.IServiceProvider sp)
    {
        History = sp.GetRequiredService<IDictationHistoryStore>();
        Icons = sp.GetRequiredService<IAppIconResolver>();
    }
}
