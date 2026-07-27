using Kivi.Core.Contracts;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>
/// P1 smoke test — proves the solution builds and the core contracts are referenceable.
/// Real parity tests (golden-frame oracle, wire parity) arrive in P2/P3.
/// </summary>
public class SkeletonSmokeTests
{
    [Fact]
    public void Contracts_Are_Defined()
    {
        // The platform seam interfaces exist and are wired for DI in later phases.
        Assert.NotNull(typeof(IHotkeyService));
        Assert.NotNull(typeof(IPasteService));
        Assert.NotNull(typeof(IAudioCapture));
        Assert.NotNull(typeof(IFrontmostApp));
    }

    [Fact]
    public void GestureEdge_Records_Kind_And_Timestamp()
    {
        var edge = new GestureEdge(GestureEdgeKind.Down, 1234);
        Assert.Equal(GestureEdgeKind.Down, edge.Kind);
        Assert.Equal(1234, edge.TimestampMs);
    }
}
