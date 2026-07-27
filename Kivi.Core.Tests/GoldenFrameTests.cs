// Golden-frame parity tests — mirror _reference/.../test/orb-core.golden.spec.ts.
// Per-field tolerance policy: EXACT on discrete/enum/bool/quantized fields; DRIFT-BUDGET
// bound on eased continuous scalars. Tested at the 16ms (60Hz) cadence the goldens were
// exported at; a separate multi-rate test exercises 24/30/60 Hz determinism.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Kivi.Core.Orb;
using Xunit;
using Xunit.Abstractions;

namespace Kivi.Core.Tests;

public sealed class GoldenFrameTests
{
    private readonly ITestOutputHelper _out;
    public GoldenFrameTests(ITestOutputHelper output) => _out = output;

    private static readonly string GoldenDir =
        Path.Combine(AppContext.BaseDirectory, "golden-frames");

    // eased (continuous) leaf field names — everything else is discrete/exact.
    private static readonly HashSet<string> Eased = new()
    {
        "now", "breath", "open", "orbWidth", "orbHeight", "orbRadius", "drop", "press",
        "fillAlpha", "backdropBlur", "blur", "spread", "yOffset", "alpha", "markOpacity",
        "sphereOpacity", "lightX", "lightY", "eyeScale", "eyeOpacity", "eyeOpen",
        "hintOpacity", "hintRise", "hint2Opacity", "hint2Rise", "selectionPillOpacity",
        "selectionPillWidth", "pillPop", "satSettingsOpacity", "satSettingsScale",
        "satExpandOpacity", "satExpandScale", "satEditOpacity", "satEditScale",
        "satEditShakeX", "orbShakeX", "satCancelOpacity", "satCancelScale", "paneOpacity",
        "paneScale", "paneShiftX", "exp", "flowShiftX", "txWrapWidth", "txWrapHeight",
        "txOpacity", "boxW", "boxH", "boxGrowUp", "boxShakeX", "glowRadius", "glowAlpha",
        "landing", "landingEased", "collapse", "fadeInStart",
    };

    private const double EasedAbsTol = 1e-4;
    private const double EasedRelTol = 1e-6;
    private const double ExactNumTol = 1e-9;

    private sealed class Stats
    {
        public int DiscreteChecked, DiscreteFail, EasedChecked, EasedFail;
        public double EasedMaxDelta;
        public string EasedMaxField = "";
        public List<string> Failures = new();
    }

    private static void Compare(string path, string leaf, object? mine, JsonElement gold, int frameIdx, Stats st)
    {
        // arrays (txLines / tokens)
        if (gold.ValueKind == JsonValueKind.Array)
        {
            st.DiscreteChecked++;
            var mineList = mine as System.Collections.IList;
            var goldLen = gold.GetArrayLength();
            if (mineList == null || mineList.Count != goldLen)
            {
                st.DiscreteFail++;
                if (st.Failures.Count < 30) st.Failures.Add($"f{frameIdx} {path}: array len {mineList?.Count} != {goldLen}");
                return;
            }
            int idx = 0;
            foreach (var ge in gold.EnumerateArray())
            {
                Compare($"{path}[{idx}]", leaf, mineList[idx], ge, frameIdx, st);
                idx++;
            }
            return;
        }
        // nested objects
        if (gold.ValueKind == JsonValueKind.Object)
        {
            var m = mine as IDictionary<string, object?> ?? new Dictionary<string, object?>();
            foreach (var prop in gold.EnumerateObject())
            {
                m.TryGetValue(prop.Name, out var mv);
                Compare($"{path}.{prop.Name}", prop.Name, mv, prop.Value, frameIdx, st);
            }
            return;
        }
        // leaf
        var eased = Eased.Contains(leaf);
        if (eased)
        {
            var goldIsNull = gold.ValueKind == JsonValueKind.Null;
            if (goldIsNull || mine == null)
            {
                st.DiscreteChecked++;
                var bothNull = goldIsNull && mine == null;
                if (!bothNull)
                {
                    st.DiscreteFail++;
                    if (st.Failures.Count < 30) st.Failures.Add($"f{frameIdx} {path}: null-mismatch mine={mine} gold={(goldIsNull ? "null" : gold.ToString())}");
                }
                return;
            }
            st.EasedChecked++;
            var a = ToDouble(mine);
            var g = gold.GetDouble();
            var delta = Math.Abs(a - g);
            var rel = delta / Math.Max(1, Math.Abs(g));
            if (delta > st.EasedMaxDelta)
            {
                st.EasedMaxDelta = delta;
                st.EasedMaxField = $"{path} (mine={a} gold={g})";
            }
            if (!(delta <= EasedAbsTol || rel <= EasedRelTol))
            {
                st.EasedFail++;
                if (st.Failures.Count < 30) st.Failures.Add($"f{frameIdx} {path}: eased Δ={delta} mine={a} gold={g}");
            }
            return;
        }
        // discrete/exact
        st.DiscreteChecked++;
        bool ok = CompareDiscrete(mine, gold);
        if (!ok)
        {
            st.DiscreteFail++;
            if (st.Failures.Count < 30) st.Failures.Add($"f{frameIdx} {path}: discrete mine={Fmt(mine)} gold={gold}");
        }
    }

