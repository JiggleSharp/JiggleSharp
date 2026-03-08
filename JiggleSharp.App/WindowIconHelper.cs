using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace JiggleSharp.App;

internal static class WindowIconHelper
{
    public static WindowIcon CreateEmojiIcon(string emoji, System.Drawing.Color iconColor)
    {
        var size = new PixelSize(64, 64);
        var dpi = new Vector(96, 96);
        var theme = Application.Current!
            .ActualThemeVariant;
        var brushColor = new SolidColorBrush(
            new Color(iconColor.A, iconColor.R, iconColor.G, iconColor.B));

        using var bitmap = new RenderTargetBitmap(size, dpi);
        using (var ctx = bitmap.CreateDrawingContext(true))
        {
            var mouseIcon = new FormattedText(
                emoji,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Apple Color Emoji", FontStyle.Normal, FontWeight.Black), // macOS
                size.Height * .9,
                brushColor);

            ctx.DrawText(mouseIcon, new Point(0, 1.0 - (size.Height * 0.1)));
            ctx.DrawEllipse(Brushes.LimeGreen, new Pen(Brushes.Green), new Rect(44,44,20,20));
        }

        return new WindowIcon(bitmap);
    }
}