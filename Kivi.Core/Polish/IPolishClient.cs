namespace Kivi.Core.Polish;
public interface IPolishClient { Task<string> CleanupAsync(string transcript, string context, CancellationToken ct); }
