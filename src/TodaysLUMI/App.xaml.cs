using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TodaysLUMI;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        try
        {
            WriteStartupLog("Application startup");

            _mainWindow = new MainWindow();
            MainWindow = _mainWindow;

            _mainWindow.Show();
            _mainWindow.Activate();

            if (e.Args.Any(arg =>
                    arg.Equals("--tray", StringComparison.OrdinalIgnoreCase)))
            {
                _mainWindow.Dispatcher.BeginInvoke(() => _mainWindow.Hide());
            }

            WriteStartupLog("Main window shown");
        }
        catch (Exception ex)
        {
            WriteStartupLog("Startup failure", ex);

            System.Windows.MessageBox.Show(
                "오늘의 루미를 시작하지 못했습니다.\n\n" +
                "오류 로그가 다음 위치에 저장되었습니다.\n" +
                GetLogPath(),
                "오늘의 루미",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    private void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        WriteStartupLog("Unhandled UI exception", e.Exception);

        System.Windows.MessageBox.Show(
            "프로그램 실행 중 오류가 발생했습니다.\n" +
            "startup.log를 확인해 주세요.",
            "오늘의 루미",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteStartupLog("Unhandled application exception", ex);
        else
            WriteStartupLog("Unhandled application exception");
    }

    private static void WriteStartupLog(string message, Exception? ex = null)
    {
        try
        {
            var path = GetLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var text =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}" +
                (ex is null
                    ? Environment.NewLine
                    : Environment.NewLine + ex + Environment.NewLine);

            File.AppendAllText(path, text);
        }
        catch
        {
            // Logging must never prevent application startup.
        }
    }

    private static string GetLogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TodaysLUMI",
            "startup.log");
    }
}
