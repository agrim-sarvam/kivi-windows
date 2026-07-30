using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kivi.App.Services;

/// <summary>
/// Resolves an application's icon (from its executable path) to a WPF <see cref="ImageSource"/>
/// for display in the History page (~20px per-row app icon).
/// </summary>
public interface IAppIconResolver
{
    /// <summary>
    /// Returns the app's icon (from its exe path) as a frozen WPF <see cref="ImageSource"/>, or
    /// <c>null</c> if the path is null/empty/missing or extraction fails. Cached by exe path
    /// (case-insensitive).
    /// </summary>
    ImageSource? Resolve(string? exePath);
}

/// <summary>
/// REAL app-icon resolver. Extracts a file's associated shell icon via <c>SHGetFileInfo</c>
/// (<c>SHGFI_ICON | SHGFI_LARGEICON</c>, which yields the crisper 32px variant) and converts the
/// native <c>HICON</c> to a frozen WPF <see cref="BitmapSource"/>.
///
/// Icon extraction is expensive (it hits the shell/disk), so every result — including negative
/// (null) results for missing/unreadable exes — is memoized in a case-insensitive concurrent cache
/// keyed by exe path. Freezing the bitmap makes it safe to hand to any thread / bind from any page.
///
/// This service never throws: any bad input or extraction failure resolves to <c>null</c>.
/// </summary>
public sealed class AppIconResolver : IAppIconResolver
{
    // Cache negative results too, so a missing/blocked exe isn't re-probed on every list render.
    private readonly ConcurrentDictionary<string, ImageSource?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public ImageSource? Resolve(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        return _cache.GetOrAdd(exePath, Extract);
    }

    private static ImageSource? Extract(string exePath)
    {
        try
        {
            // Missing file → null (cached). QueryFullProcessImageName paths can go stale.
            if (!File.Exists(exePath))
                return null;

            var info = new SHFILEINFO();
            IntPtr result = SHGetFileInfo(
                exePath,
                0,
                ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;

            try
            {
                // Build the WPF bitmap from the native HICON, then freeze so it can cross threads.
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                // Release the GDI handle SHGetFileInfo created for us — the frozen bitmap above no
                // longer needs it.
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            // Never throw — a per-row icon is decorative; failure just falls back to no icon.
            return null;
        }
    }

    // --- Win32 ---

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000; // 32px (SHGFI_SMALLICON would be 16px)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
