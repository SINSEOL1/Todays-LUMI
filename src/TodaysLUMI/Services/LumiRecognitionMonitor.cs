using System.Windows.Threading;
using TodaysLUMI.Models;

namespace TodaysLUMI.Services;

public sealed class LumiRecognitionMonitor : IDisposable
{
    private static readonly TimeSpan SearchingInterval = TimeSpan.FromMilliseconds(600);

    private readonly GameWindowService _gameWindowService = new();
    private readonly ScreenCaptureService _captureService = new();
    private readonly LumiSignatureRecognizer _recognizer = new();
    private readonly Func<bool> _isEnabled;

    private readonly DispatcherTimer _timer = new()
    {
        Interval = SearchingInterval
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
        ResetForNextGame();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        Scan();
    }

    public void ResetForLobby()
    {
        ResetForNextGame();
    }

    private void Scan()
    {
        if (!_isEnabled() || _lastConfirmed is not null)
            return;

        if (!_gameWindowService.TryGetGameWindow(out var window))
        {
            ResetForNextGame();
            return;
        }

        try
        {
            using var bitmap = _captureService.CaptureChatRegion(window);

            if (!_recognizer.TryRecognize(bitmap, out var detected) || detected is null)
            {
                _candidate = null;
                _candidateHits = 0;
                _timer.Interval = SearchingInterval;
                return;
            }

            AcceptCandidate(detected);
        }
        catch
        {
        }
    }

    private void AcceptCandidate(LumiItem detected)
    {
        if (_candidate?.Key == detected.Key)
        {
            _candidateHits++;
        }
        else
        {
            _candidate = detected;
            _candidateHits = 1;
        }

        if (_candidateHits < 2)
        {
            _timer.Interval = SearchingInterval;
            return;
        }

        _lastConfirmed = detected;
        _candidate = null;
        _candidateHits = 0;
        _timer.Stop();

        ItemDetected?.Invoke(this, detected);
    }

    private void ResetForNextGame()
    {
        _candidate = null;
        _candidateHits = 0;
        _lastConfirmed = null;
        _timer.Interval = SearchingInterval;

        if (!_timer.IsEnabled)
            _timer.Start();
    }

    public void Dispose() => _timer.Stop();
}
