// FlowFrame — the complete render-output contract, ported from packages/orb-core/src/frame.ts
// (Orb/Core/FlowFrame.swift). Every field is present with the SAME default the TS/Swift
// declares. Nested types match the golden JSON encoding.
using System.Collections.Generic;

namespace Kivi.Core.Orb;

public struct ShadowSpec
{
    public double Blur;
    public double Spread;
    public double YOffset;
    public double Alpha;

    public static ShadowSpec Make() => new() { Blur = 0, Spread = 0, YOffset = 0, Alpha = 0 };
}

/// SIMD3<Double> RGB triple.
public struct RGB
{
    public double R;
    public double G;
    public double B;

    public RGB(double r, double g, double b) { R = r; G = g; B = b; }
}

public enum SatTintType { None, Green, Blue }

public struct SatTint
{
    public SatTintType Type;
    public double R;
    public double G;
    public double B;
    public double GlowRadius;
    public double GlowAlpha;

    public static SatTint None() => new() { Type = SatTintType.None };
}

public struct DiffProgress
{
    public double Landing;
    public double LandingEased;
    public double Collapse;
}

public enum ScrollTarget { Top, Bottom }

public struct ScrollCommand
{
    public int Id;
    public ScrollTarget Target;
}

public sealed class FlowFrame
{
    // time
    public double Now = 0;
    public double Breath = 0;

    // phase & mark
    public FlowPhase Phase = FlowPhase.Rest;
    public KiwiMarkState MarkState = KiwiMarkState.Idle;
    public bool Inverted = true;

    // orb geometry
    public double Open = 0;
    public double OrbWidth = 39;
    public double OrbHeight = 15;
    public double OrbRadius = 7.5;
    public double Drop = -6;
    public double Press = 1;

    // fill / glass / glow
    public double FillAlpha = 0.72;
    public double BackdropBlur = 10;
    public ShadowSpec GlowCore = ShadowSpec.Make();
    public ShadowSpec GlowHalo = ShadowSpec.Make();
    public RGB GlowColor = new(214, 220, 230);
    public ShadowSpec DropShadow = ShadowSpec.Make();
    public double MarkOpacity = 0;
    public double SphereOpacity = 0;
    public double LightX = -0.42;
    public double LightY = -0.52;

    // eyes
    public double EyeScale = 1;
    public double EyeOpacity = 0;
    public double EyeOpen = 0;

    // hotkey labels
    public string HotkeyLabel = "fn";
    public string EditComboLabel = "⌃";

    // hint pills
    public HintContent Hint = ModelFactory.MakeHint("tap / hold to talk", true);
    public double HintOpacity = 0;
    public double HintRise = 5;
    public bool HintInteractive = false;
    public bool HintForced = false;
    public double Hint2Opacity = 0;
    public double Hint2Rise = 4;
    public string Hint2Verb = "to edit";
    public string SelectionPillText = "";
    public string? SelectionPillAppBundleID = null;
    public double SelectionPillOpacity = 0;
    public double SelectionPillWidth = 39;
    public double PillPop = 0;

    // satellites
    public double SatSettingsOpacity = 0;
    public double SatSettingsScale = 0.4;
    public double SatExpandOpacity = 0;
    public double SatExpandScale = 0.4;
    public bool SatBottomInteractive = false;
    public bool SatEditShown = false;
    public double SatEditOpacity = 0;
    public double SatEditScale = 0.96;
    public double SatEditShakeX = 0;
    public string? SatEditAppBundleID = null;
    public int TxWordCount = 0;
    public double OrbShakeX = 0;
    public SatTint SatEditTint = SatTint.None();
    public double SatCancelOpacity = 0;
    public bool SatManualCopy = false;
    public bool SatManualCopyHot = false;
    public double SatCancelScale = 0.4;
    public bool SatCancelInteractive = false;
    public bool SatEditLocked = false;
    public bool SatSettingsLocked = false;

    // edit pane
    public double PaneOpacity = 0;
    public double PaneScale = 0.92;
    public double PaneShiftX = 8;

    // expansion / transcript geometry
    public double Exp = 0;
    public bool Expanded = false;
    public double FlowShiftX = 0;
    public double TxWrapWidth = 0;
    public double TxWrapHeight = 0;
    public bool TxWrapClips = true;
    public bool TxClipped = false;
    public double TxOpacity = 0;
    public bool TxInteractive = false;
    public double BoxW = 322;
    public double BoxH = 108;
    public double BoxGrowUp = 0;
    public bool BoxMaxi = false;
    public bool BoxCanMaxi = false;
    public bool BoxOnLeft = false;
    public bool FlipY = false;

