using System.Reflection;
using System.Windows;
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
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly LumiRecognitionMonitor _recognitionMonitor;

    private AppSettings _settings;
    private OverlayWindow? _overlayWindow;
    private LumiItem? _currentDetectedItem;
    private bool _allowClose;
    private bool _loadingSettings = true;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.Load();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        VersionText.Text = versionText;
        AboutVersionText.Text = versionText;

        TestItemComboBox.ItemsSource = LumiItem.All;
        TestItemComboBox.DisplayMemberPath = nameof(LumiItem.Name);
        TestItemComboBox.SelectedItem = LumiItem.Meteorite;

        ApplySettingsToUi();
        _loadingSettings = false;

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
        menu.Items.Add("오버레이 테스트", null, (_, _) => Dispatcher.Invoke(ShowOverlayTest));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _trayIcon.ContextMenuStrip = menu;

        Closing += MainWindow_Closing;
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
        GameStatusDot.Fill = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(running ? "#5D8D72" : "#5E646D"));

        if (!running && _settings.ShowOnlyWhenGameActive)
        {
            _overlayWindow?.Hide();
        }
        else if (running && _currentDetectedItem is not null && _settings.OverlayEnabled)
        {
            ShowDetectedOverlay(_currentDetectedItem);
        }
    }

    private void ApplyDetectedItem(LumiItem item)
    {
        _currentDetectedItem = item;
        CurrentItemText.Text = item.Name;

        if (!_settings.OverlayEnabled)
            return;

        if (_settings.ShowOnlyWhenGameActive && !_gameMonitor.IsRunning)
            return;

        ShowDetectedOverlay(item);
    }

    private void ShowDetectedOverlay(LumiItem item)
    {
        _overlayWindow ??= new OverlayWindow();
        _overlayWindow.SetItem(item);

        if (!_overlayWindow.IsVisible)
            _overlayWindow.Show();

        if (_gameWindowService.TryGetGameWindow(out var game))
        {
            _overlayWindow.Left = game.X + 30;
            _overlayWindow.Top = game.Y + Math.Max(48, game.Height * 0.16);
        }
        else
        {
            _overlayWindow.Left = SystemParameters.WorkArea.Left + 30;
            _overlayWindow.Top = SystemParameters.WorkArea.Top + 160;
        }
    }

    private void ShowPage(UIElement page)
    {
        HomePage.Visibility = Visibility.Collapsed;
        OverlayPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
    }

    private void HomeNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);
    private void OverlayNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(OverlayPage);
    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);
    private void AboutNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(AboutPage);

    private void OverlayTestButton_Click(object sender, RoutedEventArgs e) => ShowOverlayTest();

    private void RecognizeNowButton_Click(object sender, RoutedEventArgs e)
    {
        _recognitionMonitor.ScanNow();
    }

    private void ShowOverlayTest()
    {
        if (OverlayEnabledCheckBox.IsChecked != true)
            return;

        _overlayWindow ??= new OverlayWindow();

        if (TestItemComboBox.SelectedItem is LumiItem item)
        {
            _overlayWindow.SetItem(item);
            CurrentItemText.Text = item.Name;
        }

        if (!_overlayWindow.IsVisible)
            _overlayWindow.Show();

        _overlayWindow.Left = SystemParameters.WorkArea.Right - _overlayWindow.Width - 32;
        _overlayWindow.Top = SystemParameters.WorkArea.Top + 180;
    }

    private void TestItemComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_overlayWindow?.IsVisible == true && TestItemComboBox.SelectedItem is LumiItem item)
        {
            _overlayWindow.SetItem(item);
            CurrentItemText.Text = item.Name;
        }
    }

    private void OverlaySetting_Changed(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();

        if (OverlayEnabledCheckBox.IsChecked != true && _overlayWindow is not null)
            _overlayWindow.Hide();
    }

    private void GeneralSetting_Changed(object sender, RoutedEventArgs e) => SaveSettingsFromUi();

    private void CopyDiscordButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText("sinseol");
        CopyStatusText.Text = "복사됨";
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
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

        if (_overlayWindow is not null)
            _overlayWindow.Close();

        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        Close();
        System.Windows.Application.Current.Shutdown();
    }
}
