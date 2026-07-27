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
}
