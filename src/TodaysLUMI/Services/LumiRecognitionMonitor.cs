using System.Windows.Threading;
using TodaysLUMI.Models;

namespace TodaysLUMI.Services;

public sealed class LumiRecognitionMonitor : IDisposable
{
    private static readonly TimeSpan SearchingInterval = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan ConfirmedInterval = TimeSpan.FromSeconds(2);

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
    private int _confirmedLineMissingScans;

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
        _lastConfirmed = null;
        _confirmedLineMissingScans = 0;
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        Scan();
    }

    private void Scan()
    {
        if (!_isEnabled())
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

                if (_lastConfirmed is not null)
                {
                    _confirmedLineMissingScans++;

                    if (_confirmedLineMissingScans >= 3)
                        ResetForNextGame();
                }
                else
                {
                    _timer.Interval = SearchingInterval;
                }

                return;
            }

            _confirmedLineMissingScans = 0;

            if (_lastConfirmed?.Key == detected.Key)
            {
                _timer.Interval = ConfirmedInterval;
                return;
            }

            if (_candidate?.Key == detected.Key)
            {
                _candidateHits++;
            }
            else
            {
                _candidate = detected;
                _candidateHits = 1;
            }

            // The same result must be visible in two captures before it is accepted.
            if (_candidateHits < 2)
                return;

            _lastConfirmed = detected;
            _candidate = null;
            _candidateHits = 0;
            _timer.Interval = ConfirmedInterval;

            ItemDetected?.Invoke(this, detected);
        }
        catch
        {
            // A recognition failure must never affect the game or close the app.
        }
    }

    private void ResetForNextGame()
    {
        _candidate = null;
        _candidateHits = 0;
        _lastConfirmed = null;
        _confirmedLineMissingScans = 0;
        _timer.Interval = SearchingInterval;
    }

    public void Dispose() => _timer.Stop();
}