    // transcript content
    public TxStage TxStage = TxStage.Idle;
    public List<TxLine> TxLines = new();
    public string TxDots = ".";
    public bool TxAwaitingSpeech = false;
    public int TxWaitingPhase = 0;
    public string? TxNotice = null;
    public string? TxBanner = null;
    public DiffProgress? DiffProgress = null;
    public ScrollCommand? ScrollCommand = null;
    public bool TxEditable = true;
    public string TxEditorSeed = "";

    // hover
    public HoverTarget? HoveredTarget = null;

    // side band + turn-surface chrome
    public bool BandHistOn = false;
    public bool BandHistDim = false;
    public bool BandHistShake = false;
    public bool BandNoSteps = true;
    public bool BandStepsDim = true;
    public bool BandCanPrev = false;
    public bool BandCanNext = false;
    public int TxPagerIndex = 0;
    public int TxPagerCount = 0;
    public string? TakeHostAppBundleID = null;
    public bool RetryOffered = false;
    public int TakeRating = 0;
    public bool TakeRatable = false;
    public bool HasEditChain = false;
    public string? EditContextKind = null;
    public string? EditContextPreview = null;
    public bool CopyFlash = false;
    public bool CopyHint = false;
    public double BoxShakeX = 0;

    // toast
    public string ToastText = "";
    public bool ToastVisible = false;

    // settings echo
    public FlowSettings Settings = FlowSettings.Default();

    // --- Hit-testing / hover geometry -----------------------------------------------------
    //
    // Ported from `_reference/sarvam-kivi-electron/tools/golden-frame-exporter/Sources/
    // GoldenFrameExporter/FlowFrame.swift` (interactiveTarget / orbShapeContains), the source the
    // orb-engine-behavior map cites at "FlowFrame.swift:437". That Swift build authors satellites at
    // FIXED zone-relative offsets (DS.Geometry.satEditX/Y, etc.) from an authored 61px orb. THIS
    // repo's already-ported renderer (Kivi.App/Drawing/SatellitesRenderer.cs, OrbRenderer.cs,
    // TranscriptBoxRenderer.cs) instead computes satellite/box positions DYNAMICALLY from the live
    // f.OrbWidth/OrbHeight/Drop/Exp/FlipY each frame (side bubbles hug the orb's current edges; the
    // box sits directly under/above it). So this port reproduces the SAME z-order, margin (+2px),
    // 1.5x satellite hit-radius, and opacity<=0.08-skip RULES as the Swift source, but measures each
    // region against THIS renderer's actual per-frame geometry so hit-testing never drifts from what
    // is drawn. All coordinates are "flow" space: origin at the orb's un-drop-shifted center
    // (OrbRenderer.CenterX, OrbRenderer.OrbCenterY in canvas space), x unshifted by FlowShiftX.
    //
    // Gap/size constants mirror SatellitesRenderer.cs verbatim (kept private there) so the hit
    // regions can never drift from the drawn bubbles.
    private const double SatGap = 6;
    private const double SatSideWoken = 32.5, SatSideSmall = 21.5;
    private const double SatExpandSize = 23;
    private const double WedgeGap = 3; // TranscriptBoxRenderer.WedgeGap
    private const double WedgeH = 9;   // TranscriptBoxRenderer.WedgeH — box content starts below this
    private const double ContentTopPad = 10; // TranscriptBoxRenderer.contentTop = wedge + 10

    // Box-internal chrome (orb-visual-and-box.md §8b/§8d): copy chip (28x28, offset 8 from the
    // box's top-right), the footer action bar (height 30, thumbs 28x28 near its left, new-session
    // pill near its right). Kept private here AND mirrored verbatim in TranscriptBoxRenderer.cs so
    // hit regions never drift from what's drawn (same discipline as the satellite gap/size consts
    // above).
    private const double CopyChipSize = 26, HeaderPadR = 16;
    // Footer STRIP height — MUST equal TranscriptBoxRenderer.FooterH (and BoxContentFit.FooterH).
    // 48 (not 30) so the footer pills sit in a strip with the reference's 8/10 padding breathing room.
    private const double FooterH = 48;
    private const double ThumbSize = 28, ThumbGap = 6;
    // MUST equal TranscriptBoxRenderer.NewSessionW — 118 fits the "new session" label snugly.
    private const double NewSessionW = 118, NewSessionH = 27, NewSessionPad = 12;

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    /// Orb center Y in flow space (flow-y measured from the orb's undropped center, i.e. 0 here
    /// corresponds to OrbRenderer's `OrbCenterY`). The orb body itself is drawn at
    /// `Drop + OrbHeight/2` below that anchor (see OrbRenderer.DrawOrb: cy = OrbCenterY + Drop + h/2).
    private double OrbCenterYFlow => Drop + OrbHeight / 2.0;

