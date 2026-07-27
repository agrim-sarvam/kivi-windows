using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace Kivi.App.Drawing;

/// <summary>
/// The deterministic 128×128 paper-grain noise tile, ported verbatim from
/// src/renderer/src/orb/render/paperGrain.ts (KiviKit/DesignKit/PaperGrain.swift):
/// a 64-bit LCG seeded 0x4B49564950415045; per-pixel alpha = the high byte of the state.
/// Tinted ink-primary; dark tile drawn at 1.5× scale. Overall opacity 0.035 light / 0.02 dark.
/// </summary>
internal static class PaperGrain
{
    private const ulong Mul = 6364136223846793005UL;
    private const ulong Inc = 1442695040888963407UL;
    private const ulong Seed = 0x4B49564950415045UL;
    public const int Tile = 128;

    private static byte[]? _noise;

    private static byte[] NoiseTile()
    {
        if (_noise != null) return _noise;
        var a = new byte[Tile * Tile];
        ulong state = Seed;
        for (int i = 0; i < a.Length; i++)
        {
            state = state * Mul + Inc; // wraps at 64 bits like the TS BigInt & MASK
            a[i] = (byte)((state >> 56) & 0xFF);
        }
        _noise = a;
        return a;
    }

    private static readonly Dictionary<bool, Bitmap> _cache = new();

    /// <summary>A 128×128 ARGB tile: ink RGB with noise alpha. dark=true uses ink #E9E7DD, else #20241F.</summary>
    public static Bitmap TileBitmap(bool dark)
    {
        if (_cache.TryGetValue(dark, out var hit)) return hit;
        var ink = dark ? (0xE9, 0xE7, 0xDD) : (0x20, 0x24, 0x1F);
        var n = NoiseTile();
        var bmp = new Bitmap(Tile, Tile, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, Tile, Tile), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        unsafe
        {
            byte* p = (byte*)data.Scan0;
            for (int i = 0; i < n.Length; i++)
            {
                // GDI+ Format32bppArgb is BGRA in memory; store non-premultiplied here (we paint at low opacity).
                p[i * 4 + 0] = (byte)ink.Item3; // B
                p[i * 4 + 1] = (byte)ink.Item2; // G
                p[i * 4 + 2] = (byte)ink.Item1; // R
                p[i * 4 + 3] = n[i];            // A
            }
        }
        bmp.UnlockBits(data);
        _cache[dark] = bmp;
        return bmp;
    }

    public static double Opacity(bool dark) => dark ? 0.02 : 0.035;
    public static double Scale(bool dark) => dark ? 1.5 : 1.0;
}
