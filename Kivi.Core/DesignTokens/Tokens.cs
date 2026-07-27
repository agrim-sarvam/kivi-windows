// Design tokens — a C# port of packages/design-tokens/tokens.ts. The EXACT token
// values (colors, type scale, spacing/radii, the full orb sub-tree, Canon light/dark)
// transcribed 1:1. XAML theme dictionaries are a later Kivi.App phase; here we produce
// the values + a pick-style accessor.
//
// R15: dark = Canon.dark override (NOT dimmed light). The two brand creams are kept
// DISTINCT (Cream.Legacy vs Cream.Canon). Never round or collapse values.
namespace Kivi.Core.DesignTokens;

public enum Mood { Light, Dark }

/// A colour that differs between the two moods.
public readonly record struct ThemedColor(string Light, string Dark)
{
    public string Pick(Mood mood) => mood == Mood.Light ? Light : Dark;
}

/// Resolve a {light,dark} token for a mood.
public static class TokenPick
{
    public static string Pick(ThemedColor t, Mood mood) => t.Pick(mood);
}

public static class Tokens
{
    // ================= FONTS =================
    public static class Font
    {
        public const string FamilyBody = "\"Matter\", system-ui, sans-serif";
        public const string FamilyMono = "\"Matter Mono\", ui-monospace, SFMono-Regular, Menlo, monospace";
        public const string FamilyDisplay = "\"Space Grotesk\", \"Helvetica Neue\", sans-serif";
        public const string FamilySerif = "\"Season Mix\", Georgia, serif";

        public const int WeightLight = 300, WeightRegular = 400, WeightMedium = 500, WeightSemibold = 600, WeightBold = 700;

        // Type scale (px, base 12).
        public const int SizeBodyXS = 12, SizeBodySM = 14, SizeBodyMD = 15, SizeBodyLG = 18;
        public const int SizeLabelSM = 14, SizeLabelMD = 15;
        public const int SizeHeadingXS = 16, SizeHeadingSM = 18, SizeHeadingMD = 20, SizeHeadingLG = 24;
        public const int SizeDisplaySM = 28, SizeDisplayMD = 40, SizeDisplayLG = 64;

        public const double LeadingTight = 1.2, LeadingBody = 1.7, LeadingRelaxed = 1.9;
        public const double DisplayTrackingSM = -0.01, DisplayTrackingMD = -0.02, DisplayTrackingLG = -0.03;
    }

    // ================= SPACING / RADII / LAYOUT =================
    public static class Space
    {
        public const int S1 = 2, S2 = 4, S3 = 6, S4 = 8, S6 = 12, S8 = 16, S10 = 20, S12 = 24, S16 = 32, S20 = 40, S24 = 48, S32 = 64;
    }
    public static class Radius
    {
        public const int Xs = 4, Sm = 8, Md = 12, Lg = 20, Xl = 28, Full = 9999;
        public const int TalkBox = 8;   // orb + main talk box (Swift = 8, canon)
        public const int EditPane = 20;
    }
    public static class Layout
    {
        public const int ContentColumn = 980, PageHeaderTopInset = 44;
    }

    // ================= OPACITY + BLUR =================
    public static class Opacity
    {
        public static readonly (double light, double dark) PaperGrain = (0.035, 0.02);
        public const double AccentWash = 0.08;
        public static readonly (double light, double dark) Hairline = (0.14, 0.08);
        public const double ChunkDim = 0.34;
        public const double TokDel = 0.85;
        public static readonly (double light, double dark) Ambient = (0.38, 0.22);
    }
    public static class Blur
    {
        public const int Sat = 6, Hint = 4, EditPane = 8, Orb = 10, Menubar = 18, Wave = 3;
    }

