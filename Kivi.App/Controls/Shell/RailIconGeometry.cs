using System.Collections.Generic;
using System.Windows.Media;
using Kivi.App.ViewModels;

namespace Kivi.App.Controls.Shell;

/// <summary>
/// The hand-drawn 24x24 2px-monoline rail glyphs, ported VERBATIM from
/// main-window/RailIcons.tsx (itself from KiviApp/Shell/RailIcons.swift). Each is
/// a stroked PathGeometry (round caps/joins) drawn at StrokeThickness 2 in a 24x24 box.
/// Some glyphs combine several sub-paths / primitives into one GeometryGroup.
/// </summary>
public static class RailIconGeometry
{
    private static readonly Dictionary<RailIconName, Geometry> _cache = new();

    public static Geometry For(RailIconName name)
    {
        if (_cache.TryGetValue(name, out var hit)) return hit;
        var g = Build(name);
        g.Freeze();
        _cache[name] = g;
        return g;
    }

    private static Geometry Build(RailIconName name) => name switch
    {
        // micDot: capsule rect(9,3,6,11 rx3) + bowl arc + stand + centre dot
        RailIconName.MicDot => Group(
            RoundRect(9, 3, 6, 11, 3),
            Path("M6 11 A6 6 0 0 0 18 11"),
            Path("M12 17 L12 21"),
            Circle(12, 8.5, 0.9)),

        // clock: circle r8.2 + hands
        RailIconName.Clock => Group(
            Circle(12, 12, 8.2),
            Path("M12 7.2 L12 12 L15.4 14")),

        // sparkle: four-point star (closed)
        RailIconName.Sparkle => Path("M12 4 L13.6 10.4 L20 12 L13.6 13.6 L12 20 L10.4 13.6 L4 12 L10.4 10.4 Z"),

        // bolt
        RailIconName.Bolt => Path("M13 3 L6 13 L11 13 L10 21 L18 10 L13 10 Z"),

        // brush
        RailIconName.Brush => Path("M19.5 4.5 L12 12 M12 12 Q9.4 14.6 5.2 18.8 Q6.4 17.6 9.2 15.2"),

        // bars (three rising bars)
        RailIconName.Bars => Path("M6 19 L6 13 M12 19 L12 6 M18 19 L18 10"),

        // layers: two offset rounded squares
        RailIconName.Layers => Group(
            RoundRect(4.5, 7.5, 11, 11, 2.4),
            RoundRect(8.5, 4, 11, 11, 2.4)),

        // trophy: cup + two handles + stem/crossbar/plinth
        RailIconName.Trophy => Group(
            RoundRect(8, 4.5, 8, 8.5, 2.2),
            Path("M8 6.5 Q4.7 6.8 4.8 10.2 Q5.6 13 8.8 12.2"),
            Path("M16 6.5 Q19.3 6.8 19.2 10.2 Q18.4 13 15.2 12.2"),
            Path("M12 13 L12 17 M8.2 19.5 L15.8 19.5 M10 17 L14 17")),

        // gear: hub + ring + eight teeth
        RailIconName.Gear => Group(
            Circle(12, 12, 6),
            Circle(12, 12, 2.5),
            Path("M18 12 L20.8 12 M16.243 16.243 L18.222 18.222 M12 18 L12 20.8 " +
                 "M7.757 16.243 L5.778 18.222 M6 12 L3.2 12 M7.757 7.757 L5.778 5.778 " +
                 "M12 6 L12 3.2 M16.243 7.757 L18.222 5.778")),

        _ => Path("M0 0"),
    };

    /// <summary>The collapse-toggle glyph (sidebar.leading): a panel with a rail. Stroke 1.7.</summary>
    public static Geometry SidebarToggle() => Group(
        RoundRect(3, 4.5, 18, 15, 2.5),
        Path("M9 4.5 L9 19.5"));

    /// <summary>Search magnifier. Stroke 1.8.</summary>
    public static Geometry Search() => Group(
        Circle(10.5, 10.5, 6.5),
        Path("M15.4 15.4 L20 20"));

    private static Geometry Path(string d) => Geometry.Parse(d);

    private static Geometry Circle(double cx, double cy, double r) =>
        new EllipseGeometry(new System.Windows.Point(cx, cy), r, r);

    private static Geometry RoundRect(double x, double y, double w, double h, double r) =>
        new RectangleGeometry(new System.Windows.Rect(x, y, w, h), r, r);

    private static Geometry Group(params Geometry[] parts)
    {
        var g = new GeometryGroup();
        foreach (var p in parts) g.Children.Add(p);
        return g;
    }
}
