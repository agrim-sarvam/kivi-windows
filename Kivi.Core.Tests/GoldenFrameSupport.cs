// Golden-frame test support: the deterministic 16ms EngineHarness, the three scripted
// timelines (ported verbatim from _reference/.../test/orb-core.golden.spec.ts, itself a
// port of the in-project Swift GoldenFrameExportTests), and FrameToPlain — the FlowFrame →
// golden-shape object encoder (matches the MANIFEST's 112-field schema + key order).
using System.Collections.Generic;
using System.Linq;
using Kivi.Core.Orb;

namespace Kivi.Core.Tests;

/// 1:1 port of Tests/NativeTests/TestSupport.swift EngineHarness
/// (16ms injected clock, MemoryFlowStore, random { 0.5 }).
public sealed class EngineHarness
{
    public MemoryFlowStore Store;
    public FlowEngine Engine;
    public double NowMs = 0;
    public FlowFrame Last = new();

    public EngineHarness(IDictationService? dictation = null, IEditService? edit = null)
    {
        Store = new MemoryFlowStore();
        Engine = new FlowEngine(store: Store, random: () => 0.5, dictation: dictation, edit: edit);
    }

    public FlowFrame Tick(int n = 1)
    {
        for (int i = 0; i < n; i++)
        {
            NowMs += 16;
            Last = Engine.Step(NowMs);
        }
        return Last;
    }
}

public static class GoldenTimelines
{
    public static List<FlowFrame> Dictation()
    {
        var dict = new ScriptedDictationService();
        var edt = new ScriptedEditService();
        var h = new EngineHarness(dict, edt);
        h.Engine.Apply(FlowSettings.Default());
        h.Engine.SetExpanded(true);

        var frames = new List<FlowFrame>();
        void Rec() => frames.Add(h.Tick());

        for (int i = 0; i < 12; i++) Rec(); // now -> 192

        h.Engine.OrbPointerDown(); // pressStart = 192
        dict.Emit(new DictationEvent.Opened("gold"));
        Rec(); // 208
        dict.Emit(new DictationEvent.SpeechStart());
        Rec(); // 224
        for (int i = 0; i < 4; i++) Rec();

        dict.Emit(new DictationEvent.Segment(0, "hey are we still on for lunch today"));
        for (int i = 0; i < 8; i++) Rec();
        dict.Emit(new DictationEvent.Segment(1, "maybe around one at the usual place"));
        for (int i = 0; i < 8; i++) Rec();

        while (h.NowMs < 672) Rec();
        h.Engine.PointerUp(); // -> processing
        for (int i = 0; i < 20; i++) Rec();

        dict.Emit(new DictationEvent.Final(new TakeResult
        {
            RawSegments = new() { "hey are we still on for lunch today", "maybe around one at the usual place" },
            FinalLines = new() { "Hey, are we still on for lunch today?", "Maybe around one, at the usual place." },
        }));
        Rec();
        for (int i = 0; i < 10; i++) Rec();
        for (int i = 0; i < 110; i++) Rec();
        return frames;
    }

