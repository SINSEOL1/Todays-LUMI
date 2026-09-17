using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TodaysLUMI.Models;

namespace TodaysLUMI;

public partial class OverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoactivate = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public OverlayWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(handle, GwlExstyle);
            SetWindowLong(handle, GwlExstyle,
                style | WsExTransparent | WsExToolwindow | WsExNoactivate);
        };
    }

    public void SetItem(LumiItem item)
    {
        ItemNameText.Text = item.Name;
        AccentBar.Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(item.AccentHex)!;
    }
}
