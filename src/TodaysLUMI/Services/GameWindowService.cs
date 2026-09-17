using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TodaysLUMI.Services;

public readonly record struct GameWindowInfo(IntPtr Handle, int X, int Y, int Width, int Height);

public sealed class GameWindowService
{
    private static readonly string[] ProcessNames =
    [
        "EternalReturn",
        "EternalReturn-Win64-Shipping"
    ];

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    public bool TryGetGameWindow(out GameWindowInfo info)
    {
        foreach (var processName in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Refresh();
                    var handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero)
                        continue;

                    if (!GetClientRect(handle, out var rect))
                        continue;

                    var topLeft = new POINT();
                    if (!ClientToScreen(handle, ref topLeft))
                        continue;

                    var width = rect.Right - rect.Left;
                    var height = rect.Bottom - rect.Top;
                    if (width < 800 || height < 450)
                        continue;

                    info = new GameWindowInfo(handle, topLeft.X, topLeft.Y, width, height);
                    return true;
                }
                catch
                {
                    // Process may exit while being inspected.
                }
            }
        }

        info = default;
        return false;
    }
}