    public static List<FlowFrame> Edit()
    {
        var dict = new ScriptedDictationService();
        var edt = new ScriptedEditService();
        var h = new EngineHarness(dict, edt);
        h.Engine.Apply(FlowSettings.Default());
        h.Engine.SetExpanded(true);

        var frames = new List<FlowFrame>();
        void Rec() => frames.Add(h.Tick());

        for (int i = 0; i < 10; i++) Rec(); // now -> 160

        h.Engine.OrbPointerDown(); // pressStart = 160
        dict.Emit(new DictationEvent.Opened("gold"));
        Rec();
        dict.Emit(new DictationEvent.SpeechStart());
        Rec();
        dict.Emit(new DictationEvent.Segment(0, "lets grab coffee sometime this week"));
        for (int i = 0; i < 4; i++) Rec();
        while (h.NowMs < 640) Rec();
        h.Engine.PointerUp();
        for (int i = 0; i < 20; i++) Rec();
        dict.Emit(new DictationEvent.Final(new TakeResult
        {
            RawSegments = new() { "lets grab coffee sometime this week" },
            FinalLines = new() { "Let's grab coffee sometime this week." },
        }));
        Rec();
        for (int i = 0; i < 14; i++) Rec();

        h.Engine.EditClick(); // idle + canEdit -> startVoiceEdit -> editListen
        dict.Emit(new DictationEvent.Opened("gold-edit"));
        Rec();
        dict.Emit(new DictationEvent.SpeechStart());
        Rec();
        dict.Emit(new DictationEvent.Segment(0, "make it more formal"));
        for (int i = 0; i < 8; i++) Rec();

        h.Engine.EditClick(); // apply tap -> runEditProcess -> editProcess
        Rec();
        dict.Emit(new DictationEvent.Final(new TakeResult
        {
            RawSegments = new() { "make it more formal" },
            FinalLines = new() { "make it more formal" },
        }));
        for (int i = 0; i < 6; i++) Rec();

        edt.Emit(new EditOutcome.Ok(new EditResult { Lines = new() { "Would you be available to meet for coffee this week?" } }));
        Rec();
        for (int i = 0; i < 10; i++) Rec();
        for (int i = 0; i < 60; i++) Rec();
        return frames;
    }

    public static List<FlowFrame> CollapsedDemo()
    {
        var h = new EngineHarness(); // demo dictation + edit, random { 0.5 }
        h.Engine.Apply(FlowSettings.Default()); // collapsed (defaultExpansion == collapsed)

        var frames = new List<FlowFrame>();
        void Rec() => frames.Add(h.Tick());

        for (int i = 0; i < 8; i++) Rec(); // rest baseline (now -> 128)
        h.Engine.OrbPointerDown(); // pressStart = 128 -> listening
        for (int i = 0; i < 40; i++) Rec(); // demo speechStart at +250; hold past holdMs
        h.Engine.PointerUp(); // now >= 768 -> processing; demo final at +2000
        for (int i = 0; i < 140; i++) Rec();
        for (int i = 0; i < 80; i++) Rec();
        return frames;
    }
}

/// FlowFrame → golden-shape plain object (matches the FrameJSON.encode field set + key order).
public static class FrameToPlain
{
    private static Dictionary<string, object?> Shadow(ShadowSpec s) => new()
    {
        ["blur"] = s.Blur,
        ["spread"] = s.Spread,
        ["yOffset"] = s.YOffset,
        ["alpha"] = s.Alpha,
    };

    private static object SatTintObj(SatTint t)
    {
        if (t.Type == SatTintType.None) return new Dictionary<string, object?> { ["type"] = "none" };
        return new Dictionary<string, object?>
        {
            ["type"] = t.Type == SatTintType.Green ? "green" : "blue",
            ["r"] = t.R,
            ["g"] = t.G,
            ["b"] = t.B,
            ["glowRadius"] = t.GlowRadius,
            ["glowAlpha"] = t.GlowAlpha,
        };
    }

    private static object? DiffProgressObj(DiffProgress? d) => d == null ? null : new Dictionary<string, object?>
    {
        ["landing"] = d.Value.Landing,
        ["landingEased"] = d.Value.LandingEased,
        ["collapse"] = d.Value.Collapse,
    };

    private static object? ScrollObj(ScrollCommand? c) => c == null ? null : new Dictionary<string, object?>
    {
        ["id"] = c.Value.Id,
        ["target"] = c.Value.Target == ScrollTarget.Top ? "top" : "bottom",
    };

