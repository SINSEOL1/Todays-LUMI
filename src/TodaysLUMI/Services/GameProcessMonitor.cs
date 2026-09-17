using System.Diagnostics;
using System.Windows.Threading;

namespace TodaysLUMI.Services;

public sealed class GameProcessMonitor : IDisposable
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    private bool _isRunning;

    public event EventHandler<bool>? RunningStateChanged;

    public bool IsRunning => _isRunning;

    public GameProcessMonitor()
    {
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        Refresh();
        _timer.Start();
    }

    private void Refresh()
    {
        // 실제 실행 파일명을 추가 확인한 뒤 후보를 더 좁힐 예정.
        var running =
            Process.GetProcessesByName("EternalReturn").Length > 0 ||
            Process.GetProcessesByName("EternalReturn-Win64-Shipping").Length > 0;

        if (running == _isRunning)
            return;

        _isRunning = running;
        RunningStateChanged?.Invoke(this, running);
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
