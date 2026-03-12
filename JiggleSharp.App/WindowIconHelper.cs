using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace JiggleSharp.App;

/// <summary>
/// Provides helpers for generating <see cref="WindowIcon"/> instances rendered
/// from an emoji glyph and a colored status indicator dot.
/// </summary>
internal static class WindowIconHelper
{
    // =========================================================================
    // Constants
    // =========================================================================

    /// <summary>Width and height of the rendered icon bitmap in pixels.</summary>
    private const int IconSize = 64;

    /// <summary>Screen DPI used when creating the render target bitmap.</summary>
    private const int IconDpi = 96;

    /// <summary>
    /// The emoji glyph is rendered at this fraction of <see cref="IconSize"/>
    /// to leave room for the indicator dot.
    /// </summary>
    private const double EmojiFontSizeRatio = 0.9;

    /// <summary>
    /// Factor applied to each RGB channel to produce the indicator border
    /// color from the fill color. Lower values produce a darker border.
    /// </summary>
    private const double IndicatorDarkenFactor = 0.6;

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Renders a 64×64 <see cref="WindowIcon"/> composed of an emoji glyph
    /// with a small colored status indicator dot in the bottom-right corner.
    /// </summary>
    /// <param name="emoji">The emoji or symbol character to render as the icon body.</param>
    /// <param name="iconColor">Foreground color applied to the emoji glyph.</param>
    /// <param name="indicatorColor">
    /// Fill color of the status dot. The border color is derived automatically
    /// by darkening this value.
    /// </param>
    /// <returns>A <see cref="WindowIcon"/> backed by the rendered bitmap.</returns>
    public static WindowIcon CreateEmojiIcon(string emoji, Color iconColor, Color indicatorColor)
    {
        var size = new PixelSize(IconSize, IconSize);
        var dpi  = new Vector(IconDpi, IconDpi);

        var emojiBrush         = new SolidColorBrush(iconColor);
        var indicatorFill      = new SolidColorBrush(indicatorColor);
        var indicatorStroke    = new Pen(new SolidColorBrush(Darken(indicatorColor, IndicatorDarkenFactor)));

        using var bitmap = new RenderTargetBitmap(size, dpi);
        using (var ctx = bitmap.CreateDrawingContext(true))
        {
            // Draw the emoji glyph, slightly offset downward to center it visually.
            var glyphText = new FormattedText(
                emoji,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(GetEmojiFontName(), FontStyle.Normal, FontWeight.Black),
                IconSize * EmojiFontSizeRatio,
                emojiBrush);
            var yPosition = 0.1;

            // Determine the y coordinate of the tray icon based on platform
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()) yPosition = 0;
            if (OperatingSystem.IsLinux()) yPosition = 0.1;
            
            ctx.DrawText(glyphText, new Point(0, 1.0 - (IconSize * yPosition)));

            // Draw the status indicator dot in the bottom-right corner.
            ctx.DrawEllipse(indicatorFill, indicatorStroke, new Rect(44, 44, 20, 20));
        }

        return new WindowIcon(bitmap);
    }

    // =========================================================================
    // Private Helpers
    // =========================================================================

    /// <summary>
    /// Returns the platform-appropriate emoji font family name.
    /// Falls back to an empty string on unrecognised platforms, which causes
    /// Avalonia to use the default system font.
    /// </summary>
    private static string GetEmojiFontName()
    {
        if (OperatingSystem.IsMacOS())   return "Apple Color Emoji";
        if (OperatingSystem.IsLinux())   return "Noto Color Emoji";
        if (OperatingSystem.IsWindows()) return "Segoe MDL2 Assets";
        return string.Empty;
    }

    /// <summary>
    /// Returns a darkened copy of <paramref name="color"/> by scaling each
    /// RGB channel by <paramref name="factor"/>. Alpha is preserved.
    /// </summary>
    /// <param name="color">The source color to darken.</param>
    /// <param name="factor">
    /// Multiplier applied to each RGB channel. Must be in the range [0, 1];
    /// lower values produce a darker result.
    /// </param>
    private static Color Darken(Color color, double factor = 0.6) =>
        Color.FromArgb(
            color.A,
            (byte)(color.R * factor),
            (byte)(color.G * factor),
            (byte)(color.B * factor));
}