    private static object LineObj(TxLine l)
    {
        if (l.Role == TxLineRole.Tokens)
        {
            return new Dictionary<string, object?>
            {
                ["role"] = "tokens",
                ["tokens"] = (l.Tokens ?? new()).Select(t => (object)new Dictionary<string, object?>
                {
                    ["kind"] = t.Kind.RawValue(),
                    ["text"] = t.Text,
                }).ToList(),
                ["text"] = l.Text,
                ["fadeInStart"] = l.FadeInStart,
            };
        }
        return new Dictionary<string, object?>
        {
            ["role"] = l.Role.RawValue(),
            ["text"] = l.Text,
            ["fadeInStart"] = l.FadeInStart,
        };
    }

    private static Dictionary<string, object?> SettingsObj(FlowSettings s) => new()
    {
        ["page"] = s.Page.RawValue(),
        ["orb"] = s.Orb.RawValue(),
        ["orbSize"] = s.OrbSize.RawValue(),
        ["tooltips"] = s.Tooltips,
        ["defaultExpansion"] = s.DefaultExpansion.RawValue(),
        ["movable"] = s.Movable,
        ["defaultPosition"] = s.DefaultPosition.RawValue(),
        ["reduceMotion"] = s.ReduceMotion,
        ["haptics"] = s.Haptics,
        ["sounds"] = s.Sounds,
    };

