using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using TodaysLUMI.Models;
using TodaysLUMI.Services;

namespace TodaysLUMI;

public partial class MainWindow : Window
{
    private readonly AppSettingsService _settingsService = new();
    private readonly GameProcessMonitor _gameMonitor = new();
    private readonly GameWindowService _gameWindowService = new();
    private readonly GlobalHotkeyService _hotkeyService = new();
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly LumiRecognitionMonitor _recognitionMonitor;

    private Forms.ToolStripMenuItem? _overlayToggleMenuItem;
    private AppSettings _settings;
    private OverlayWindow? _overlayWindow;
    private LumiItem? _currentDetectedItem;

    private bool _allowClose;
    private bool _loadingSettings = true;
    private bool _overlayHiddenByHotkey;
    private bool _capturingHotkey;
    private string _hotkeyBeforeCapture = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.Load();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null
            ? "1.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";

        VersionText.Text = versionText;
        AboutVersionText.Text = versionText;

        TestItemComboBox.ItemsSource = LumiItem.All;
        TestItemComboBox.DisplayMemberPath = nameof(LumiItem.Name);
        TestItemComboBox.SelectedItem = LumiItem.Meteorite;

        ApplySettingsToUi();
        _loadingSettings = false;

        SourceInitialized += MainWindow_SourceInitialized;

        _gameMonitor.RunningStateChanged += (_, running) =>
            Dispatcher.Invoke(() => UpdateGameState(running));
        _gameMonitor.Start();

        _recognitionMonitor = new LumiRecognitionMonitor(() => _settings.AutoRecognition);
        _recognitionMonitor.ItemDetected += (_, item) =>
            Dispatcher.Invoke(() => ApplyDetectedItem(item));
        _recognitionMonitor.Start();

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "오늘의 루미",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("오늘의 루미 열기", null, (_, _) => ShowFromTray());

        _overlayToggleMenuItem = new Forms.ToolStripMenuItem(
            "오버레이 숨기기",
            null,
            (_, _) => Dispatcher.Invoke(ToggleOverlayVisibility));

