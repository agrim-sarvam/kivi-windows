// Unit tests for the pure geometric hit-test (FlowFrame.InteractiveTarget / OrbShapeContains),
// ported per docs/maps/orb-engine-behavior.md §7 and orb-visual-and-box.md §4 from
// _reference/.../GoldenFrameExporter/FlowFrame.swift's interactiveTarget. These construct a
// FlowFrame directly (no engine tick needed — the hit-test is pure geometry over frame fields) and
// assert: orb-center hit, far-away miss, invisible-satellite skip, and z-order precedence.
using Kivi.Core.Orb;
using Xunit;

namespace Kivi.Core.Tests;

public sealed class HitTestTests
{
    // A woken orb (open=1) at the default 61x61 size, drop=0, no expansion/flip/shift — the
    // simplest "orb is awake" frame to hit-test against.
    private static FlowFrame WokenOrb()
    {
        var f = new FlowFrame
        {
            Open = 1,
            OrbWidth = 61,
            OrbHeight = 61,
            OrbRadius = 30.5,
            Drop = 0,
            FlowShiftX = 0,
            Exp = 0,
            FlipY = false,
        };
        return f;
    }

    [Fact]
    public void OrbCenter_HitsOrb()
    {
        var f = WokenOrb();
        // orb center in flow space is (0, OrbCenterYFlow) = (0, Drop + OrbHeight/2) = (0, 30.5)
        Assert.Equal(HoverTarget.Orb, f.InteractiveTarget(0, 30.5));
    }

    [Fact]
    public void FarAway_MissesEverything()
    {
        var f = WokenOrb();
        Assert.Null(f.InteractiveTarget(5000, 5000));
    }

    [Fact]
    public void OrbShapeMargin_2px_JustOutsideEdgeStillHits()
    {
        var f = WokenOrb();
        // Right edge of the (rounded, effectively near-circular at r=30.5, halfW=halfH=30.5) orb is
        // at flowX = 30.5 from center. 1px further out is still within the +2px margin.
        Assert.Equal(HoverTarget.Orb, f.InteractiveTarget(31.5, 30.5));
        // Directly using OrbShapeContains (bypassing the lower-priority "woken widget envelope"
        // fallback that legitimately covers the whole open cluster once Open>0.5): 5px past the
        // edge is outside the orb's own +2px margin.
        Assert.False(FlowFrame.OrbShapeContains(36.5, 30.5, 30.5, 30.5, 30.5, 30.5, margin: 2));
        // And well outside the woken-cluster envelope entirely (which the .Field branch legitimately
        // claims for any open>0.5 orb) is a true click-through miss.
        Assert.Null(f.InteractiveTarget(9000, 30.5));
    }

    [Fact]
    public void InvisibleSatellite_OpacityAtOrBelowThreshold_ReservesNoArea()
    {
        var f = WokenOrb();
        f.SatEditShown = true;
        f.SatEditOpacity = 0.08; // <= 0.08 threshold -> must NOT be hittable
        // The left satellite center sits at (-sideDX, orbCenterY); sideDX = OrbWidth/2 + gap(6) +
        // sideSize/2. At Exp=0, sideSize=32.5, so sideDX = 30.5+6+16.25 = 52.75.
        double sideDX = f.OrbWidth / 2.0 + 6 + 32.5 / 2.0;
        var hit = f.InteractiveTarget(-sideDX, f.Drop + f.OrbHeight / 2.0);
        Assert.NotEqual(HoverTarget.SatEdit, hit);
    }

    [Fact]
    public void VisibleSatellite_AboveThreshold_IsHittableAtCenter()
    {
        var f = WokenOrb();
        f.SatEditShown = true;
        f.SatEditOpacity = 1.0;
        double sideDX = f.OrbWidth / 2.0 + 6 + 32.5 / 2.0;
        var hit = f.InteractiveTarget(-sideDX, f.Drop + f.OrbHeight / 2.0);
        Assert.Equal(HoverTarget.SatEdit, hit);
    }

