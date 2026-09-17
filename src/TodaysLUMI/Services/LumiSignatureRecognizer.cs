using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TodaysLUMI.Models;

namespace TodaysLUMI.Services;

public sealed class LumiSignatureRecognizer
{
    private readonly record struct InkRun(int Start, int End)
    {
        public int Width => End - Start + 1;
    }

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

            var purpleColumns = new int[scanWidth];

            for (var y = fromY; y <= toY; y++)
            {
                var rowOffset = GetRowOffset(data.Stride, stride, bitmap.Height, y);

                for (var x = 0; x < scanWidth; x++)
                {
                    var (r, g, b) = GetColor(bytes, rowOffset, x);
                    if (IsPurple(r, g, b))
                        purpleColumns[x]++;
                }
            }

            var runs = BuildRuns(purpleColumns, bandHeight);
            if (runs.Count == 0)
                return false;

            // Keep the largest real text cluster and discard tiny purple UI noise.
            runs = runs
                .Where(x => x.Width >= Math.Max(4, bandHeight / 5))
                .ToList();

            if (runs.Count == 0)
                return false;

            runs = CollapseToAtMostTwoGroups(runs);

            var firstX = runs[0].Start;
            if (!HasLumiPrefix(bytes, data.Stride, stride, bitmap.Height, fromY, toY, firstX, bitmap.Width))
                return false;

            var textHeight = MeasurePurpleTextHeight(
                bytes,
                data.Stride,
                stride,
                bitmap.Height,
                fromY,
                toY,
                runs[0].Start,
                runs[^1].End);

            if (textHeight < 5)
                return false;

            if (runs.Count == 1)
            {
                // Both single-word candidates use the same font and color.
                // Their only meaningful difference is 2 Hangul syllables vs 3.
                var normalizedWidth = runs[0].Width / (double)textHeight;
                item = normalizedWidth < 2.35
                    ? LumiItem.Meteorite
                    : LumiItem.Mithril;

                return true;
            }

            // Two-word candidates are distinguishable without per-item screenshots:
            // "포스 코어" has similarly-sized words (2 + 2 syllables),
            // while "생명의 나무" has a visibly wider first word (3 + 2 syllables).
            var wordRatio = runs[0].Width / (double)Math.Max(1, runs[1].Width);
            item = wordRatio >= 1.25
                ? LumiItem.TreeOfLife
                : LumiItem.ForceCore;

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

    private static List<InkRun> BuildRuns(int[] columns, int bandHeight)
    {
        var raw = new List<InkRun>();
        var start = -1;

        for (var x = 0; x < columns.Length; x++)
        {
            var active = columns[x] > 0;

            if (active && start < 0)
            {
                start = x;
            }
            else if (!active && start >= 0)
            {
                raw.Add(new InkRun(start, x - 1));
                start = -1;
            }
        }

        if (start >= 0)
            raw.Add(new InkRun(start, columns.Length - 1));

        if (raw.Count <= 1)
            return raw;

        var closeGap = Math.Max(2, bandHeight / 8);
        var merged = new List<InkRun>();
        var current = raw[0];

        for (var i = 1; i < raw.Count; i++)
        {
            var next = raw[i];
            var gap = next.Start - current.End - 1;

            if (gap <= closeGap)
            {
                current = new InkRun(current.Start, next.End);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    private static List<InkRun> CollapseToAtMostTwoGroups(List<InkRun> runs)
    {
        runs = runs.OrderBy(x => x.Start).ToList();

        while (runs.Count > 2)
        {
            var smallestGap = int.MaxValue;
            var mergeIndex = 0;

            for (var i = 0; i < runs.Count - 1; i++)
            {
                var gap = runs[i + 1].Start - runs[i].End - 1;
                if (gap >= smallestGap)
                    continue;

                smallestGap = gap;
                mergeIndex = i;
            }

            runs[mergeIndex] = new InkRun(runs[mergeIndex].Start, runs[mergeIndex + 1].End);
            runs.RemoveAt(mergeIndex + 1);
        }

        return runs;
    }

    private static bool HasLumiPrefix(
        byte[] bytes,
        int signedStride,
        int stride,
        int height,
        int fromY,
        int toY,
        int itemStartX,
        int bitmapWidth)
    {
        var minX = int.MaxValue;
        var maxX = -1;
        var pixelCount = 0;

        for (var y = fromY; y <= toY; y++)
        {
            var rowOffset = GetRowOffset(signedStride, stride, height, y);

            for (var x = 0; x < itemStartX; x++)
            {
                var (r, g, b) = GetColor(bytes, rowOffset, x);
                if (!IsCyan(r, g, b))
                    continue;

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                pixelCount++;
            }
        }

        if (maxX <= minX || pixelCount < 35)
            return false;

        var prefixWidth = maxX - minX + 1;
        if (prefixWidth < bitmapWidth * 0.10)
            return false;

        var gap = itemStartX - maxX;
        return gap >= -4 && gap <= Math.Max(35, prefixWidth / 4);
    }

    private static int MeasurePurpleTextHeight(
        byte[] bytes,
        int signedStride,
        int stride,
        int height,
        int fromY,
        int toY,
        int fromX,
        int toX)
    {
        var minY = int.MaxValue;
        var maxY = -1;

        for (var y = fromY; y <= toY; y++)
        {
            var rowOffset = GetRowOffset(signedStride, stride, height, y);

            for (var x = fromX; x <= toX; x++)
            {
                var (r, g, b) = GetColor(bytes, rowOffset, x);
                if (!IsPurple(r, g, b))
                    continue;

                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxY >= minY ? maxY - minY + 1 : 0;
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
