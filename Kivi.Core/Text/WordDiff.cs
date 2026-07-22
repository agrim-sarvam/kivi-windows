using System.Text.RegularExpressions;

namespace Kivi.Core.Text;

public enum DiffOp { Equal, Delete, Insert }

public readonly record struct DiffToken(DiffOp Op, string Text);

/// <summary>
/// Word-level LCS diff for the "hey kivi" rewrite review UI: original vs. rewritten text,
/// tokenized on runs of whitespace/non-whitespace so each token already carries its own
/// spacing and the renderer can just concatenate runs directly.
/// </summary>
public static class WordDiff
{
    public static IReadOnlyList<DiffToken> Compute(string original, string rewritten)
    {
        var a = Tokenize(original);
        var b = Tokenize(rewritten);
        int n = a.Count, m = b.Count;

        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<DiffToken>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { result.Add(new DiffToken(DiffOp.Equal, a[x])); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) { result.Add(new DiffToken(DiffOp.Delete, a[x])); x++; }
            else { result.Add(new DiffToken(DiffOp.Insert, b[y])); y++; }
        }
        while (x < n) { result.Add(new DiffToken(DiffOp.Delete, a[x])); x++; }
        while (y < m) { result.Add(new DiffToken(DiffOp.Insert, b[y])); y++; }
        return result;
    }

    private static List<string> Tokenize(string text) =>
        Regex.Matches(text, @"\S+|\s+").Select(m => m.Value).ToList();
}
