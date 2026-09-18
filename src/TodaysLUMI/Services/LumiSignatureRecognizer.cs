using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TodaysLUMI.Models;

namespace TodaysLUMI.Services;

public sealed class LumiSignatureRecognizer
{
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

            // The LUMI system line is always in the left portion of the chat area.
            // Keeping the scan narrow prevents unrelated HUD colors from joining it.
            var scanWidth = Math.Max(1, (int)Math.Round(bitmap.Width * 0.60));
            var cyanRows = new int[bitmap.Height];
            var purpleRows = new int[bitmap.Height];

            for (var y = 0; y < bitmap.Height; y++)
            {
                var rowOffset = GetRowOffset(data.Stride, stride, bitmap.Height, y);

                for (var x = 0; x < scanWidth; x++)
                {
                    var (r, g, b) = GetColor(bytes, rowOffset, x);

                    if (IsCyan(r, g, b))
                        cyanRows[y]++;
                    else if (IsPurple(r, g, b))
                        purpleRows[y]++;
                }
            }

            var bestY = FindSignatureRow(cyanRows, purpleRows);
            if (bestY < 0)
                return false;

            var bandHeight = Math.Max(10, bitmap.Height / 12);
            var fromY = Math.Max(0, bestY - bandHeight / 2);
            var toY = Math.Min(bitmap.Height - 1, bestY + bandHeight / 2);

            if (!TryFindPrefixEnd(
                    bytes,
                    data.Stride,
                    stride,
                    bitmap.Height,
                    bitmap.Width,
                    fromY,
                    toY,
                    out var prefixEndX))
            {
                return false;
            }

            if (!TryMeasureItemText(
                    bytes,
                    data.Stride,
                    stride,
                    bitmap.Height,
                    bitmap.Width,
                    fromY,
                    toY,
                    prefixEndX,
                    out var itemSpan,
                    out var textHeight))
            {
                return false;
            }

            // This ratio is resolution-independent because both dimensions scale
            // with the game's UI size. A real "포스 코어" sample measures ~4.06.
            //
            // Approximate glyph counts:
            // 운석       -> 2 Hangul syllables
            // 미스릴     -> 3 Hangul syllables
            // 포스 코어  -> 4 syllables + one word space
            // 생명의 나무 -> 5 syllables + one word space
            var normalizedWidth = itemSpan / (double)textHeight;

            if (normalizedWidth < 1.25 || normalizedWidth > 5.80)
                return false;

            item = normalizedWidth switch
            {
                < 2.25 => LumiItem.Meteorite,
                < 3.38 => LumiItem.Mithril,
                < 4.50 => LumiItem.ForceCore,
                _ => LumiItem.TreeOfLife
            };

            return true;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static int FindSignatureRow(int[] cyanRows, int[] purpleRows)
    {
        var bestY = -1;
        var bestScore = 0;
        var radius = Math.Max(2, cyanRows.Length / 80);

        for (var y = radius; y < cyanRows.Length - radius; y++)
        {
            var cyan = 0;
            var purple = 0;

            for (var yy = y - radius; yy <= y + radius; yy++)
            {
                cyan += cyanRows[yy];
                purple += purpleRows[yy];
            }

            if (cyan < 20 || purple < 6)
                continue;

            var score = cyan + (purple * 2);

            if (score <= bestScore)
                continue;

            bestScore = score;
            bestY = y;
        }

        return bestY;
    }

    private static bool TryFindPrefixEnd(
        byte[] bytes,
        int signedStride,
        int stride,
        int height,
        int bitmapWidth,
        int fromY,
        int toY,
        out int prefixEndX)
    {
        prefixEndX = -1;

        var minX = int.MaxValue;
        var maxX = -1;
        var pixelCount = 0;

        // The fixed "안내 로봇-LUMI 시그니처 상품 :" prefix sits in roughly
        // the first third of the captured chat region.
        var limitX = Math.Min(bitmapWidth - 1, (int)Math.Round(bitmapWidth * 0.36));

        for (var y = fromY; y <= toY; y++)
        {
            var rowOffset = GetRowOffset(signedStride, stride, height, y);

            for (var x = 0; x <= limitX; x++)
            {
                var (r, g, b) = GetColor(bytes, rowOffset, x);

                if (!IsCyan(r, g, b))
                    continue;

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                pixelCount++;
            }
        }

        if (maxX <= minX || pixelCount < 100)
            return false;

        var prefixWidth = maxX - minX + 1;
        var normalizedPrefixWidth = prefixWidth / (double)bitmapWidth;

        // Calibrated from the real in-game chat screenshot while still leaving
        // room for UI scaling differences.
        if (minX > bitmapWidth * 0.08)
            return false;

        if (normalizedPrefixWidth < 0.20 || normalizedPrefixWidth > 0.35)
            return false;

        prefixEndX = maxX;
        return true;
    }

    private static bool TryMeasureItemText(
        byte[] bytes,
        int signedStride,
        int stride,
        int height,
        int bitmapWidth,
        int fromY,
        int toY,
        int prefixEndX,
        out int itemSpan,
        out int textHeight)
    {
        itemSpan = 0;
        textHeight = 0;

        var minX = int.MaxValue;
        var maxX = -1;
        var minY = int.MaxValue;
        var maxY = -1;
        var pixelCount = 0;

        var startX = Math.Max(0, prefixEndX - 2);
        var endX = Math.Min(
            bitmapWidth - 1,
            prefixEndX + Math.Max(60, (int)Math.Round(bitmapWidth * 0.18)));

        for (var y = fromY; y <= toY; y++)
        {
            var rowOffset = GetRowOffset(signedStride, stride, height, y);

            for (var x = startX; x <= endX; x++)
            {
                var (r, g, b) = GetColor(bytes, rowOffset, x);

                if (!IsPurple(r, g, b))
                    continue;

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                pixelCount++;
            }
        }

        if (maxX <= minX || maxY <= minY || pixelCount < 12)
            return false;

        // The item must begin directly after the fixed cyan prefix.
        var gap = minX - prefixEndX;
        if (gap < -4 || gap > Math.Max(14, bitmapWidth / 45))
            return false;

        itemSpan = maxX - minX + 1;
        textHeight = maxY - minY + 1;

        return textHeight >= 5;
    }

    private static int GetRowOffset(int signedStride, int stride, int height, int y)
    {
        var row = signedStride >= 0 ? y : height - 1 - y;
        return row * stride;
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
        return v >= 0.25 && s >= 0.20 && h is >= 245 and <= 320;
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
