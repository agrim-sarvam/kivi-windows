using Kivi.Core.Text;
using Xunit;

public class WordDiffTests
{
    [Fact]
    public void Compute_IdenticalText_AllEqual_AndReconstructs()
    {
        var diff = WordDiff.Compute("hello world", "hello world");
        Assert.All(diff, t => Assert.Equal(DiffOp.Equal, t.Op));
        Assert.Equal("hello world", string.Concat(diff.Select(t => t.Text)));
    }

    [Fact]
    public void Compute_ReconstructsOriginal_FromEqualAndDeleteTokens()
    {
        var diff = WordDiff.Compute("Kal 3 PM works fine", "Kal 3 PM works great");
        var original = string.Concat(diff.Where(t => t.Op != DiffOp.Insert).Select(t => t.Text));
        Assert.Equal("Kal 3 PM works fine", original);
    }

    [Fact]
    public void Compute_ReconstructsRewritten_FromEqualAndInsertTokens()
    {
        var diff = WordDiff.Compute("Kal 3 PM works fine", "Kal 3 PM works great");
        var rewritten = string.Concat(diff.Where(t => t.Op != DiffOp.Delete).Select(t => t.Text));
        Assert.Equal("Kal 3 PM works great", rewritten);
    }

    [Fact]
    public void Compute_PureInsertion_HasNoDeletes()
    {
        var diff = WordDiff.Compute("hello", "hello world");
        Assert.DoesNotContain(diff, t => t.Op == DiffOp.Delete);
        Assert.Contains(diff, t => t.Op == DiffOp.Insert && t.Text.Contains("world"));
    }

    [Fact]
    public void Compute_EmptyOriginal_HasNoDeletes()
    {
        var diff = WordDiff.Compute("", "new text");
        Assert.DoesNotContain(diff, t => t.Op == DiffOp.Delete);
    }

    [Fact]
    public void Compute_EmptyRewritten_HasNoInserts()
    {
        var diff = WordDiff.Compute("old text", "");
        Assert.DoesNotContain(diff, t => t.Op == DiffOp.Insert);
    }
}
