using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TodaysLUMI.Services;

public sealed class InGameHudDetector
{
    public bool HasInGameHud(GameWindowInfo window)
    {
        // The player's HP bar is a stable in-match HUD element near the
        // lower center of the screen. It disappears during result loading
        // and is absent from the lobby regardless of lobby background.
        using var bitmap = CaptureRelative(window, 0.25, 0.82, 0.37, 0.16);
        return HasHorizontalGreenBar(bitmap, window.Width);
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

    private static bool HasHorizontalGreenBar(Bitmap bitmap, int fullWindowWidth)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            // Keep the threshold relative to the game window so UI scaling and
            // resolution changes do not require hard-coded pixel widths.
            var requiredRun = Math.Max(45, (int)Math.Round(fullWindowWidth * 0.045));

            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = data.Stride >= 0 ? y : bitmap.Height - 1 - y;
                var offset = row * stride;
                var run = 0;

                for (var x = 0; x < bitmap.Width; x++)
                {
                    var i = offset + x * 3;
                    var b = bytes[i];
                    var g = bytes[i + 1];
                    var r = bytes[i + 2];

                    if (IsHealthBarGreen(r, g, b))
                    {
                        run++;

                        if (run >= requiredRun)
                            return true;
                    }
                    else
                    {
                        run = 0;
                    }
                }
            }

            return false;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static bool IsHealthBarGreen(byte r, byte g, byte b)
    {
        RgbToHsv(r, g, b, out var hue, out var saturation, out var value);

        return hue is >= 70 and <= 140 &&
               saturation >= 0.45 &&
               value >= 0.35;
    }

    private static void RgbToHsv(
        byte r,
        byte g,
        byte b,
        out double hue,
        out double saturation,
        out double value)
    {
        var rf = r / 255.0;
        var gf = g / 255.0;
        var bf = b / 255.0;

        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;

        value = max;
        saturation = max <= 0 ? 0 : delta / max;

        if (delta <= 0.0001)
        {
            hue = 0;
            return;
        }

        if (max == rf)
            hue = 60 * (((gf - bf) / delta) % 6);
        else if (max == gf)
            hue = 60 * (((bf - rf) / delta) + 2);
        else
            hue = 60 * (((rf - gf) / delta) + 4);

        if (hue < 0)
            hue += 360;
    }
}
