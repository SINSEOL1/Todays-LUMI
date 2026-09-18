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
    private readonly LobbyStateMonitor _lobbyMonitor = new();
    private readonly MatchTransitionMonitor _matchTransitionMonitor = new();
    private readonly StartupService _startupService = new();
    private readonly UpdateService _updateService = new();
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly LumiRecognitionMonitor _recognitionMonitor;

    private Forms.ToolStripMenuItem? _overlayToggleMenuItem;
    private AppSettings _settings;
    private OverlayWindow? _overlayWindow;
    private LumiItem? _currentDetectedItem;

    private bool _allowClose;
    private bool _loadingSettings = true;
    private bool _overlayHiddenByHotkey;
    private bool _overlaySuppressedByMatchEnd;
    private bool _overlayPositionEditMode;
    private bool _capturingHotkey;
    private bool _checkingForUpdates;
    private bool _downloadingUpdate;
    private UpdateInfo? _availableUpdate;
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

        _recognitionMonitor = new LumiRecognitionMonitor(() => _settings.AutoRecognition);

        _gameMonitor.RunningStateChanged += (_, running) =>
            Dispatcher.Invoke(() => UpdateGameState(running));

        _recognitionMonitor.ItemDetected += (_, item) =>
            Dispatcher.Invoke(() => ApplyDetectedItem(item));

        _lobbyMonitor.LobbyEntered += (_, _) =>
            Dispatcher.Invoke(HandleLobbyEntered);

        _matchTransitionMonitor.MatchEnding += (_, _) =>
            Dispatcher.Invoke(HandleMatchEnding);
        _matchTransitionMonitor.MatchHudReturned += (_, _) =>
            Dispatcher.Invoke(HandleMatchHudReturned);

        var executableIcon =
            !string.IsNullOrWhiteSpace(Environment.ProcessPath)
                ? System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath)
                : null;

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "오늘의 루미",
            Icon = executableIcon ?? System.Drawing.SystemIcons.Application,
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
            Dispatcher.Invoke(StartManualRescan));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon.ContextMenuStrip = menu;

        Closing += MainWindow_Closing;
        Loaded += MainWindow_Loaded;

        UpdateShortcutLabels();
        UpdateOverlayRuntimeUi();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _gameMonitor.Start();
        }
        catch
        {
            // Individual monitors are optional; one failure must not close the app.
        }

        try
        {
            _recognitionMonitor.Start();
        }
        catch
        {
        }

        try
        {
            _lobbyMonitor.Start();
        }
        catch
        {
        }

        try
        {
            _matchTransitionMonitor.Start();
        }
        catch
        {
        }

        if (_settings.AutoCheckUpdates)
            _ = CheckForUpdatesAsync(userInitiated: false);
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

        OverlayScaleSlider.Value = Math.Clamp(_settings.OverlayScale, 0.75, 1.50);
        OverlayOpacitySlider.Value = Math.Clamp(_settings.OverlayOpacity, 0.50, 1.00);
        UpdateOverlayAppearanceLabels();
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
        _settings.OverlayScale = OverlayScaleSlider.Value;
        _settings.OverlayOpacity = OverlayOpacitySlider.Value;

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

    private void HandleMatchEnding()
    {
        if (_currentDetectedItem is null)
            return;

        _overlaySuppressedByMatchEnd = true;
        _overlayWindow?.Hide();
        UpdateOverlayRuntimeUi();
    }

    private void HandleMatchHudReturned()
    {
        if (!_overlaySuppressedByMatchEnd)
            return;

        _overlaySuppressedByMatchEnd = false;

        if (_currentDetectedItem is not null &&
            _settings.OverlayEnabled &&
            !_overlayHiddenByHotkey &&
            (!_settings.ShowOnlyWhenGameActive || _gameMonitor.IsRunning))
        {
            ShowDetectedOverlay(_currentDetectedItem);
        }

        UpdateOverlayRuntimeUi();
    }

    private void HandleLobbyEntered()
    {
        _overlaySuppressedByMatchEnd = false;
        _matchTransitionMonitor.Reset();
        _currentDetectedItem = null;
        CurrentItemText.Text = "아직 감지되지 않음";
        CurrentItemAccentBar.Background = CreateBrush("#D4D9DF");
        _overlayWindow?.Hide();
        _recognitionMonitor.ResetForLobby();
        UpdateOverlayRuntimeUi();
    }

    private void ApplyDetectedItem(LumiItem item)
    {
        _overlaySuppressedByMatchEnd = false;
        _currentDetectedItem = item;
        CurrentItemText.Text = item.Name;
        CurrentItemAccentBar.Background = CreateBrush(item.AccentHex);

        if (!_settings.OverlayEnabled || _overlayHiddenByHotkey || _overlaySuppressedByMatchEnd)
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
        _overlayWindow.ApplyAppearance(
            _settings.OverlayCompact,
            _settings.OverlayOpacity,
            _settings.OverlayScale);

        if (!_overlayWindow.IsVisible)
            _overlayWindow.Show();

        if (!_overlayPositionEditMode)
            ApplySavedOverlayPosition();
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
                 !_overlaySuppressedByMatchEnd &&
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
        else if (_overlaySuppressedByMatchEnd)
        {
            text = "경기 종료";
            color = "#7A848F";
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
        StartManualRescan();
    }

    private void StartManualRescan()
    {
        _overlaySuppressedByMatchEnd = false;
        _matchTransitionMonitor.Reset();
        _currentDetectedItem = null;
        CurrentItemText.Text = "다시 인식 중...";
        CurrentItemAccentBar.Background = CreateBrush("#D4D9DF");
        _overlayWindow?.Hide();

        _recognitionMonitor.ScanNow();
        UpdateOverlayRuntimeUi();
    }

    private void ShowOverlayTest()
    {
        if (!_settings.OverlayEnabled)
            return;

        _overlayHiddenByHotkey = false;
        _overlayWindow ??= new OverlayWindow();

        if (TestItemComboBox.SelectedItem is LumiItem item)
            _overlayWindow.SetItem(item);

        _overlayWindow.ApplyAppearance(
            _settings.OverlayCompact,
            _settings.OverlayOpacity,
            _settings.OverlayScale);

        if (!_overlayWindow.IsVisible)
            _overlayWindow.Show();

        ApplySavedOverlayPosition();
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

        _overlayWindow?.ApplyAppearance(
            _settings.OverlayCompact,
            _settings.OverlayOpacity,
            _settings.OverlayScale);

        if (_overlayWindow?.IsVisible == true && !_overlayPositionEditMode)
            ApplySavedOverlayPosition();

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
        if (_loadingSettings)
            return;

        var previousStartup = _settings.StartWithWindows;
        SaveSettingsFromUi();

        if (previousStartup != _settings.StartWithWindows)
            _startupService.Apply(_settings.StartWithWindows);
    }

    private void OverlayScaleSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingSettings)
            return;

        _settings.OverlayScale = OverlayScaleSlider.Value;
        _settingsService.Save(_settings);
        UpdateOverlayAppearanceLabels();

        _overlayWindow?.ApplyAppearance(
            _settings.OverlayCompact,
            _settings.OverlayOpacity,
            _settings.OverlayScale);

        if (_overlayWindow?.IsVisible == true && !_overlayPositionEditMode)
            ApplySavedOverlayPosition();
    }

    private void OverlayOpacitySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingSettings)
            return;

        _settings.OverlayOpacity = OverlayOpacitySlider.Value;
        _settingsService.Save(_settings);
        UpdateOverlayAppearanceLabels();

        _overlayWindow?.ApplyAppearance(
            _settings.OverlayCompact,
            _settings.OverlayOpacity,
            _settings.OverlayScale);
    }

    private void UpdateOverlayAppearanceLabels()
    {
        if (OverlayScaleValueText is null || OverlayOpacityValueText is null)
            return;

        OverlayScaleValueText.Text = $"{Math.Round(_settings.OverlayScale * 100):0}%";
        OverlayOpacityValueText.Text = $"{Math.Round(_settings.OverlayOpacity * 100):0}%";
    }

    private void OverlayPositionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_overlayPositionEditMode)
        {
            BeginOverlayPositionEdit();
            return;
        }

        EndOverlayPositionEdit(save: true);
    }

    private void ResetOverlayPositionButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.OverlayPositionXRatio = 0.02;
        _settings.OverlayPositionYRatio = 0.16;
        _settingsService.Save(_settings);

        if (_overlayWindow?.IsVisible == true)
            ApplySavedOverlayPosition();

        OverlayPositionStatusText.Text = "기본 위치로 되돌렸습니다.";
    }

    private void BeginOverlayPositionEdit()
    {
        if (!_settings.OverlayEnabled)
            return;

        _overlayPositionEditMode = true;
        _overlayHiddenByHotkey = false;

        _overlayWindow ??= new OverlayWindow();

        if (_currentDetectedItem is not null)
            _overlayWindow.SetItem(_currentDetectedItem);
        else if (TestItemComboBox.SelectedItem is LumiItem previewItem)
            _overlayWindow.SetItem(previewItem);

        _overlayWindow.ApplyAppearance(
            _settings.OverlayCompact,
            _settings.OverlayOpacity,
            _settings.OverlayScale);

        ApplySavedOverlayPosition();

        if (!_overlayWindow.IsVisible)
            _overlayWindow.Show();

        _overlayWindow.SetPositionEditMode(true);
        _overlayWindow.Activate();

        OverlayPositionButton.Content = "위치 저장";
        OverlayPositionStatusText.Text = "오버레이를 드래그한 뒤 위치 저장을 누르세요.";
    }

    private void EndOverlayPositionEdit(bool save)
    {
        if (!_overlayPositionEditMode || _overlayWindow is null)
            return;

        if (save)
            SaveCurrentOverlayPosition();

        _overlayPositionEditMode = false;
        _overlayWindow.SetPositionEditMode(false);

        OverlayPositionButton.Content = "위치 조정";
        OverlayPositionStatusText.Text = save
            ? "게임 화면 기준 위치가 저장되었습니다."
            : "위치 조정을 취소했습니다.";

        if (_currentDetectedItem is null ||
            _overlaySuppressedByMatchEnd ||
            _overlayHiddenByHotkey ||
            (_settings.ShowOnlyWhenGameActive && !_gameMonitor.IsRunning))
        {
            _overlayWindow.Hide();
        }
    }

    private void SaveCurrentOverlayPosition()
    {
        if (_overlayWindow is null)
            return;

        double originX;
        double originY;
        double areaWidth;
        double areaHeight;

        if (_gameWindowService.TryGetGameWindow(out var game))
        {
            originX = game.X;
            originY = game.Y;
            areaWidth = Math.Max(1, game.Width - _overlayWindow.Width);
            areaHeight = Math.Max(1, game.Height - _overlayWindow.Height);
        }
        else
        {
            originX = SystemParameters.WorkArea.Left;
            originY = SystemParameters.WorkArea.Top;
            areaWidth = Math.Max(1, SystemParameters.WorkArea.Width - _overlayWindow.Width);
            areaHeight = Math.Max(1, SystemParameters.WorkArea.Height - _overlayWindow.Height);
        }

        _settings.OverlayPositionXRatio = Math.Clamp(
            (_overlayWindow.Left - originX) / areaWidth,
            0.0,
            1.0);

        _settings.OverlayPositionYRatio = Math.Clamp(
            (_overlayWindow.Top - originY) / areaHeight,
            0.0,
            1.0);

        _settingsService.Save(_settings);
    }

    private void ApplySavedOverlayPosition()
    {
        if (_overlayWindow is null)
            return;

        double originX;
        double originY;
        double areaWidth;
        double areaHeight;

        if (_gameWindowService.TryGetGameWindow(out var game))
        {
            originX = game.X;
            originY = game.Y;
            areaWidth = Math.Max(1, game.Width - _overlayWindow.Width);
            areaHeight = Math.Max(1, game.Height - _overlayWindow.Height);
        }
        else
        {
            originX = SystemParameters.WorkArea.Left;
            originY = SystemParameters.WorkArea.Top;
            areaWidth = Math.Max(1, SystemParameters.WorkArea.Width - _overlayWindow.Width);
            areaHeight = Math.Max(1, SystemParameters.WorkArea.Height - _overlayWindow.Height);
        }

        _overlayWindow.Left = originX +
            Math.Clamp(_settings.OverlayPositionXRatio, 0.0, 1.0) * areaWidth;

        _overlayWindow.Top = originY +
            Math.Clamp(_settings.OverlayPositionYRatio, 0.0, 1.0) * areaHeight;
    }

    private async void UpdateActionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_checkingForUpdates || _downloadingUpdate)
            return;

        if (_availableUpdate is null)
        {
            await CheckForUpdatesAsync(userInitiated: true);
            return;
        }

        if (_currentDetectedItem is not null && !_overlaySuppressedByMatchEnd)
        {
            System.Windows.MessageBox.Show(
                "게임이 끝난 뒤 업데이트를 설치해 주세요.",
                "오늘의 루미",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await DownloadAndInstallUpdateAsync(_availableUpdate);
    }

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_checkingForUpdates || _downloadingUpdate)
            return;

        _checkingForUpdates = true;
        UpdateActionButton.IsEnabled = false;
        UpdateStatusText.Text = "업데이트 확인 중...";

        try
        {
            _availableUpdate = await _updateService.CheckForUpdateAsync();

            if (_availableUpdate is null)
            {
                UpdateStatusText.Text = userInitiated
                    ? "현재 최신 버전입니다."
                    : "최신 버전을 사용 중입니다.";

                UpdateActionButton.Content = "업데이트 확인";
                return;
            }

            UpdateStatusText.Text =
                $"v{_availableUpdate.VersionText} 업데이트를 사용할 수 있습니다.";

            UpdateActionButton.Content = "업데이트";
        }
        catch
        {
            UpdateStatusText.Text = userInitiated
                ? "업데이트를 확인하지 못했습니다."
                : "자동 업데이트 확인에 실패했습니다.";

            _availableUpdate = null;
            UpdateActionButton.Content = "다시 확인";
        }
        finally
        {
            _checkingForUpdates = false;
            UpdateActionButton.IsEnabled = true;
        }
    }

    private async Task DownloadAndInstallUpdateAsync(UpdateInfo update)
    {
        _downloadingUpdate = true;
        UpdateActionButton.IsEnabled = false;
        UpdateProgressBar.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;
        UpdateStatusText.Text = $"v{update.VersionText} 다운로드 중...";

        try
        {
            var progress = new Progress<double>(value =>
            {
                UpdateProgressBar.Value = value * 100;
                UpdateStatusText.Text =
                    $"v{update.VersionText} 다운로드 중... {Math.Round(value * 100):0}%";
            });

            var installerPath =
                await _updateService.DownloadInstallerAsync(update, progress);

            UpdateStatusText.Text = "업데이트 설치를 시작합니다.";

            if (!_updateService.LaunchInstaller(installerPath))
            {
                UpdateStatusText.Text = "업데이트 설치 프로그램을 실행하지 못했습니다.";
                return;
            }

            await Task.Delay(300);
            ExitApplication();
        }
        catch
        {
            UpdateStatusText.Text = "업데이트 다운로드에 실패했습니다.";
        }
        finally
        {
            _downloadingUpdate = false;
            UpdateActionButton.IsEnabled = true;
            UpdateProgressBar.Visibility = Visibility.Collapsed;
        }
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

        if (_overlayPositionEditMode)
            EndOverlayPositionEdit(save: true);

        _gameMonitor.Dispose();
        _lobbyMonitor.Dispose();
        _matchTransitionMonitor.Dispose();
        _recognitionMonitor.Dispose();
        _hotkeyService.Dispose();
        _updateService.Dispose();

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
