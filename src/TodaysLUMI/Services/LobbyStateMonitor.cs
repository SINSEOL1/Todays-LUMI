using System.Windows.Threading;

namespace TodaysLUMI.Services;

public sealed class LobbyStateMonitor : IDisposable
{
    private readonly GameWindowService _gameWindowService = new();
    private readonly LobbyDetector _detector = new();

    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private int _lobbyHits;
    private bool _isLobby;

    public event EventHandler? LobbyEntered;

    public LobbyStateMonitor()
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
        if (!_gameWindowService.TryGetGameWindow(out var window))
        {
            _lobbyHits = 0;
            _isLobby = false;
            return;
        }

        bool lobby;

        try
        {
            lobby = _detector.IsLobby(window);
        }
        catch
        {
            return;
        }

        if (!lobby)
        {
            _lobbyHits = 0;
            _isLobby = false;
            return;
        }

        _lobbyHits++;

        // Require two consecutive detections so a bright in-game frame cannot
        // accidentally clear the current LUMI item.
        if (_lobbyHits < 2 || _isLobby)
            return;

        _isLobby = true;
        LobbyEntered?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _timer.Stop();
}
