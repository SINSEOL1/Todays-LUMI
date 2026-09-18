using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TodaysLUMI.Services;

public sealed class LobbyDetector
{
    public bool IsLobby(GameWindowInfo window)
    {
        using var menu = CaptureRelative(window, 0.015, 0.11, 0.19, 0.58);
        using var footer = CaptureRelative(window, 0.00, 0.88, 0.28, 0.11);

        var menuTextBands = CountBrightTextBands(menu);
        var footerBrightRatio = GetBrightPixelRatio(footer);

        // The lobby background can change, but these two UI groups stay fixed:
        // the vertical navigation menu on the left and the ENTER/ESC help row.
        return menuTextBands >= 7 && footerBrightRatio >= 0.006;
    }

    private static Bitmap CaptureRelative(
        GameWindowInfo window,
        double left,
        double top,
        double width,
        double height)
    {
        var x = window.X + (int)Math.Round(window.Width * left);
        var y = window.Y + (int)Math.Round(window.Height * top);
        var w = Math.Max(1, (int)Math.Round(window.Width * width));
        var h = Math.Max(1, (int)Math.Round(window.Height * height));

        var bitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            x,
            y,
            0,
            0,
            new Size(w, h),
            CopyPixelOperation.SourceCopy);

        return bitmap;
    }

    private static int CountBrightTextBands(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            var activeRows = new bool[bitmap.Height];
            var threshold = Math.Max(8, (int)Math.Round(bitmap.Width * 0.035));

            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = data.Stride >= 0 ? y : bitmap.Height - 1 - y;
                var offset = row * stride;
                var bright = 0;

                for (var x = 0; x < bitmap.Width; x++)
                {
                    var i = offset + x * 3;
                    var b = bytes[i];
                    var g = bytes[i + 1];
                    var r = bytes[i + 2];

                    if (IsLobbyTextPixel(r, g, b))
                        bright++;
                }

                activeRows[y] = bright >= threshold;
            }

            var bands = 0;
            var start = -1;

            for (var y = 0; y <= activeRows.Length; y++)
            {
                var active = y < activeRows.Length && activeRows[y];

                if (active && start < 0)
                {
                    start = y;
                    continue;
                }

                if (!active && start >= 0)
                {
                    var bandHeight = y - start;

                    // Lobby menu labels are compact text rows. Ignore single-pixel
                    // noise and unusually tall bright background patches.
                    if (bandHeight is >= 2 and <= 28)
                        bands++;

                    start = -1;
                }
            }

            return bands;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static double GetBrightPixelRatio(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            var bright = 0;
            var total = bitmap.Width * bitmap.Height;

            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = data.Stride >= 0 ? y : bitmap.Height - 1 - y;
                var offset = row * stride;

                for (var x = 0; x < bitmap.Width; x++)
                {
                    var i = offset + x * 3;
                    var b = bytes[i];
                    var g = bytes[i + 1];
                    var r = bytes[i + 2];

                    if (IsLobbyTextPixel(r, g, b))
                        bright++;
                }
            }

            return total == 0 ? 0 : bright / (double)total;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static bool IsLobbyTextPixel(byte r, byte g, byte b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));

        // Lobby navigation/footer text is bright and close to neutral white.
        return max >= 185 && (max - min) <= 70;
    }
}
