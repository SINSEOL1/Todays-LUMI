using System.Windows.Threading;
using TodaysLUMI.Models;

namespace TodaysLUMI.Services;

public sealed class LumiRecognitionMonitor : IDisposable
{
    private static readonly TimeSpan SearchingInterval = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan ConfirmedInterval = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ArmAfterMissing = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan MinimumGameGap = TimeSpan.FromSeconds(20);

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

    private DateTime _confirmedAtUtc = DateTime.MinValue;
    private DateTime? _signatureMissingSinceUtc;
    private bool _nextGameArmed;

    public event EventHandler<LumiItem>? ItemDetected;

    public LumiRecognitionMonitor(Func<bool> isEnabled)
    {
        _isEnabled = isEnabled;
        _timer.Tick += (_, _) => Scan();
    }

    public void Start() => _timer.Start();

    public void ScanNow()
    {
        // Manual re-scan intentionally clears the current lock.
        ResetForNextGame();
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
                HandleMissingSignature();
                return;
            }

            HandleDetectedSignature(detected);
        }
        catch
        {
            // Recognition failures must never affect the game or close the app.
        }
    }

    private void HandleMissingSignature()
    {
        _candidate = null;
        _candidateHits = 0;

        if (_lastConfirmed is null)
        {
            _timer.Interval = SearchingInterval;
            return;
        }

        _signatureMissingSinceUtc ??= DateTime.UtcNow;

        var missingLongEnough =
            DateTime.UtcNow - _signatureMissingSinceUtc.Value >= ArmAfterMissing;

        var enoughTimeSinceConfirmation =
            DateTime.UtcNow - _confirmedAtUtc >= MinimumGameGap;

        if (missingLongEnough && enoughTimeSinceConfirmation)
            _nextGameArmed = true;

        // Keep the confirmed item locked. Do not clear it just because
        // the chat line scrolled away.
        _timer.Interval = ConfirmedInterval;
    }

    private void HandleDetectedSignature(LumiItem detected)
    {
        if (_lastConfirmed is null)
        {
            AcceptCandidate(detected, requiredHits: 2);
            return;
        }

        // The currently confirmed item is still visible.
        // Keep it locked and cancel any next-game candidate.
        if (_lastConfirmed.Key == detected.Key)
        {
            _candidate = null;
            _candidateHits = 0;
            _signatureMissingSinceUtc = null;
            _nextGameArmed = false;
            _timer.Interval = ConfirmedInterval;
            return;
        }

        // A different-looking colored UI element must never be allowed to
        // replace the confirmed LUMI item during the same game.
        if (!_nextGameArmed)
        {
            _candidate = null;
            _candidateHits = 0;
            _timer.Interval = ConfirmedInterval;
            return;
        }

        // A new game candidate must remain identical across several captures.
        AcceptCandidate(detected, requiredHits: 4);
    }

    private void AcceptCandidate(LumiItem detected, int requiredHits)
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

        if (_candidateHits < requiredHits)
        {
            _timer.Interval = SearchingInterval;
            return;
        }

        _lastConfirmed = detected;
        _confirmedAtUtc = DateTime.UtcNow;
        _signatureMissingSinceUtc = null;
        _nextGameArmed = false;
        _candidate = null;
        _candidateHits = 0;
        _timer.Interval = ConfirmedInterval;

        ItemDetected?.Invoke(this, detected);
    }

    private void ResetForNextGame()
    {
        _candidate = null;
        _candidateHits = 0;
        _lastConfirmed = null;
        _confirmedAtUtc = DateTime.MinValue;
        _signatureMissingSinceUtc = null;
        _nextGameArmed = false;
        _timer.Interval = SearchingInterval;
    }

    public void Dispose() => _timer.Stop();
}
