namespace Kivi.Core.Abstractions;

public interface IPasteService
{
    Task InjectTextAsync(string text, bool pressEnter);
    Task UndoAsync();
}
