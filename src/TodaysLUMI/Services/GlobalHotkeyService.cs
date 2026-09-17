using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TodaysLUMI.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4C55;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private HwndSource? _source;
    private IntPtr _windowHandle;
    private Action? _callback;
    private bool _registered;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public void Initialize(Window window, Action callback)
    {
        _windowHandle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        _callback = callback;
        _source?.AddHook(WndProc);
    }

    public bool Register(string shortcut)
    {
        if (_windowHandle == IntPtr.Zero)
            return false;

        if (!TryParse(shortcut, out var modifiers, out var key))
            return false;

        Unregister();

        var nativeModifiers = ToNativeModifiers(modifiers) | ModNoRepeat;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

        _registered = RegisterHotKey(_windowHandle, HotkeyId, nativeModifiers, virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered || _windowHandle == IntPtr.Zero)
            return;

        UnregisterHotKey(_windowHandle, HotkeyId);
        _registered = false;
    }

    public static bool TryParse(string shortcut, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;

        if (string.IsNullOrWhiteSpace(shortcut))
            return false;

        foreach (var rawPart in shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.Trim();

            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Control;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Shift;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Alt;
                continue;
            }

            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Windows;
                continue;
            }

            if (part.Length == 1 && char.IsDigit(part[0]))
            {
                key = Key.D0 + (part[0] - '0');
                continue;
            }

            if (!Enum.TryParse(part, true, out key))
                return false;
        }

        return key != Key.None;
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");

        parts.Add(FormatKey(key));
        return string.Join("+", parts);
    }

    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;

    public static bool IsSafeShortcut(ModifierKeys modifiers, Key key)
    {
        if (key is >= Key.F1 and <= Key.F24)
            return true;

        return modifiers != ModifierKeys.None;
    }

    private static string FormatKey(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
            return ((int)key - (int)Key.D0).ToString();

        return key switch
        {
            Key.Return => "Enter",
            Key.Escape => "Esc",
            _ => key.ToString()
        };
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        var result = 0u;

        if (modifiers.HasFlag(ModifierKeys.Alt))
            result |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Control))
            result |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Shift))
            result |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows))
            result |= ModWin;

        return result;
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _callback?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();

        if (_source is not null)
            _source.RemoveHook(WndProc);

        _source = null;
        _callback = null;
    }
}
