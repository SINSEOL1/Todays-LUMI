using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TodaysLUMI.Services;

public readonly record struct GameWindowInfo(IntPtr Handle, int X, int Y, int Width, int Height);

public sealed class GameWindowService
{
    private static readonly string[] ProcessNames =
    [
        "EternalReturn",
        "EternalReturn-Win64-Shipping"
    ];

    private static readonly string[] WindowTitleHints =
    [
        "Eternal Return",
        "이터널 리턴"
    ];

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    public bool TryGetGameWindow(out GameWindowInfo info)
    {
        foreach (var processName in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Refresh();

                    if (TryBuildInfo(process.MainWindowHandle, out info))
                        return true;
                }
                catch
                {
                    // The game can close while its process is being inspected.
                }
            }
        }

        GameWindowInfo found = default;
        var hasFound = false;

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
                return true;

            var length = GetWindowTextLength(handle);
            if (length <= 0)
                return true;

            var title = new StringBuilder(length + 1);
            GetWindowText(handle, title, title.Capacity);

            if (!WindowTitleHints.Any(hint =>
                    title.ToString().Contains(hint, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (!TryBuildInfo(handle, out found))
                return true;

            hasFound = true;
            return false;
        }, IntPtr.Zero);

        info = found;
        return hasFound;
    }

    private static bool TryBuildInfo(IntPtr handle, out GameWindowInfo info)
    {
        info = default;

        if (handle == IntPtr.Zero || !GetClientRect(handle, out var rect))
            return false;

        var topLeft = new POINT();

        if (!ClientToScreen(handle, ref topLeft))
            return false;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        if (width < 800 || height < 450)
            return false;

        info = new GameWindowInfo(handle, topLeft.X, topLeft.Y, width, height);
        return true;
    }
}
