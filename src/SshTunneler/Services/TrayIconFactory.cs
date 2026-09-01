using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SshTunneler.Services;

/// <summary>Draws the tray icon at runtime so the app ships without binary assets.</summary>
public static class TrayIconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(bool anyActive)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var background = new SolidBrush(Color.FromArgb(230, 27, 27, 31));
            g.FillEllipse(background, 1, 1, 30, 30);

            var accent = anyActive ? Color.FromArgb(63, 196, 107) : Color.FromArgb(130, 130, 140);
            using var pen = new Pen(accent, 3f);
            // A short tunnel: two rails converging into an arch.
            g.DrawArc(pen, 8, 8, 16, 16, 180, 180);
            g.DrawLine(pen, 8, 16, 8, 24);
            g.DrawLine(pen, 24, 16, 24, 24);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
