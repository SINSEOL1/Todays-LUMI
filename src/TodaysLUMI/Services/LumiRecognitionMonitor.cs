using System.Windows.Threading;
using TodaysLUMI.Models;

namespace TodaysLUMI.Services;

public sealed class LumiRecognitionMonitor : IDisposable
{
    private readonly GameWindowService _gameWindowService = new();
    private readonly ScreenCaptureService _captureService = new();
    private readonly LumiSignatureRecognizer _recognizer = new();
    private readonly Func<bool> _isEnabled;

    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(600)
    };

    private LumiItem? _candidate;
    private int _candidateHits;
    private LumiItem? _lastConfirmed;

    public event EventHandler<LumiItem>? ItemDetected;

    public LumiRecognitionMonitor(Func<bool> isEnabled)
    {
        _isEnabled = isEnabled;
        _timer.Tick += (_, _) => Scan();
    }

    public void Start() => _timer.Start();

    public void ScanNow()
    {
        _candidate = null;
        _candidateHits = 0;
        Scan();
    }

    private void Scan()
    {
        if (!_isEnabled())
            return;

        if (!_gameWindowService.TryGetGameWindow(out var window))
            return;

        try
        {
            using var bitmap = _captureService.CaptureChatRegion(window);

            if (!_recognizer.TryRecognize(bitmap, out var detected) || detected is null)
            {
                _candidate = null;
                _candidateHits = 0;
                return;
            }

            if (_candidate?.Key == detected.Key)
                _candidateHits++;
            else
            {
                _candidate = detected;
                _candidateHits = 1;
            }

            // Require the same result twice to avoid a one-frame false positive.
            if (_candidateHits < 2)
                return;

            if (_lastConfirmed?.Key == detected.Key)
                return;

            _lastConfirmed = detected;
            ItemDetected?.Invoke(this, detected);
        }
        catch
        {
            // Recognition failures must never affect the game or the main app.
        }
    }

    public void Dispose() => _timer.Stop();
}
