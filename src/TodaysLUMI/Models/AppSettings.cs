namespace TodaysLUMI.Models;

public sealed class AppSettings
{
    public bool OverlayEnabled { get; set; } = true;
    public bool OverlayCompact { get; set; }
    public double OverlayOpacity { get; set; } = 0.92;
    public double OverlayScale { get; set; } = 1.0;
    public bool ShowOnlyWhenGameActive { get; set; } = true;
    public bool AutoRecognition { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public double OverlayOffsetX { get; set; } = 30;
    public double OverlayOffsetY { get; set; } = 180;
    public string OverlayHotkey { get; set; } = "Ctrl+Shift+L";
}