    // ================= MOTION (global) =================
    public static class Motion
    {
        public const int DurFast = 120, DurBase = 200, DurSlow = 300, DurReveal = 700;
        public const double PulseDurS = 1.6;
        public const string EaseOut = "cubic-bezier(0.2, 0.8, 0.2, 1)";
        public const string EaseIn = "cubic-bezier(0.4, 0, 1, 1)";
        public const string EaseSoft = "cubic-bezier(0.16, 1, 0.3, 1)";
        public static readonly double[] OutPoints = { 0.2, 0.8, 0.2, 1 };
        public static readonly double[] InPoints = { 0.4, 0, 1, 1 };
        public static readonly double[] SoftPoints = { 0.16, 1, 0.3, 1 };
        public const double PressScale = 0.96, MicPressScale = 0.94;
    }

    // ================= COLOURS — CANON =================
    public static class Color
    {
        public static readonly ThemedColor Canvas = new("#F6F3EA", "#0C0E0C");
        public static readonly ThemedColor Surface1 = new("#FCFAF3", "#161616");
        public static readonly ThemedColor Surface2 = new("#FFFFFF", "#1E1E1E");
        public static readonly ThemedColor InkPrimary = new("#20241F", "#E9E7DD");
        public static readonly ThemedColor InkSecondary = new("#5C6454", "#A7A99E");
        public static readonly ThemedColor InkTertiary = new("#666D5F", "#84887D");
        public static readonly ThemedColor Accent = new("#41691E", "#8FCE6E");
        public static readonly ThemedColor AccentWash = new("rgba(65,105,30,0.08)", "rgba(143,206,110,0.08)");
        public static readonly ThemedColor Hairline = new("rgba(32,36,31,0.14)", "rgba(255,255,255,0.08)");
        public static readonly ThemedColor Annotation = new("#B98A2E", "#D8B85C");
        public static readonly ThemedColor CListening = new("#D05C1E", "#F8A86C");
        public static readonly ThemedColor CProcessing = new("#303CC8", "#788CFF");
        public static readonly ThemedColor CSettled = new("#5E7A4E", "#8FCE6E");
        public static readonly ThemedColor CError = new("#B81514", "#B81514");

        // R15: warm-tint dark is KEPT at #404948 (resolveKiviTheme), NOT KDS ledger.
        public static readonly ThemedColor WarmTint = new("#E7EEDD", "#404948");
        public static readonly ThemedColor LegGreen = new("#41691E", "#8FCE6E");
        public const string LegGreenDark = "#6EA335";
    }

    /// The two brand creams — kept DISTINCT, never collapsed.
    public static class Cream
    {
        public const string Legacy = "#F1F4EC"; // KDS.light.paper — legacy pages + orb light page
        public const string Canon = "#F6F3EA";   // Canon.light.canvas
    }
    /// Orb forest fill — rgb(13,30,9), NOT pure black.
    public const string ForestGreen = "#0D1E09";

    // ================= ORB — floating bar =================
    public static class Orb
    {
        public static class ThemeForest
        {
            public const string Fill = "#0D1E09";
            public static readonly int[] FillRGB = { 13, 30, 9 };
            public const double RestAlpha = 0.72;
            public const bool Invert = true;
            public const bool Glossy = false;
            public const string Eye = "#EAF0E2";
            public const string Glow = "#78B848";
            public static readonly int[] GlowRGB = { 120, 184, 72 };
        }
        public static class ThemeMist
        {
            public const string Fill = "#DFEAD1";
            public static readonly int[] FillRGB = { 223, 234, 209 };
            public const double RestAlpha = 0.66;
            public const bool Invert = false;
            public const bool Glossy = true;
            public const string Eye = "#1B330F";
            public const string Glow = "#B0D484";
            public static readonly int[] GlowRGB = { 176, 212, 132 };
        }

        /// Theme-invariant orb accents (DS.Accent).
        public static class Accent
        {
            public const string Idle = "#41691E", Listen = "#E6651B", Edit = "#385418";
            public const string Hint2Bg = "#294614", TooltipBg = "#18300F", TooltipFg = "#EAF0E2", HintClose = "#8C8F88";
            public const string CancelHover = "rgba(150,28,26,0.92)", CancelHoverMist = "rgba(216,95,30,0.95)";
        }
        public const string PillFill = "rgba(24,48,15,0.72)";

