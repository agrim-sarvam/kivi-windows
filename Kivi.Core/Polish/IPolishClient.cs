namespace Kivi.Core.Polish;
public interface IPolishClient
{
    event Action<string>? EnteringCooldown;
    Task<string> CleanupAsync(string transcript, string context, CancellationToken ct);
    Task<string> RewriteAsync(string selectedText, string voiceCommand, CancellationToken ct);
}
