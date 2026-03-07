using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace JiggleSharp.App;

internal static class WindowIconHelper
{
    public static WindowIcon CreateEmojiIcon(string emoji)
    {
        var size = new PixelSize(32, 32);
        var dpi = new Vector(96, 96);
        var theme = Application.Current!
            .ActualThemeVariant;
        var brushColor = Brushes.Black;

        if (theme == Avalonia.Styling.ThemeVariant.Dark)
            brushColor = Brushes.White;

        using var bitmap = new RenderTargetBitmap(size, dpi);
        using (var ctx = bitmap.CreateDrawingContext(true))
        {
            var text = new FormattedText(
                emoji,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Apple Color Emoji", FontStyle.Normal, FontWeight.Black), // macOS
                26,
                brushColor);

            ctx.DrawText(text, new Point(0, 0));
        }

        return new WindowIcon(bitmap);
    }
}