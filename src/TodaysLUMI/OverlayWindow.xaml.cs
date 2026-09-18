using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
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

    private IntPtr _handle;
    private bool _positionEditMode;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public bool IsPositionEditMode => _positionEditMode;

    public OverlayWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            ApplyInteractionStyle();
        };
    }

    public void SetItem(LumiItem item)
    {
        ItemNameText.Text = item.Name;
        AccentBar.Background =
            (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(item.AccentHex)!;
    }

    public void ApplyAppearance(bool compact, double opacity, double scale)
    {
        Opacity = Math.Clamp(opacity, 0.5, 1.0);
        scale = Math.Clamp(scale, 0.75, 1.50);

        double baseWidth;
        double baseHeight;

        if (compact)
        {
            baseWidth = 168;
            baseHeight = 44;

            TitleText.Visibility = Visibility.Collapsed;
            ContentStack.Margin = new Thickness(12, 8, 10, 7);
            ItemNameText.Margin = new Thickness(0);
            ItemNameText.FontSize = 15;

            EditBadge.Margin = new Thickness(0, 3, 3, 0);
        }
        else
        {
            baseWidth = 218;
            baseHeight = 66;

            TitleText.Visibility = Visibility.Visible;
            ContentStack.Margin = new Thickness(14, 9, 12, 8);
            ItemNameText.Margin = new Thickness(0, 2, 0, 0);
            ItemNameText.FontSize = 17;

            EditBadge.Margin = new Thickness(0, 4, 4, 0);
        }

        RootLayout.Width = baseWidth;
        RootLayout.Height = baseHeight;

        Width = baseWidth * scale;
        Height = baseHeight * scale;
    }

    public void SetPositionEditMode(bool enabled)
    {
        _positionEditMode = enabled;
        Cursor = enabled
            ? System.Windows.Input.Cursors.SizeAll
            : System.Windows.Input.Cursors.Arrow;
        EditBadge.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        ApplyInteractionStyle();
    }

    private void ApplyInteractionStyle()
    {
        if (_handle == IntPtr.Zero)
            return;

        var style = GetWindowLong(_handle, GwlExstyle);

        if (_positionEditMode)
        {
            style &= ~WsExTransparent;
            style &= ~WsExNoactivate;
            style |= WsExToolwindow;
        }
        else
        {
            style |= WsExTransparent | WsExToolwindow | WsExNoactivate;
        }

        SetWindowLong(_handle, GwlExstyle, style);
    }

    private void MainBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_positionEditMode || e.LeftButton != MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove may throw if the mouse button is released during transition.
        }
    }
}