    /// Whether a point lies within the orb's CURRENTLY VISIBLE rounded-rect shape (the flat pill at
    /// rest, the round orb once woken) inflated by `margin`. Standard rounded-rect signed distance —
    /// matches the shape OrbRenderer actually fills/clips to (DrawUtil.RoundedRect), never the
    /// bounding box (whose corners stick out past the visible round orb).
    public static bool OrbShapeContains(double px, double py, double centerY, double halfW, double halfH, double radius, double margin)
    {
        double dx = Math.Max(Math.Abs(px) - (halfW - radius), 0);
        double dy = Math.Max(Math.Abs(py - centerY) - (halfH - radius), 0);
        double rm = radius + margin;
        return dx * dx + dy * dy <= rm * rm;
    }

    /// The box's top-left in flow space (matches TranscriptBoxRenderer.Draw: boxLeft = centerX -
    /// boxW/2 + FlowShiftX, boxTop = orbCenterYBase + Drop + OrbHeight + WedgeGap — both expressed
    /// relative to the (CenterX, OrbCenterY) anchor here).
    private (double left, double top) BoxOriginFlow()
    {
        double top = FlipY
            ? OrbCenterYFlow - WedgeGap - BoxH // opens upward when flipped
            : OrbCenterYFlow + OrbHeight / 2.0 + WedgeGap;
        double left = -BoxW / 2.0 + FlowShiftX;
        return (left, top);
    }

    /// A satellite bubble's live center in flow space, mirroring SatellitesRenderer.Draw exactly:
    /// side bubbles hug the orb's current left/right edge (sideDX from orb half-width + gap +
    /// half bubble size, blended 32.5→21.5px as Exp rises); the expand bubble hugs the top/bottom
    /// edge depending on FlipY.
    private (double cx, double cy, double size) SatelliteGeometry(bool left)
    {
        double blend = Clamp01(Exp);
        double sideSize = SatSideWoken + (SatSideSmall - SatSideWoken) * blend;
        double orbCenterY = OrbCenterYFlow;
        double sideDX = OrbWidth / 2.0 + SatGap + sideSize / 2.0;
        double cx = left ? -sideDX : sideDX;
        return (cx, orbCenterY, sideSize);
    }

    private (double cx, double cy, double size) ExpandGeometry()
    {
        double orbCenterY = OrbCenterYFlow;
        double belowDY = OrbHeight / 2.0 + SatGap + SatExpandSize / 2.0;
        double cy = FlipY ? orbCenterY - belowDY : orbCenterY + belowDY;
        return (0, cy, SatExpandSize);
    }

    /// Whether a satellite bubble at (cx, cy, size) is hittable: both meant-to-be-interactive AND
    /// actually visible (opacity > 0.08) — an invisible bubble (faded during recording, or a
    /// collapsing reveal) reserves NO hover area. Hit radius is 1.5x the VISIBLE radius (size/2),
    /// matching the map's "satellite circles at 1.5x visible radius" rule (the Swift source uses a
    /// flat +2px margin instead; 1.5x is what orb-engine-behavior.md / orb-visual-and-box.md specify
    /// for this port, and is generous enough to be forgiving on a small bubble without overlapping
    /// neighbors at the gaps above).
    private static bool SatHit(double px, double py, double cx, double cy, double size, bool active, double opacity)
    {
        if (!active || opacity <= 0.08) return false;
        double hitR = (size / 2.0) * 1.5;
        double dx = px - cx, dy = py - cy;
        return dx * dx + dy * dy <= hitR * hitR;
    }

    /// Whether a flow-space point lands on editable transcript-box content (excludes the resize
    /// edge / dead space so a click there just focuses the box). Pure so the shell and tests share
    /// one definition.
    public bool IsOverBoxContent(double flowX, double flowY)
    {
        if (!TxInteractive) return false;
        double local = BoxLocalX(flowX);
        var (_, top) = BoxOriginFlow();
        return local >= 2 && local <= TxWrapWidth - 2 && flowY >= top && flowY <= top + BoxH;
    }

