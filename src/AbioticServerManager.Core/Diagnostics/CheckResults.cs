namespace AbioticServerManager.Core.Diagnostics;

public enum CheckStatus
{
    Unknown,
    Pass,
    Fail,
}

public sealed record QueryCheckResult
{
    public required CheckStatus Status { get; init; }
    public string Detail { get; init; } = "";
}

public sealed record ExternalVisibilityResult
{
    public required CheckStatus Status { get; init; }
    public string Detail { get; init; } = "";

    /// <summary>Actionable guidance shown in the diagnostics panel (port forwarding, etc.).</summary>
    public IReadOnlyList<string> Guidance { get; init; } = [];
}

public sealed record VersionCheckResult
{
    public required CheckStatus Status { get; init; }
    public string Detail { get; init; } = "";
}
