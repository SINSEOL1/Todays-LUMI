using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TodaysLUMI.Models;

namespace TodaysLUMI.Services;

public sealed class LumiSignatureRecognizer
{
    private readonly record struct Prototype(LumiItem Item, double Ratio);

    // Purple item width / cyan LUMI-prefix width.
    // Force Core (0.29) is calibrated from a real in-game sample.
    private static readonly Prototype[] Prototypes =
    [
        new(LumiItem.Meteorite, 0.14),
        new(LumiItem.Mithril, 0.21),
        new(LumiItem.ForceCore, 0.29),
        new(LumiItem.TreeOfLife, 0.41)
    ];

    public bool TryRecognize(System.Drawing.Bitmap bitmap, out LumiItem? item)
    {
        item = null;

        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            var cyanCount = new int[bitmap.Height];
            var purpleCount = new int[bitmap.Height];
            var purpleMin = Enumerable.Repeat(int.MaxValue, bitmap.Height).ToArray();
            var purpleMax = Enumerable.Repeat(-1, bitmap.Height).ToArray();

            // The signature line itself occupies the left side of the chat ROI.
            // Ignoring the right side prevents combat/UI colors from joining the text span.
            var scanWidth = Math.Max(1, (int)Math.Round(bitmap.Width * 0.60));

            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = data.Stride >= 0 ? y : bitmap.Height - 1 - y;
                var rowOffset = row * stride;

                for (var x = 0; x < scanWidth; x++)
                {
                    var color = GetColor(bytes, rowOffset, x);
                    if (IsCyan(color.R, color.G, color.B))
                    {
                        cyanCount[y]++;
                    }
                    else if (IsPurple(color.R, color.G, color.B))
                    {
                        purpleCount[y]++;
                        purpleMin[y] = Math.Min(purpleMin[y], x);
                        purpleMax[y] = Math.Max(purpleMax[y], x);
                    }
                }
            }

            var bestY = -1;
            var bestScore = 0;
            var radius = Math.Max(2, bitmap.Height / 80);

            for (var y = radius; y < bitmap.Height - radius; y++)
            {
                var cyan = 0;
                var purple = 0;

                for (var yy = y - radius; yy <= y + radius; yy++)
                {
                    cyan += cyanCount[yy];
                    purple += purpleCount[yy];
                }

                if (cyan < 20 || purple < 6)
                    continue;

                var score = cyan + (purple * 2);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestY = y;
                }
            }

            if (bestY < 0)
                return false;

            var bandHeight = Math.Max(8, bitmap.Height / 12);
            var fromY = Math.Max(0, bestY - bandHeight / 2);
            var toY = Math.Min(bitmap.Height - 1, bestY + bandHeight / 2);

            var pMin = int.MaxValue;
            var pMax = -1;

            for (var y = fromY; y <= toY; y++)
            {
                if (purpleCount[y] < 2)
                    continue;

                pMin = Math.Min(pMin, purpleMin[y]);
                pMax = Math.Max(pMax, purpleMax[y]);
            }

            if (pMax <= pMin)
                return false;

            // Re-scan the same horizontal band and only accept cyan pixels before
            // the purple item. This isolates "안내 로봇-LUMI 시그니처 상품 :" from
            // unrelated cyan UI elements in the same row.
            var cMin = int.MaxValue;
            var cMax = -1;

            for (var y = fromY; y <= toY; y++)
            {
                var row = data.Stride >= 0 ? y : bitmap.Height - 1 - y;
                var rowOffset = row * stride;
                var limit = Math.Min(scanWidth, pMin);

                for (var x = 0; x < limit; x++)
                {
                    var color = GetColor(bytes, rowOffset, x);
                    if (!IsCyan(color.R, color.G, color.B))
                        continue;

                    cMin = Math.Min(cMin, x);
                    cMax = Math.Max(cMax, x);
                }
            }

            if (cMax <= cMin)
                return false;

            var cyanWidth = cMax - cMin + 1;
            var purpleWidth = pMax - pMin + 1;
            var gap = pMin - cMax;

            if (cyanWidth < bitmap.Width * 0.12)
                return false;

            if (gap < -cyanWidth * 0.08 || gap > cyanWidth * 0.30)
                return false;

            var ratio = purpleWidth / (double)cyanWidth;
            var best = Prototypes
                .Select(x => (Prototype: x, Distance: Math.Abs(x.Ratio - ratio)))
                .OrderBy(x => x.Distance)
                .First();

            if (best.Distance > 0.075)
                return false;

            item = best.Prototype.Item;
            return true;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static (byte R, byte G, byte B) GetColor(byte[] bytes, int rowOffset, int x)
    {
        var i = rowOffset + (x * 3);
        return (bytes[i + 2], bytes[i + 1], bytes[i]);
    }

    private static bool IsCyan(byte r, byte g, byte b)
    {
        RgbToHsv(r, g, b, out var h, out var s, out var v);
        return v >= 0.28 && s >= 0.28 && h is >= 165 and <= 205;
    }

    private static bool IsPurple(byte r, byte g, byte b)
    {
        RgbToHsv(r, g, b, out var h, out var s, out var v);
        return v >= 0.28 && s >= 0.28 && h is >= 250 and <= 315;
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
