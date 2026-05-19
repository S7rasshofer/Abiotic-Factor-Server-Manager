namespace AbioticServerManager.Core.Runtime;

public enum PlayerActivityKind
{
    Joined,
    Left,
}

public sealed record PlayerActivityEvent(
    DateTimeOffset Timestamp,
    string PlayerName,
    PlayerActivityKind Kind,
    string RawLine)
{
    public string ActionText => Kind == PlayerActivityKind.Joined ? "joined" : "left";
}

/// <summary>
/// A play session: when a player joined and (if seen) left. Lets the Players view show
/// who is on now and how long past sessions lasted.
/// </summary>
public sealed class PlayerSession(string playerName, DateTimeOffset start)
{
    public string PlayerName { get; } = playerName;
    public DateTimeOffset Start { get; } = start;
    public DateTimeOffset? End { get; set; }

    public bool IsActive => End is null;

    public TimeSpan Duration => (End ?? DateTimeOffset.Now) - Start;

    public string DurationText
    {
        get
        {
            var d = Duration;
            if (d < TimeSpan.Zero)
            {
                d = TimeSpan.Zero;
            }

            return d.TotalHours >= 1
                ? $"{(int)d.TotalHours}h {d.Minutes}m"
                : d.TotalMinutes >= 1
                    ? $"{d.Minutes}m {d.Seconds}s"
                    : $"{d.Seconds}s";
        }
    }

    public string StatusText => IsActive ? "online" : "ended";
}
