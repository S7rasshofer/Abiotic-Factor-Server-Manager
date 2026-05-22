using AbioticServerManager.Core.Backup;

namespace AbioticServerManager.Tests.BackupTests;

public class BackupConfidenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);

    private static BackupEntry Backup(
        bool world = true,
        bool sandbox = true,
        bool admin = true,
        DateTimeOffset? createdAt = null) => new()
    {
        Id = "20260520-120000",
        Path = @"C:\backups\x",
        CreatedAt = createdAt ?? Now,
        Reason = "manual",
        SizeBytes = 0,
        IncludedWorldSave = world,
        IncludedSandboxIni = sandbox,
        IncludedAdminIni = admin,
    };

    [Fact]
    public void Full_when_world_sandbox_and_admin_present()
    {
        var c = BackupConfidenceCalculator.Evaluate(Backup(), Now);
        Assert.Equal(BackupConfidenceLevel.Full, c.Level);
        Assert.Equal("Full", c.Label);
        Assert.Contains("World save", c.Detail);
    }

    [Fact]
    public void Partial_when_world_present_but_sandbox_missing()
    {
        var c = BackupConfidenceCalculator.Evaluate(Backup(sandbox: false), Now);
        Assert.Equal(BackupConfidenceLevel.Partial, c.Level);
        Assert.Contains("sandbox settings", c.Detail);
    }

    [Fact]
    public void Partial_when_world_present_but_admin_missing()
    {
        var c = BackupConfidenceCalculator.Evaluate(Backup(admin: false), Now);
        Assert.Equal(BackupConfidenceLevel.Partial, c.Level);
        Assert.Contains("admin", c.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Low_when_no_world_save()
    {
        var c = BackupConfidenceCalculator.Evaluate(Backup(world: false), Now);
        Assert.Equal(BackupConfidenceLevel.Low, c.Level);
        Assert.Equal("Limited", c.Label);
        Assert.Contains("restore will not", c.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(45, "just now")]
    [InlineData(90, "1m ago")]
    [InlineData(3600, "1h ago")]
    [InlineData(86400, "1d ago")]
    public void Age_label_uses_compact_units(int secondsAgo, string expected)
    {
        var c = BackupConfidenceCalculator.Evaluate(
            Backup(createdAt: Now - TimeSpan.FromSeconds(secondsAgo)),
            Now);
        Assert.Equal(expected, c.AgeLabel);
    }

    [Fact]
    public void Stale_flag_flips_after_threshold()
    {
        var fresh = BackupConfidenceCalculator.Evaluate(
            Backup(createdAt: Now - TimeSpan.FromDays(3)), Now);
        Assert.False(fresh.IsStale);

        var stale = BackupConfidenceCalculator.Evaluate(
            Backup(createdAt: Now - TimeSpan.FromDays(8)), Now);
        Assert.True(stale.IsStale);
        Assert.Contains("staleness", stale.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stale_threshold_is_configurable()
    {
        var c = BackupConfidenceCalculator.Evaluate(
            Backup(createdAt: Now - TimeSpan.FromHours(2)),
            Now,
            staleAfter: TimeSpan.FromHours(1));
        Assert.True(c.IsStale);
    }
}
