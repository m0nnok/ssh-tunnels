using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;

namespace SshTunneler.Services;

/// <summary>Draws the tray icon at runtime so the app ships without binary assets.</summary>
public static class TrayIconFactory
{
    private static readonly IBrush Background = new ImmutableSolidColorBrush(Color.FromArgb(230, 27, 27, 31));
    private static readonly IBrush ActiveAccent = new ImmutableSolidColorBrush(Color.FromRgb(63, 196, 107));
    private static readonly IBrush IdleAccent = new ImmutableSolidColorBrush(Color.FromRgb(130, 130, 140));

    public static WindowIcon Create(bool anyActive)
    {
        using var bitmap = Draw(anyActive);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    public static Bitmap Draw(bool anyActive)
    {
        // The macOS menu bar shows icons at 18pt, so draw enough pixels for Retina screens.
        var size = OperatingSystem.IsMacOS() ? 36 : 32;

        var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(size / 32.0, size / 32.0)))
        {
            context.DrawEllipse(Background, null, new Rect(1, 1, 30, 30));

            // A short tunnel: two rails converging into an arch.
            var pen = new Pen(anyActive ? ActiveAccent : IdleAccent, 3);
            var arch = new StreamGeometry();
            using (var geometry = arch.Open())
            {
                geometry.BeginFigure(new Point(8, 16), isFilled: false);
                geometry.ArcTo(new Point(24, 16), new Size(8, 8), 0, isLargeArc: false, SweepDirection.Clockwise);
                geometry.EndFigure(isClosed: false);
            }

            context.DrawGeometry(null, pen, arch);
            context.DrawLine(pen, new Point(8, 16), new Point(8, 24));
            context.DrawLine(pen, new Point(24, 16), new Point(24, 24));
        }

        return bitmap;
    }
}