    private static double ToDouble(object o) => o switch
    {
        double d => d,
        int i => i,
        float fl => fl,
        long l => l,
        _ => Convert.ToDouble(o),
    };

    private static string Fmt(object? o) => o == null ? "null" : o.ToString() ?? "";

    private static bool CompareDiscrete(object? mine, JsonElement gold)
    {
        switch (gold.ValueKind)
        {
            case JsonValueKind.Null:
                return mine == null;
            case JsonValueKind.True:
            case JsonValueKind.False:
                return mine is bool b && b == gold.GetBoolean();
            case JsonValueKind.Number:
                if (mine == null) return false;
                return Math.Abs(ToDouble(mine) - gold.GetDouble()) <= ExactNumTol;
            case JsonValueKind.String:
                return mine is string s && s == gold.GetString();
            default:
                return false;
        }
    }

    private Stats Verify(string name, List<FlowFrame> frames)
    {
        var golden = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(GoldenDir, $"{name}.json")));
        var st = new Stats();
        var goldLen = golden.GetArrayLength();
        Assert.True(frames.Count == goldLen, $"{name}: frame count mine={frames.Count} gold={goldLen}");
        var goldArr = golden.EnumerateArray().ToList();
        var n = Math.Min(frames.Count, goldLen);
        for (int i = 0; i < n; i++)
        {
            var mine = FrameToPlain.Convert(frames[i]);
            var gold = goldArr[i];
            foreach (var prop in gold.EnumerateObject())
            {
                mine.TryGetValue(prop.Name, out var mv);
                Compare(prop.Name, prop.Name, mv, prop.Value, i, st);
            }
        }
        var discretePass = st.DiscreteChecked - st.DiscreteFail;
        var easedPass = st.EasedChecked - st.EasedFail;
        _out.WriteLine(
            $"[{name}] frames={frames.Count}\n" +
            $"  discrete (exact): {discretePass}/{st.DiscreteChecked} pass ({100.0 * discretePass / st.DiscreteChecked:F4}%)\n" +
            $"  eased    (float): {easedPass}/{st.EasedChecked} pass ({100.0 * easedPass / st.EasedChecked:F4}%)  maxΔ={st.EasedMaxDelta:E3} @ {st.EasedMaxField}");
        if (st.Failures.Count > 0)
            _out.WriteLine("  first failures:\n    " + string.Join("\n    ", st.Failures.Take(20)));
        return st;
    }

    [Fact]
    public void MatchesDictationOracle()
    {
        var st = Verify("dictation", GoldenTimelines.Dictation());
        Assert.True(st.DiscreteFail == 0, $"dictation: {st.DiscreteFail} discrete field mismatches");
        Assert.True(st.EasedFail == 0, $"dictation: {st.EasedFail} eased field mismatches");
    }

    [Fact]
    public void MatchesEditOracle()
    {
        var st = Verify("edit", GoldenTimelines.Edit());
        Assert.True(st.DiscreteFail == 0, $"edit: {st.DiscreteFail} discrete field mismatches");
        Assert.True(st.EasedFail == 0, $"edit: {st.EasedFail} eased field mismatches");
    }

    [Fact]
    public void MatchesCollapsedDemoOracle()
    {
        var st = Verify("dictation-collapsed-demo", GoldenTimelines.CollapsedDemo());
        Assert.True(st.DiscreteFail == 0, $"collapsed-demo: {st.DiscreteFail} discrete field mismatches");
        Assert.True(st.EasedFail == 0, $"collapsed-demo: {st.EasedFail} eased field mismatches");
    }
}
