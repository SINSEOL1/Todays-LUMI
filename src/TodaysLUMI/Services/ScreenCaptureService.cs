namespace TodaysLUMI.Services;

public sealed class ScreenCaptureService
{
    // The LUMI message appears in the lower-left chat area.
    private const double RoiLeft = 0.00;
    private const double RoiTop = 0.60;
    private const double RoiWidth = 0.38;
    private const double RoiHeight = 0.18;

    public System.Drawing.Bitmap CaptureChatRegion(GameWindowInfo window)
    {
        var x = window.X + (int)Math.Round(window.Width * RoiLeft);
        var y = window.Y + (int)Math.Round(window.Height * RoiTop);
        var width = Math.Max(1, (int)Math.Round(window.Width * RoiWidth));
        var height = Math.Max(1, (int)Math.Round(window.Height * RoiHeight));

        var bitmap = new System.Drawing.Bitmap(
            width,
            height,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            x,
            y,
            0,
            0,
            new System.Drawing.Size(width, height),
            System.Drawing.CopyPixelOperation.SourceCopy);

        return bitmap;
    }
}