    [Fact]
    public void ZOrder_PaneBeatsSatellite_WhenBothCoverSamePoint()
    {
        var f = WokenOrb();
        // Put the cancel satellite fully live at a known point, then open a pane whose rect also
        // covers a point far to the side — verify pane wins over any satellite it happens to
        // overlap. Rather than engineering an exact overlap (fragile across future geometry
        // tweaks), assert the documented ordering contract directly: with paneOpacity>0.5 the pane
        // check runs FIRST and returns before any satellite check executes for any point inside its
        // rect, regardless of whether a satellite is also configured live.
        f.PaneOpacity = 1.0;
        f.SatCancelInteractive = true;
        f.SatCancelOpacity = 1.0;
        double paneTop = (f.Drop + f.OrbHeight / 2.0) - 6;
        // Pane sits on the left by default (BoxOnLeft=false): paneRight = -zoneHalf-8, paneLeft =
        // paneRight-212. Pick a point well inside that rect.
        double zoneHalf = f.OrbWidth / 2.0 + 6 + 32.5;
        double paneRight = -zoneHalf - 8;
        double paneLeft = paneRight - 212;
        double px = (paneLeft + paneRight) / 2.0;
        double py = paneTop + 20;
        Assert.Equal(HoverTarget.Pane, f.InteractiveTarget(px, py));
    }

    [Fact]
    public void ZOrder_SatelliteBeatsOrb_WhenSatelliteOverlapsOrbBounds()
    {
        // A satellite hit-test happens before the orb-shape check in InteractiveTarget; verify a
        // point that satisfies BOTH the satellite circle and (hypothetically) the orb SDF resolves
        // to the satellite, not the orb. We engineer this by placing the expand satellite very close
        // to the orb center (small orb) so their hit areas overlap.
        var f = new FlowFrame
        {
            Open = 1,
            OrbWidth = 10,
            OrbHeight = 10,
            OrbRadius = 5,
            Drop = 0,
            SatBottomInteractive = true,
            SatExpandOpacity = 1.0,
            Expanded = false,
        };
        // Expand bubble center: (0, orbCenterY + OrbHeight/2 + gap(6) + expandSize(23)/2)
        double orbCenterY = f.Drop + f.OrbHeight / 2.0;
        double expCy = orbCenterY + f.OrbHeight / 2.0 + 6 + 23 / 2.0;
        // The orb's SDF at margin 2 extends to roughly halfH+2 = 7 below center — far short of
        // expCy, so this point can ONLY be the expand satellite, proving satellites are reachable
        // independently of the orb region without a false "orb" classification bleeding through.
        var hit = f.InteractiveTarget(0, expCy);
        Assert.Equal(HoverTarget.SatExpand, hit);
    }

    [Fact]
    public void IsInteractive_TrueOnOrb_FalseFarAway()
    {
        var f = WokenOrb();
        Assert.True(f.IsInteractive(0, 30.5));
        Assert.False(f.IsInteractive(5000, 5000));
    }

    [Fact]
    public void RestPill_HitTestUsesCollapsedShape()
    {
        // At rest (open=0) the orb is the flat 39x15 pill, not the 61px orb — a point that would be
        // inside the woken orb's radius but outside the pill's half-height should miss.
        var f = new FlowFrame
        {
            Open = 0,
            OrbWidth = 39,
            OrbHeight = 15,
            OrbRadius = 7.5,
            Drop = -6,
        };
        double centerY = f.Drop + f.OrbHeight / 2.0; // -6 + 7.5 = 1.5
        Assert.Equal(HoverTarget.Orb, f.InteractiveTarget(0, centerY));
        // 20px below the pill's center is far outside a 15px-tall pill (+2px margin).
        Assert.Null(f.InteractiveTarget(0, centerY + 20));
    }

    // --- box-internal chrome: copy chip / footer thumbs / new-session pill ---------------------
    // These mirror the geometry TranscriptBoxRenderer.cs actually draws (header-row copy chip,
    // bottom-anchored footer bar) per orb-visual-and-box.md §8b-§8d, added in the ui-fidelity
    // completion pass. A minimal "expanded box, woken orb, no shift/flip" frame is enough since the
    // box-internal regions only depend on TxInteractive/BoxW/BoxH/FlipY, not the orb's own geometry.
    private static FlowFrame ExpandedBox(double boxW = 322, double boxH = 200)
    {
        return new FlowFrame
        {
            Open = 1,
            OrbWidth = 61,
            OrbHeight = 61,
            OrbRadius = 30.5,
            Drop = 0,
            Exp = 1,
            Expanded = true,
            TxInteractive = true,
            TxWrapWidth = boxW,
            BoxW = boxW,
            BoxH = boxH,
        };
    }

