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
        AccentBar.Background =
            (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(item.AccentHex)!;
    }

    public void ApplyAppearance(bool compact, double opacity)
    {
        Opacity = Math.Clamp(opacity, 0.5, 1.0);

        if (compact)
        {
            Width = 168;
            Height = 44;
            TitleText.Visibility = Visibility.Collapsed;
            ContentStack.Margin = new Thickness(12, 8, 10, 7);
            ItemNameText.Margin = new Thickness(0);
            ItemNameText.FontSize = 15;
        }
        else
        {
            Width = 218;
            Height = 66;
            TitleText.Visibility = Visibility.Visible;
            ContentStack.Margin = new Thickness(14, 9, 12, 8);
            ItemNameText.Margin = new Thickness(0, 2, 0, 0);
            ItemNameText.FontSize = 17;
        }
    }
}
