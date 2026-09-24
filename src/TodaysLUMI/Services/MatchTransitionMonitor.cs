using System.Windows.Threading;

namespace TodaysLUMI.Services;

public sealed class MatchTransitionMonitor : IDisposable
{
    private readonly GameWindowService _gameWindowService = new();
    private readonly InGameHudDetector _hudDetector = new();

    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };

    private bool _hudSeen;
    private bool _hudLost;
    private int _missingHits;

    public bool IsInMatch => _hudSeen && !_hudLost && _missingHits == 0;

    public event EventHandler? MatchEnding;
    public event EventHandler? MatchHudReturned;

    public MatchTransitionMonitor()
    {
        _timer.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        Refresh();
        _timer.Start();
    }

    public void Reset()
    {
        _hudSeen = false;
        _hudLost = false;
        _missingHits = 0;
    }

    private void Refresh()
    {
        if (!_gameWindowService.TryGetGameWindow(out var window))
        {
            Reset();
            return;
        }

        bool hudPresent;

        try
        {
            hudPresent = _hudDetector.HasInGameHud(window);
        }
        catch
        {
            return;
        }

        if (hudPresent)
        {
            _hudSeen = true;
            _missingHits = 0;

            if (_hudLost)
            {
                _hudLost = false;
                MatchHudReturned?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        // Never call a transition until an actual in-game HUD has been seen.
        // This prevents lobby/startup screens from being treated as match end.
        if (!_hudSeen || _hudLost)
            return;

        _missingHits++;

        // 3 x 250 ms gives a quick hide at result loading while tolerating a
        // single capture miss or short UI animation.
        if (_missingHits < 3)
            return;

        _hudLost = true;
        MatchEnding?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _timer.Stop();
}