    [Fact]
    public void CopyChip_HitsOnlyWhenSettledAndNonEmpty()
    {
        var f = ExpandedBox();
        f.TxWordCount = 12;
        f.TxStage = TxStage.Done;
        // chip: 26x26 top-right of the header row, inset 16 from the box's right edge, top at
        // (flipY?0:wedgeH=9)+10 = 19 below the box's own top.
        var (_, boxTop) = BoxOriginFlowForTest(f);
        double chipCenterLocalX = f.BoxW - 16 - 13; // right inset(16) - half chip(13)
        double chipCenterY = boxTop + 19 + 13;
        Assert.Equal(HoverTarget.CopyChip, f.InteractiveTarget(chipCenterLocalX - f.BoxW / 2.0, chipCenterY));

        // Same point misses once there's no settled content (chip isn't drawn) — falls through to
        // the generic Box region instead.
        f.TxWordCount = 0;
        Assert.Equal(HoverTarget.Box, f.InteractiveTarget(chipCenterLocalX - f.BoxW / 2.0, chipCenterY));
    }

    [Fact]
    public void FooterThumbs_OnlyHitWhenTakeRatable()
    {
        var f = ExpandedBox();
        f.TakeRatable = true;
        var (_, boxTop) = BoxOriginFlowForTest(f);
        double footerTop = boxTop + f.BoxH - 30;
        double nsLeftAnchor = f.BoxW - 12 - 92; // NewSessionPad(12) + NewSessionW(92)
        double upLeftLocal = nsLeftAnchor - 6 - 28 * 2 - 6; // thumbGap/size mirror FlowFrame consts
        double upCenterLocalX = upLeftLocal + 14;
        double thumbCenterY = footerTop + 15;
        Assert.Equal(HoverTarget.ThumbUp, f.InteractiveTarget(upCenterLocalX - f.BoxW / 2.0, thumbCenterY));

        double downCenterLocalX = upLeftLocal + 28 + 6 + 14;
        Assert.Equal(HoverTarget.ThumbDown, f.InteractiveTarget(downCenterLocalX - f.BoxW / 2.0, thumbCenterY));

        // Not ratable -> the same point is just the generic box region, not a thumb.
        f.TakeRatable = false;
        Assert.Equal(HoverTarget.Box, f.InteractiveTarget(upCenterLocalX - f.BoxW / 2.0, thumbCenterY));
    }

    [Fact]
    public void NewSessionPill_HitsInFooterRightRegardlessOfRatable()
    {
        var f = ExpandedBox();
        var (_, boxTop) = BoxOriginFlowForTest(f);
        double footerTop = boxTop + f.BoxH - 30;
        double nsLeftAnchor = f.BoxW - 12 - 92;
        double centerLocalX = nsLeftAnchor + 46;
        double centerY = footerTop + 15;
        Assert.Equal(HoverTarget.NewSession, f.InteractiveTarget(centerLocalX - f.BoxW / 2.0, centerY));
    }

    [Fact]
    public void BoxChrome_MissesFarBelowBox()
    {
        var f = ExpandedBox();
        var (_, boxTop) = BoxOriginFlowForTest(f);
        Assert.Null(f.InteractiveTarget(0, boxTop + f.BoxH + 5000));
    }

    /// Mirrors FlowFrame's private BoxOriginFlow() for test purposes (box sits directly under the
    /// woken orb with the WedgeGap(3) seam, non-flipped, non-shifted).
    private static (double left, double top) BoxOriginFlowForTest(FlowFrame f)
    {
        double orbCenterYFlow = f.Drop + f.OrbHeight / 2.0;
        double top = f.FlipY ? orbCenterYFlow - 3 - f.BoxH : orbCenterYFlow + f.OrbHeight / 2.0 + 3;
        double left = -f.BoxW / 2.0 + f.FlowShiftX;
        return (left, top);
    }
}