    /// Flow-x measured from the box's own left edge (BoxOnLeft has no meaning in this renderer today
    /// — the box is always centered under the orb — kept for parity with the source's naming and to
    /// stay forward-compatible if a left-docked layout is added later).
    public double BoxLocalX(double flowX)
    {
        double shifted = flowX - FlowShiftX;
        var (left, _) = BoxOriginFlow();
        return shifted - (left - FlowShiftX); // = shifted - (-BoxW/2) = shifted + BoxW/2
    }

    /// Reflect a flow-y about the orb's own center when the flow is mirrored vertically (FlipY) —
    /// unused directly by the orb/box regions below (which already account for FlipY themselves) but
    /// kept as the named helper the map calls for (`FlipFlowY`), for any caller needing the mirror.
    public double FlipFlowY(double flowY) => FlipY ? 2 * OrbCenterYFlow - flowY : flowY;

    public bool IsInteractive(double flowX, double flowY) => InteractiveTarget(flowX, flowY) != null;

    /// The SINGLE geometric hover/hit classifier: which interactive element a flow-space point is
    /// over, or null for click-through. Checked topmost-first (z-order): pane -> satellites -> drag
    /// handle -> orb (rounded-rect SDF, +2px) -> hint -> box -> field (lowest priority). Every region
    /// matches what is actually DRAWN (never a bounding box), and invisible satellites (opacity <=
    /// 0.08) reserve no hit area. `DragHandle`/`Field`/free-form pane rects are not yet drawn by this
    /// port's renderer (movable mode / edit pane are future milestones per orb-visual-and-box.md §4);
    /// their branches are ported now so hover/click wiring doesn't need to change again when those
    /// ship, but they will simply never fire until the corresponding FlowFrame fields are populated
    /// (PaneOpacity stays 0, Settings.Movable stays false).
    public HoverTarget? InteractiveTarget(double flowX, double flowY)
    {
        double shifted = flowX - FlowShiftX;

        // pane (topmost) — on the side opposite the box.
        if (PaneOpacity > 0.5)
        {
            double paneTop = OrbCenterYFlow - 6;
            const double paneWidth = 212, paneGap = 8;
            double zoneHalfPane = OrbWidth / 2.0 + SatGap + SatSideWoken; // approx cluster half-width
            double paneLeft, paneRight;
            if (BoxOnLeft) { paneLeft = zoneHalfPane + paneGap; paneRight = paneLeft + paneWidth; }
            else { paneRight = -zoneHalfPane - paneGap; paneLeft = paneRight - paneWidth; }
            if (shifted >= paneLeft && shifted <= paneRight && flowY >= paneTop && flowY <= paneTop + 200)
                return HoverTarget.Pane;
        }

        // satellites (above the orb, below the pane).
        var (cancelCx, cancelCy, cancelSize) = SatelliteGeometry(left: false);
        bool cancelMode = SatCancelInteractive;
        bool manualCopy = SatManualCopy;
        if ((cancelMode || manualCopy) && SatHit(shifted, flowY, cancelCx, cancelCy, cancelSize, active: true, opacity: SatCancelOpacity))
            return HoverTarget.SatCancel;

        var (editCx, editCy, editSize) = SatelliteGeometry(left: true);
        if (SatHit(shifted, flowY, editCx, editCy, editSize, active: SatEditShown || SatEditLocked, opacity: SatEditOpacity))
            return HoverTarget.SatEdit;

        if (!cancelMode && !manualCopy &&
            SatHit(shifted, flowY, cancelCx, cancelCy, cancelSize, active: SatBottomInteractive || SatSettingsLocked, opacity: SatSettingsOpacity))
            return HoverTarget.SatSettings;

        var (expCx, expCy, expSize) = ExpandGeometry();
        if (SatHit(shifted, flowY, expCx, expCy, expSize, active: SatBottomInteractive && !Expanded, opacity: SatExpandOpacity))
            return HoverTarget.SatExpand;

        // drag handle (movable mode only, box closed) — checked BEFORE the orb so grabbing it to
        // move the bar never reads as a talk press. Not yet drawn by this renderer (Settings.Movable
        // is always false today per FlowSettings.Default()); ported for forward-compatibility.
        if (Settings.Movable && !Expanded)
        {
            const double hw = 14, hh = 10; // dragHandleHitW/H halves (28x20)
            double handleY = OrbCenterYFlow - OrbHeight / 2.0 - 19;
            if (Math.Abs(shifted) <= hw && Math.Abs(flowY - handleY) <= hh)
                return HoverTarget.DragHandle;
        }

        // orb — its currently visible rounded shape, +2px.
        if (OrbShapeContains(shifted, flowY, OrbCenterYFlow, OrbWidth / 2.0, OrbHeight / 2.0, OrbRadius, margin: 2))
            return HoverTarget.Orb;

        // hint pill row (approximate width; tall enough for the x badge).
        if (HintInteractive)
        {
            double hintY = OrbCenterYFlow + OrbHeight / 2.0 + SatGap + 24; // below the orb-zone bottom
            if (flowY >= hintY - 8 && flowY <= hintY + 24 && Math.Abs(shifted) <= 130)
                return HoverTarget.Hint;
        }

        // expanded box + band + resize handles. Resize handles bleed +-10 past the visible edges.
        if (TxInteractive)
        {
            double local = BoxLocalX(flowX);
            var (_, top) = BoxOriginFlow();

            // copy chip (§8b/§8c, matched to the actual _reference/TranscriptBox.tsx header-row
            // markup rather than the map's inner-card wording — the two disagree on placement/size;
            // per RULE 2 the running Electron code is the visual source of truth): a 26x26 chip in
            // the HEADER row's top-right, inset from the box's right edge by padR(16), vertically
            // aligned with the header content top. Only live once there's settled, non-empty
            // content — a click when the chip isn't drawn falls through to the generic Box region.
            bool copyChipVisible = TxWordCount > 0 &&
                (TxStage == TxStage.Done || TxStage == TxStage.Typed || TxStage == TxStage.Pasted);
            if (copyChipVisible)
            {
                double contentTop = (FlipY ? 0 : WedgeH) + ContentTopPad;
                double chipLeft = BoxW - HeaderPadR - CopyChipSize;
                double chipTop = top + contentTop;
                if (local >= chipLeft && local <= chipLeft + CopyChipSize &&
                    flowY >= chipTop && flowY <= chipTop + CopyChipSize)
                    return HoverTarget.CopyChip;
            }

            // footer action bar (§8d, height 30, bottom-anchored inside the box): thumbs near the
            // left (only when a take is ratable), the "new session" pill near the right.
            double footerTop = top + BoxH - FooterH;
            if (flowY >= footerTop && flowY <= top + BoxH)
            {
                if (TakeRatable)
                {
                    // Thumbs sit AFTER the left voice-slot pill + a flex spacer in the reference
                    // markup (i.e. they float just left of the new-session pill, not pinned to the
                    // box's left edge) — anchor them relative to the new-session pill's left edge
                    // instead of a fixed left offset so they never drift from what's drawn.
                    double nsLeftAnchor = BoxW - NewSessionPad - NewSessionW;
                    double upLeft = nsLeftAnchor - ThumbGap - ThumbSize * 2 - ThumbGap;
                    double upTop = footerTop + (FooterH - ThumbSize) / 2.0;
                    if (local >= upLeft && local <= upLeft + ThumbSize &&
                        flowY >= upTop && flowY <= upTop + ThumbSize)
                        return HoverTarget.ThumbUp;
                    double downLeft = upLeft + ThumbSize + ThumbGap;
                    if (local >= downLeft && local <= downLeft + ThumbSize &&
                        flowY >= upTop && flowY <= upTop + ThumbSize)
                        return HoverTarget.ThumbDown;
                }
                double nsRight = BoxW - NewSessionPad;
                double nsLeft = nsRight - NewSessionW;
                double nsTop = footerTop + (FooterH - NewSessionH) / 2.0;
                if (local >= nsLeft && local <= nsRight && flowY >= nsTop && flowY <= nsTop + NewSessionH)
                    return HoverTarget.NewSession;
            }

            if (local >= -10 && local <= TxWrapWidth + 10 && flowY >= top - 10 && flowY <= top + BoxH + 10)
                return HoverTarget.Box;
        }

        // woken-widget envelope (lowest priority): while awake, the visible cluster (orb zone +
        // bridge down to the hint) reads as one solid companion region so drags/hovers between the
        // orb and its satellites/hint never fall through empty space.
        if (Open > 0.5)
        {
            double zoneHalf = OrbWidth / 2.0 + SatGap + SatSideWoken;
            double zoneBottom = OrbCenterYFlow + OrbHeight / 2.0 + SatGap + SatExpandSize;
            double bottom = HintInteractive ? (zoneBottom + 2) : zoneBottom;
            if (Math.Abs(shifted) <= zoneHalf && flowY >= OrbCenterYFlow - OrbHeight / 2.0 - 2 && flowY <= bottom)
                return HoverTarget.Field;
        }

        return null;
    }
}