        menu.Items.Add(_overlayToggleMenuItem);
        menu.Items.Add("다시 인식", null, (_, _) =>
            Dispatcher.Invoke(() => _recognitionMonitor.ScanNow()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon.ContextMenuStrip = menu;

        Closing += MainWindow_Closing;

        UpdateShortcutLabels();
        UpdateOverlayRuntimeUi();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _hotkeyService.Initialize(this, () =>
        {
            if (_capturingHotkey)
                return;

            ToggleOverlayVisibility();
        });

        if (_hotkeyService.Register(_settings.OverlayHotkey))
        {
            SetHotkeyStatus(string.Empty, "#68737F");
        }
        else
        {
            SetHotkeyStatus("현재 단축키를 등록할 수 없습니다. 다른 조합으로 변경해 주세요.", "#A34E4E");
        }
    }

    private void ApplySettingsToUi()
    {
        OverlayEnabledCheckBox.IsChecked = _settings.OverlayEnabled;
        CompactOverlayCheckBox.IsChecked = _settings.OverlayCompact;
        GameActiveOnlyCheckBox.IsChecked = _settings.ShowOnlyWhenGameActive;
        CloseToTrayCheckBox.IsChecked = _settings.CloseToTray;
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        AutoRecognitionCheckBox.IsChecked = _settings.AutoRecognition;
        AutoUpdateCheckBox.IsChecked = _settings.AutoCheckUpdates;

        UpdateShortcutLabels();
    }

    private void SaveSettingsFromUi()
    {
        if (_loadingSettings)
            return;

        _settings.OverlayEnabled = OverlayEnabledCheckBox.IsChecked == true;
        _settings.OverlayCompact = CompactOverlayCheckBox.IsChecked == true;
        _settings.ShowOnlyWhenGameActive = GameActiveOnlyCheckBox.IsChecked == true;
        _settings.CloseToTray = CloseToTrayCheckBox.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _settings.AutoRecognition = AutoRecognitionCheckBox.IsChecked == true;
        _settings.AutoCheckUpdates = AutoUpdateCheckBox.IsChecked == true;

        _settingsService.Save(_settings);
    }

    private void UpdateGameState(bool running)
    {
        GameStatusText.Text = running ? "실행 중" : "게임을 기다리는 중";
        GameStatusDescription.Text = running
            ? "게임 창이 감지되었습니다."
            : "이터널 리턴이 실행되면 자동으로 연결합니다.";

        GameStatusDot.Fill = CreateBrush(running ? "#4A8A62" : "#929AA3");

        if (!running && _settings.ShowOnlyWhenGameActive)
        {
            _overlayWindow?.Hide();
        }
        else if (running &&
                 _currentDetectedItem is not null &&
                 _settings.OverlayEnabled &&
                 !_overlayHiddenByHotkey)
        {
            ShowDetectedOverlay(_currentDetectedItem);
        }

        UpdateOverlayRuntimeUi();
    }

    private void ApplyDetectedItem(LumiItem item)
    {
        _currentDetectedItem = item;
        CurrentItemText.Text = item.Name;
        CurrentItemAccentBar.Background = CreateBrush(item.AccentHex);

        if (!_settings.OverlayEnabled || _overlayHiddenByHotkey)
        {
            UpdateOverlayRuntimeUi();
            return;
        }

        if (_settings.ShowOnlyWhenGameActive && !_gameMonitor.IsRunning)
        {
            UpdateOverlayRuntimeUi();
            return;
        }

        ShowDetectedOverlay(item);
        UpdateOverlayRuntimeUi();
    }

    private void ShowDetectedOverlay(LumiItem item)
    {
        _overlayWindow ??= new OverlayWindow();
        _overlayWindow.SetItem(item);
        _overlayWindow.ApplyAppearance(_settings.OverlayCompact, _settings.OverlayOpacity);

        if (!_overlayWindow.IsVisible)
            _overlayWindow.Show();

        if (_gameWindowService.TryGetGameWindow(out var game))
        {
            _overlayWindow.Left = game.X + _settings.OverlayOffsetX;
            _overlayWindow.Top = game.Y + Math.Max(48, game.Height * 0.16);
        }
        else
        {
            _overlayWindow.Left = SystemParameters.WorkArea.Left + _settings.OverlayOffsetX;
            _overlayWindow.Top = SystemParameters.WorkArea.Top + _settings.OverlayOffsetY;
        }
    }

    private void ToggleOverlayVisibility()
    {
        if (!_settings.OverlayEnabled)
        {
            SetHotkeyStatus("오버레이 사용이 꺼져 있습니다.", "#A34E4E");
            return;
        }

        _overlayHiddenByHotkey = !_overlayHiddenByHotkey;

        if (_overlayHiddenByHotkey)
        {
            _overlayWindow?.Hide();
        }
        else if (_currentDetectedItem is not null &&
                 (!_settings.ShowOnlyWhenGameActive || _gameMonitor.IsRunning))
        {
            ShowDetectedOverlay(_currentDetectedItem);
        }

        UpdateOverlayRuntimeUi();
    }

    private void UpdateOverlayRuntimeUi()
    {
        string text;
        string color;

        if (!_settings.OverlayEnabled)
        {
            text = "사용 안 함";
            color = "#7A848F";
        }
        else if (_overlayHiddenByHotkey)
        {
            text = "숨김";
            color = "#A34E4E";
        }
        else if (_settings.ShowOnlyWhenGameActive && !_gameMonitor.IsRunning)
        {
            text = "게임 대기";
            color = "#667B96";
        }
        else if (_currentDetectedItem is null)
        {
            text = "표시 대기";
            color = "#667B96";
        }
        else
        {
            text = "표시 중";
            color = "#3F7D59";
        }

        OverlayRuntimeStatusText.Text = text;
        OverlayPageRuntimeStatusText.Text = text;

        var brush = CreateBrush(color);
        OverlayRuntimeStatusText.Foreground = brush;
        OverlayPageRuntimeStatusText.Foreground = brush;

        if (_overlayToggleMenuItem is not null)
        {
            _overlayToggleMenuItem.Text = _overlayHiddenByHotkey
                ? "오버레이 표시"
                : "오버레이 숨기기";
        }
    }

    private void ShowPage(UIElement page, System.Windows.Controls.Button activeButton)
    {
        HomePage.Visibility = Visibility.Collapsed;
        OverlayPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Collapsed;

        HomeNavButton.Tag = null;
        OverlayNavButton.Tag = null;
        SettingsNavButton.Tag = null;
        AboutNavButton.Tag = null;

        page.Visibility = Visibility.Visible;
        activeButton.Tag = "Active";
    }

    private void HomeNavButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage(HomePage, HomeNavButton);

    private void OverlayNavButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage(OverlayPage, OverlayNavButton);

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage(SettingsPage, SettingsNavButton);

    private void AboutNavButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage(AboutPage, AboutNavButton);

    private void OverlayTestButton_Click(object sender, RoutedEventArgs e) => ShowOverlayTest();

    private void RecognizeNowButton_Click(object sender, RoutedEventArgs e)
    {
        _recognitionMonitor.ScanNow();
    }

    private void ShowOverlayTest()
    {
        if (!_settings.OverlayEnabled)
            return;

        _overlayHiddenByHotkey = false;
        _overlayWindow ??= new OverlayWindow();

        if (TestItemComboBox.SelectedItem is LumiItem item)
            _overlayWindow.SetItem(item);

        _overlayWindow.ApplyAppearance(_settings.OverlayCompact, _settings.OverlayOpacity);

        if (!_overlayWindow.IsVisible)
            _overlayWindow.Show();

        _overlayWindow.Left = SystemParameters.WorkArea.Right - _overlayWindow.Width - 32;
        _overlayWindow.Top = SystemParameters.WorkArea.Top + 180;

        UpdateOverlayRuntimeUi();
    }

    private void TestItemComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_overlayWindow?.IsVisible == true &&
            TestItemComboBox.SelectedItem is LumiItem item)
        {
            _overlayWindow.SetItem(item);
        }
    }

    private void OverlaySetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
            return;

        SaveSettingsFromUi();

        _overlayWindow?.ApplyAppearance(_settings.OverlayCompact, _settings.OverlayOpacity);

        if (!_settings.OverlayEnabled)
        {
            _overlayWindow?.Hide();
        }
        else if (!_overlayHiddenByHotkey &&
                 _currentDetectedItem is not null &&
                 (!_settings.ShowOnlyWhenGameActive || _gameMonitor.IsRunning))
        {
            ShowDetectedOverlay(_currentDetectedItem);
        }

        UpdateOverlayRuntimeUi();
    }

    private void GeneralSetting_Changed(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
    }

    private void HotkeyCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        _hotkeyBeforeCapture = _settings.OverlayHotkey;
        HotkeyCaptureButton.Content = "입력 중…";
        SetHotkeyStatus("새 단축키를 누르세요 · Esc로 취소", "#667B96");
        Keyboard.Focus(HotkeyCaptureButton);
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturingHotkey)
            return;

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            CancelHotkeyCapture();
            return;
        }

        if (GlobalHotkeyService.IsModifierKey(key))
            return;

        var modifiers = Keyboard.Modifiers;

        if (!GlobalHotkeyService.IsSafeShortcut(modifiers, key))
        {
            SetHotkeyStatus("Ctrl, Shift, Alt, Win 중 하나를 함께 누르거나 F1~F24를 사용해 주세요.", "#A34E4E");
            return;
        }

        var candidate = GlobalHotkeyService.Format(modifiers, key);

        if (_hotkeyService.Register(candidate))
        {
            _settings.OverlayHotkey = candidate;
            _settingsService.Save(_settings);

            _capturingHotkey = false;
            HotkeyCaptureButton.Content = "변경";
            UpdateShortcutLabels();
            SetHotkeyStatus("단축키가 저장되었습니다.", "#3F7D59");
            return;
        }

        _hotkeyService.Register(_hotkeyBeforeCapture);
        _capturingHotkey = false;
        HotkeyCaptureButton.Content = "변경";
        SetHotkeyStatus("다른 프로그램에서 사용 중인 단축키입니다.", "#A34E4E");
    }

    private void CancelHotkeyCapture()
    {
        _capturingHotkey = false;
        HotkeyCaptureButton.Content = "변경";
        UpdateShortcutLabels();
        SetHotkeyStatus(string.Empty, "#68737F");
    }

    private void UpdateShortcutLabels()
    {
        if (HotkeyDisplayText is null || HomeHotkeyText is null)
            return;

        var display = _settings.OverlayHotkey.Replace("+", " + ");
        HotkeyDisplayText.Text = display;
        HomeHotkeyText.Text = display;
    }

    private void SetHotkeyStatus(string text, string color)
    {
        HotkeyStatusText.Text = text;
        HotkeyStatusText.Foreground = CreateBrush(color);
    }

    private void CopyDiscordButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText("sinseol");
        CopyStatusText.Text = "복사됨";
    }

    private void MainWindow_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
            return;

        if (_settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            ExitApplication();
        }
    }

    private void ShowFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    private void ExitApplication()
    {
        _allowClose = true;

        _gameMonitor.Dispose();
        _recognitionMonitor.Dispose();
        _hotkeyService.Dispose();

        if (_overlayWindow is not null)
            _overlayWindow.Close();

        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private static SolidColorBrush CreateBrush(string hex) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
}
