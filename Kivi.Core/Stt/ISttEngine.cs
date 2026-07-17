namespace Kivi.Core.Stt;
public interface ISttEngine { Task<string> TranscribeAsync(byte[] wav, CancellationToken ct); }
