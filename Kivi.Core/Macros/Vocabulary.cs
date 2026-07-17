namespace Kivi.Core.Macros;

public static class Vocabulary
{
    public static string Merge(string raw) => string.Join(", ",
        raw.Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Distinct(StringComparer.Ordinal));
}