    public static Dictionary<string, object?> Convert(FlowFrame f) => new()
    {
        ["now"] = f.Now,
        ["breath"] = f.Breath,
        ["phase"] = f.Phase.RawValue(),
        ["markState"] = f.MarkState.RawValue(),
        ["inverted"] = f.Inverted,
        ["open"] = f.Open,
        ["orbWidth"] = f.OrbWidth,
        ["orbHeight"] = f.OrbHeight,
        ["orbRadius"] = f.OrbRadius,
        ["drop"] = f.Drop,
        ["press"] = f.Press,
        ["fillAlpha"] = f.FillAlpha,
        ["backdropBlur"] = f.BackdropBlur,
        ["glowCore"] = Shadow(f.GlowCore),
        ["glowHalo"] = Shadow(f.GlowHalo),
        ["glowColor"] = new Dictionary<string, object?> { ["r"] = f.GlowColor.R, ["g"] = f.GlowColor.G, ["b"] = f.GlowColor.B },
        ["dropShadow"] = Shadow(f.DropShadow),
        ["markOpacity"] = f.MarkOpacity,
        ["sphereOpacity"] = f.SphereOpacity,
        ["lightX"] = f.LightX,
        ["lightY"] = f.LightY,
        ["eyeScale"] = f.EyeScale,
        ["eyeOpacity"] = f.EyeOpacity,
        ["eyeOpen"] = f.EyeOpen,
        ["hotkeyLabel"] = f.HotkeyLabel,
        ["editComboLabel"] = f.EditComboLabel,
        ["hint"] = new Dictionary<string, object?> { ["text"] = f.Hint.Text, ["showsKey"] = f.Hint.ShowsKey, ["accent"] = f.Hint.Accent.RawValue() },
        ["hintOpacity"] = f.HintOpacity,
        ["hintRise"] = f.HintRise,
        ["hintInteractive"] = f.HintInteractive,
        ["hintForced"] = f.HintForced,
        ["hint2Opacity"] = f.Hint2Opacity,
        ["hint2Rise"] = f.Hint2Rise,
        ["hint2Verb"] = f.Hint2Verb,
        ["selectionPillText"] = f.SelectionPillText,
        ["selectionPillAppBundleID"] = f.SelectionPillAppBundleID,
        ["selectionPillOpacity"] = f.SelectionPillOpacity,
        ["selectionPillWidth"] = f.SelectionPillWidth,
        ["pillPop"] = f.PillPop,
        ["satSettingsOpacity"] = f.SatSettingsOpacity,
        ["satSettingsScale"] = f.SatSettingsScale,
        ["satExpandOpacity"] = f.SatExpandOpacity,
        ["satExpandScale"] = f.SatExpandScale,
        ["satBottomInteractive"] = f.SatBottomInteractive,
        ["satEditShown"] = f.SatEditShown,
        ["satEditOpacity"] = f.SatEditOpacity,
        ["satEditScale"] = f.SatEditScale,
        ["satEditShakeX"] = f.SatEditShakeX,
        ["satEditAppBundleID"] = f.SatEditAppBundleID,
        ["txWordCount"] = f.TxWordCount,
        ["orbShakeX"] = f.OrbShakeX,
        ["satEditTint"] = SatTintObj(f.SatEditTint),
        ["satCancelOpacity"] = f.SatCancelOpacity,
        ["satManualCopy"] = f.SatManualCopy,
        ["satManualCopyHot"] = f.SatManualCopyHot,
        ["satCancelScale"] = f.SatCancelScale,
        ["satCancelInteractive"] = f.SatCancelInteractive,
        ["satEditLocked"] = f.SatEditLocked,
        ["satSettingsLocked"] = f.SatSettingsLocked,
        ["paneOpacity"] = f.PaneOpacity,
        ["paneScale"] = f.PaneScale,
        ["paneShiftX"] = f.PaneShiftX,
        ["exp"] = f.Exp,
        ["expanded"] = f.Expanded,
        ["flowShiftX"] = f.FlowShiftX,
        ["txWrapWidth"] = f.TxWrapWidth,
        ["txWrapHeight"] = f.TxWrapHeight,
        ["txWrapClips"] = f.TxWrapClips,
        ["txClipped"] = f.TxClipped,
        ["txOpacity"] = f.TxOpacity,
        ["txInteractive"] = f.TxInteractive,
        ["boxW"] = f.BoxW,
        ["boxH"] = f.BoxH,
        ["boxGrowUp"] = f.BoxGrowUp,
        ["boxMaxi"] = f.BoxMaxi,
        ["boxCanMaxi"] = f.BoxCanMaxi,
        ["boxOnLeft"] = f.BoxOnLeft,
        ["flipY"] = f.FlipY,
        ["txStage"] = f.TxStage.RawValue(),
        ["txLines"] = f.TxLines.Select(LineObj).ToList(),
        ["txDots"] = f.TxDots,
        ["txAwaitingSpeech"] = f.TxAwaitingSpeech,
        ["txWaitingPhase"] = f.TxWaitingPhase,
        ["txNotice"] = f.TxNotice,
        ["txBanner"] = f.TxBanner,
        ["diffProgress"] = DiffProgressObj(f.DiffProgress),
        ["scrollCommand"] = ScrollObj(f.ScrollCommand),
        ["txEditable"] = f.TxEditable,
        ["txEditorSeed"] = f.TxEditorSeed,
        ["hoveredTarget"] = f.HoveredTarget?.RawValue(),
        ["bandHistOn"] = f.BandHistOn,
        ["bandHistDim"] = f.BandHistDim,
        ["bandHistShake"] = f.BandHistShake,
        ["bandNoSteps"] = f.BandNoSteps,
        ["bandStepsDim"] = f.BandStepsDim,
        ["bandCanPrev"] = f.BandCanPrev,
        ["bandCanNext"] = f.BandCanNext,
        ["txPagerIndex"] = f.TxPagerIndex,
        ["txPagerCount"] = f.TxPagerCount,
        ["takeHostAppBundleID"] = f.TakeHostAppBundleID,
        ["retryOffered"] = f.RetryOffered,
        ["takeRating"] = f.TakeRating,
        ["takeRatable"] = f.TakeRatable,
        ["hasEditChain"] = f.HasEditChain,
        ["editContextKind"] = f.EditContextKind,
        ["editContextPreview"] = f.EditContextPreview,
        ["copyFlash"] = f.CopyFlash,
        ["copyHint"] = f.CopyHint,
        ["boxShakeX"] = f.BoxShakeX,
        ["toastText"] = f.ToastText,
        ["toastVisible"] = f.ToastVisible,
        ["settings"] = SettingsObj(f.Settings),
    };
}
