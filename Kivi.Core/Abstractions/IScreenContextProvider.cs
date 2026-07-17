namespace Kivi.Core.Abstractions;

// Returns a context string (<=500 chars) or "" on any failure.
public interface IScreenContextProvider
{
    Task<string> CaptureContextAsync(CancellationToken ct);
}
