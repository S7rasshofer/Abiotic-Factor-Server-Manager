using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Tests.NetworkingTests;

public class InternalIpChangeTrackerTests
{
    private static readonly DateTimeOffset Then = new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_run_when_no_last_seen()
    {
        var result = InternalIpChangeTracker.Detect(lastSeen: null, currentIpv4: "192.168.1.42");
        Assert.Equal(InternalIpChange.FirstRun, result);
    }

    [Fact]
    public void First_run_when_no_last_seen_even_if_current_is_missing()
    {
        // No previous snapshot AND no current IP - still FirstRun (we have no
        // baseline to claim something was "lost").
        var result = InternalIpChangeTracker.Detect(lastSeen: null, currentIpv4: null);
        Assert.Equal(InternalIpChange.FirstRun, result);
    }

    [Fact]
    public void Unchanged_when_current_matches_last_seen()
    {
        var last = new InternalIpSnapshot("192.168.1.42", Then);
        var result = InternalIpChangeTracker.Detect(last, "192.168.1.42");
        Assert.Equal(InternalIpChange.Unchanged, result);
    }

    [Fact]
    public void Changed_when_current_differs_from_last_seen()
    {
        var last = new InternalIpSnapshot("192.168.1.42", Then);
        var result = InternalIpChangeTracker.Detect(last, "192.168.1.99");
        Assert.Equal(InternalIpChange.Changed, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Lost_when_we_had_an_ip_but_now_dont(string? current)
    {
        var last = new InternalIpSnapshot("192.168.1.42", Then);
        var result = InternalIpChangeTracker.Detect(last, current);
        Assert.Equal(InternalIpChange.Lost, result);
    }

    [Fact]
    public void Snapshot_for_returns_null_when_no_ip_to_record()
    {
        // Important: do NOT clobber a good last-seen value with a transient
        // nothing - the persistence layer should keep the previous snapshot
        // when the current one is empty.
        Assert.Null(InternalIpChangeTracker.SnapshotFor(null, Then));
        Assert.Null(InternalIpChangeTracker.SnapshotFor("", Then));
        Assert.Null(InternalIpChangeTracker.SnapshotFor("   ", Then));
    }

    [Fact]
    public void Snapshot_for_captures_current_ip_and_time()
    {
        var snap = InternalIpChangeTracker.SnapshotFor("10.0.0.5", Then);
        Assert.NotNull(snap);
        Assert.Equal("10.0.0.5", snap!.Ipv4);
        Assert.Equal(Then, snap.CapturedAt);
    }

    [Fact]
    public void Comparison_is_case_sensitive_so_canonical_form_matters()
    {
        // IPv4 strings don't have casing in practice; the test pins the
        // ordinal comparison so future IPv6 work doesn't quietly broaden it.
        var last = new InternalIpSnapshot("192.168.1.42", Then);
        Assert.Equal(InternalIpChange.Unchanged,
            InternalIpChangeTracker.Detect(last, "192.168.1.42"));
    }
}