        /// Wave "thinking" sweep — SAME indigo for processing AND edit.
        public static class Wave
        {
            public const string Processing = "rgba(74,94,232,0.95)";
            public const string Edit = "rgba(74,94,232,0.95)";
            public const int BandPct = 46, Blur = 3;
            public const double ProcessingS = 2.6, EditS = 2.4;
        }

        /// Page glow (desktop background) — 4-layer box-shadow.
        public static class PageGlowLight
        {
            public static readonly int[] DropRGB = { 20, 20, 20 };
            public const double DropBase = 0.28, DropAdd = 0.12, GlowA = 0.12;
            public const int GlowBlur = 40, GlowSpread = 4;
        }
        public static class PageGlowDark
        {
            public static readonly int[] DropRGB = { 0, 0, 0 };
            public const double DropBase = 0.42, DropAdd = 0.16, GlowA = 0.40;
            public const int GlowBlur = 60, GlowSpread = 9;
        }

        /// Transcript box (lb-tx). box/card/outline are CANON surfaces.
        public static class Tx
        {
            public static readonly ThemedColor Box = new("#FCFAF3", "#161616");
            public static readonly ThemedColor Card = new("#EFECDF", "#20211E");
            public static readonly ThemedColor Outline = new("rgba(32,36,31,0.14)", "rgba(255,255,255,0.08)");
            public static readonly ThemedColor Base = new("#1A2710", "#ECEFE8");
            public static readonly ThemedColor Listen = new("#646E58", "#9AA192");
            public static readonly ThemedColor WaveText = new("#595E50", "#B3B8AC");
            public static readonly ThemedColor Del = new("#B81514", "#F0716F");
            public static readonly ThemedColor DelBg = new("rgba(184,21,20,0.10)", "rgba(240,113,111,0.14)");
            public static readonly ThemedColor Ins = new("#2F7D2E", "#8FD06A");
            // Diff/token styling
            public const int BodySize = 13;
            public const double LineHeight = 1.45;
            public const int BodyLineSpacing = 3, ChunkSpacing = 9;
            public const double ChunkDimOpacity = 0.34;
            public const int TokDelRadius = 3, TokDelPadH = 2;
            public const double TokDelOpacity = 0.85;
            public const int TokInsWeight = 600;
            public const double TokInsUnderlineAlpha = 0.45;
            public const int TokInsUnderlineOffset = 2, TokFinalWeight = 600;
        }

        /// Geometry (px) — DS.Geometry.
        public static class Geometry
        {
            public const double RestW = 39, RestH = 15, RestR = 7.5, WakeW = 61, WakeH = 61, WakeR = 30.5;
            public const double FlowTop = 50, FlowGap = 10, OrbZoneW = 62, OrbZoneH = 76, OrbCenterYInZone = 30.5, MarkSize = 65;
            public const double SatSize = 23;
            public const double SatEditX = -38, SatEditY = 14.25, SatEditSize = 32.5;
            public const double SatSettingsX = 67.5, SatSettingsY = 14.25, SatSettingsSize = 32.5;
            public const double SatSideSizeSmall = 21.5, SatSidePillSize = 21.5, SatSidePillIcon = 14.5, SatSidePillGap = 3.5, SatExpandPillY = 26.5;
            public const double PillTakeW = 57, PillTakeH = 18;
            public const double SatSideIcon = 17.5, SatSideIconSmall = 14.5;
            public const double SatExpandX = 19.5, SatExpandY = 64, SatExpandSize = 23;
            public const double DragHandleX = 0, DragHandleY = -19, DragHandleHitW = 28, DragHandleHitH = 20;
            public const double EditPaneWidth = 212, EditPaneTop = -6, EditPaneRightGap = 8, EditPaneRadius = 20, EditPanePadding = 7;
            public const double HintGap = 7, HintPadV = 4, HintPadH = 12, HintCloseX = -6, HintCloseY = -6, HintCloseSize = 15, HintCloseBorder = 1.5;
            public const double HintKeyRadius = 6, HintKeyMinWidth = 17, Hint2PadV = 4, Hint2PadH = 11;
            public const double TxBoxW = 322, TxBoxH = 108, TxBoxRadius = 8, TxBoxPadTop = 14, TxBoxPadRight = 34, TxBoxPadBottom = 14, TxBoxPadLeft = 52;
            public const double BoxWedgeW = 20, BoxWedgeH = 9, BoxWedgeGap = 3;
            public const double TxBoxMinW = 322, TxBoxMaxW = 640, TxBoxMinH = 108, TxBoxMaxH = 360;
            public const double TxWrapTop = -23, FlowShift = 159, TxBandWidth = 32;
            public const double TxHeaderBlockH = 44, TxFooterBlockH = 56, TxReadingPad = 22;
            public const double BridgeW = 172, BridgeH = 66, BridgeTop = 24;
            public const double BridgeClipTopLeftPct = 33, BridgeClipTopRightPct = 67, BridgeClipBottomRightPct = 97, BridgeClipBottomLeftPct = 3;
            public const double ToastTop = 104;
        }

