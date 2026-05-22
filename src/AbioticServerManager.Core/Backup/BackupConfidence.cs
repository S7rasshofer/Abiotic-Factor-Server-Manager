namespace AbioticServerManager.Core.Backup;

/// <summary>
/// "How much can I trust this backup?" reduced to a band the UI can color.
/// Distinct from integrity-of-bytes — we don't hash payloads here; we judge
/// completeness (world+sandbox+admin) plus age.
/// </summary>
public enum BackupConfidenceLevel
{
    /// <summary>Missing core artifacts (no world save).</summary>
    Low,

    /// <summary>Has world save but missing either sandbox or admin ini.</summary>
    Partial,

    /// <summary>All three artifacts present.</summary>
    Full,
}

public sealed record BackupConfidence
{
    public required BackupConfidenceLevel Level { get; init; }
    public required string Label { get; init; }

    /// <summary>Short human-readable reason for the badge tooltip.</summary>
    public required string Detail { get; init; }

    /// <summary>True when the backup is older than the configured "stale" threshold.</summary>
    public required bool IsStale { get; init; }

    /// <summary>Wall-clock age of the backup at evaluation time.</summary>
    public required TimeSpan Age { get; init; }

    /// <summary>Compact age label suitable for a chip ("3h ago", "5d ago").</summary>
    public required string AgeLabel { get; init; }
}

public static class BackupConfidenceCalculator
{
    /// <summary>Default age threshold beyond which a backup is considered stale.</summary>
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromDays(7);

    public static BackupConfidence Evaluate(
        BackupEntry entry,
        DateTimeOffset now,
        TimeSpan? staleAfter = null)
    {
        var threshold = staleAfter ?? DefaultStaleAfter;
        var age = now - entry.CreatedAt;
        var isStale = age > threshold;

        var (level, detail) = ClassifyContents(entry);
        var label = level switch
        {
            BackupConfidenceLevel.Full => "Full",
            BackupConfidenceLevel.Partial => "Partial",
            BackupConfidenceLevel.Low => "Limited",
            _ => "Unknown",
        };

        return new BackupConfidence
        {
            Level = level,
            Label = label,
            Detail = detail + (isStale ? " · Older than the staleness threshold." : ""),
            IsStale = isStale,
            Age = age,
            AgeLabel = FormatAge(age),
        };
    }

    private static (BackupConfidenceLevel level, string detail) ClassifyContents(BackupEntry entry)
    {
        if (entry.IncludedWorldSave && entry.IncludedSandboxIni && entry.IncludedAdminIni)
        {
            return (BackupConfidenceLevel.Full,
                "World save + sandbox settings + admin/ban list all captured.");
        }

        if (entry.IncludedWorldSave)
        {
            var missing = new List<string>();
            if (!entry.IncludedSandboxIni) missing.Add("sandbox settings");
            if (!entry.IncludedAdminIni) missing.Add("admin / ban list");
            return (BackupConfidenceLevel.Partial,
                $"World save present; missing {string.Join(" and ", missing)}.");
        }

        return (BackupConfidenceLevel.Low,
            "No world save in this backup — restore will not bring the world back.");
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 0) return "just now";
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours}h ago";
        if (age.TotalDays < 14) return $"{(int)age.TotalDays}d ago";
        return $"{(int)(age.TotalDays / 7)}w ago";
    }
}
