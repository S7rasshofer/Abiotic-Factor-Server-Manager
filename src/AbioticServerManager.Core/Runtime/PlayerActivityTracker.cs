namespace AbioticServerManager.Core.Runtime;

public sealed class PlayerActivityTracker
{
    private readonly List<string> _activePlayers = [];
    private readonly List<PlayerActivityEvent> _history = [];
    private readonly List<PlayerSession> _sessions = [];
    private readonly int _historyLimit;

    public PlayerActivityTracker(int historyLimit = 500)
    {
        _historyLimit = Math.Max(1, historyLimit);
    }

    public IReadOnlyList<string> ActivePlayers => _activePlayers;

    public IReadOnlyList<PlayerActivityEvent> History => _history;

    /// <summary>Play sessions, newest first; an open session has a null End.</summary>
    public IReadOnlyList<PlayerSession> Sessions => _sessions;

    public bool HasSeenActivity => _history.Count > 0;

    public PlayerActivityEvent? Apply(ServerLogLine line)
    {
        var activity = PlayerActivityParser.TryParse(line);
        if (activity is null)
        {
            return null;
        }

        if (activity.Kind == PlayerActivityKind.Joined)
        {
            if (!_activePlayers.Any(p => string.Equals(p, activity.PlayerName, StringComparison.OrdinalIgnoreCase)))
            {
                _activePlayers.Add(activity.PlayerName);
                _activePlayers.Sort(StringComparer.OrdinalIgnoreCase);
            }

            _sessions.Insert(0, new PlayerSession(activity.PlayerName, activity.Timestamp));
            if (_sessions.Count > _historyLimit)
            {
                _sessions.RemoveRange(_historyLimit, _sessions.Count - _historyLimit);
            }
        }
        else
        {
            var existing = _activePlayers
                .FirstOrDefault(p => string.Equals(p, activity.PlayerName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _activePlayers.Remove(existing);
            }

            var openSession = _sessions.FirstOrDefault(s =>
                s.IsActive &&
                string.Equals(s.PlayerName, activity.PlayerName, StringComparison.OrdinalIgnoreCase));
            if (openSession is not null)
            {
                openSession.End = activity.Timestamp;
            }
        }

        _history.Insert(0, activity);
        if (_history.Count > _historyLimit)
        {
            _history.RemoveRange(_historyLimit, _history.Count - _historyLimit);
        }

        return activity;
    }
}