        /// Motion — per-frame lerp factors k and durations (ms unless suffixed). DS.Motion.
        public static class MotionTokens
        {
            public const double WakeLerp = 0.20, CollapseLerpBase = 0.16, CollapseLerpAdd = 0.24;
            public const double ExpandLerp = 0.18, SatFadeLerp = 0.16, EditFadeLerp = 0.12, PaneOpenLerp = 0.28, Hint2Lerp = 0.18;
            public const double MarkLerp = 0.12, MarkBreathColorLerp = 0.14, SphereLightLerp = 0.16;
            public const double BreathPeriodS = 2.6;
            public const int DotsMs = 600, ChunkFadeMs = 240, WaitingTimeoutMs = 3000;
            public const int ProcessingMs = 2000, DoneHoldMs = 2000, EditApplyMs = 1700, EditedHoldMs = 1100;
            public const int DiffMs = 520, DiffHoldMs = 1050, DiffSettleMs = 620;
            public const double WaveProcessingS = 2.6, WaveEditS = 2.4;
            public const int WaveBandPct = 46, WaveBlur = 3;
            public const int PopDimMs = 2600, HoldUntilDoneMs = 3000, HoldUntilCancelMs = 1800, HoldUntilCollapseClickMs = 1200, HoldUntilOutsideClickMs = 350;
            public const int HoverInPx = 44, HoverOutPx = 54, GroupLeaveMs = 150, PaneLeaveMs = 280, SatLeaveMs = 500;
            public const int HoldMs = 420, DoubleTapMs = 450;
            public const int BotHideMs = 2600, EditHideDoneMs = 3000, EditHideHoverMs = 4000, EditHidePasteMs = 9000, ExpFaintMs = 4000;
            public const int ToastMs = 1500, CopiedMs = 1100, ShakeMs = 450, TbShakeMs = 300;
            public const double PressScale = 0.95;
            public const int DropPx = -6;
        }

        // Orb page (desktop background) theme.
        public static class Page
        {
            public static readonly ThemedColor Paper = new("#F1F4EC", "#121512");
            public static readonly ThemedColor Paper2 = new("#FFFFFF", "#1B1F1A");
            public static readonly ThemedColor Fg1 = new("#141414", "#ECEFE8");
            public static readonly ThemedColor Fg2 = new("#666666", "#C9CDC0");
            public static readonly ThemedColor Fg3 = new("#999999", "#7D8278");
            public static readonly ThemedColor Border1 = new("rgb(240,240,240)", "rgba(255,255,255,0.12)");
            public static readonly ThemedColor Border2 = new("rgb(230,230,230)", "rgba(255,255,255,0.10)");
            public static readonly ThemedColor WarmTint = new("#E7EEDD", "rgba(255,255,255,0.06)");
        }
    }

    // ================= PAPER GRAIN =================
    public static class PaperGrain
    {
        public const int TileSize = 128;
        public const string Seed = "0x4B49564950415045"; // fixed LCG seed
        public static readonly (double light, double dark) OpacityByMood = (0.035, 0.02);
        public const double DarkScale = 1.5;
        public const string Tint = "inkPrimary";
    }
}
