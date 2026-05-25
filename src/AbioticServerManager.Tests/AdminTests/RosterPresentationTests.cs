using AbioticServerManager.Core.Admin;
using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Tests.AdminTests;

/// <summary>
/// Pure tests for Sec 3.2 (banned ids excluded from active roster, banned page rows
/// built from sectioned ids + roster) and Sec 3.3 (IsAdmin derivation).
/// Covers <c>BannedPagePresentationTests</c> and <c>AdminMarkerPresentationTests</c>
/// from the master plan's test matrix.
/// </summary>
public class RosterPresentationTests
{
    private static PlayerRosterEntry Entry(string name, string? steamId = null) =>
        new()
        {
            Key = steamId ?? name.ToLowerInvariant(),
            DisplayName = name,
            SteamId64 = steamId,
            IsOnline = true,
            LastSeenAt = DateTimeOffset.Now,
        };

    [Fact]
    public void Filter_active_excludes_banned_steamids_from_roster()
    {
        var roster = new List<PlayerRosterEntry>
        {
            Entry("Alice", "76561198000000001"),
            Entry("BannedBob", "76561198000000099"),
            Entry("Charlie", "76561198000000003"),
        };

        var filtered = RosterPresentation.FilterActive(
            roster,
            ["76561198000000099"]);

        Assert.Equal(2, filtered.Count);
        Assert.DoesNotContain(filtered, e => e.SteamId64 == "76561198000000099");
        Assert.Contains(filtered, e => e.DisplayName == "Alice");
        Assert.Contains(filtered, e => e.DisplayName == "Charlie");
    }

    [Fact]
    public void Filter_active_keeps_name_only_entries_even_if_a_banned_id_is_present()
    {
        // A name-only roster entry has no SteamID64 captured yet; it must
        // never be filtered out by a banned-id list (no way to match it).
        var roster = new List<PlayerRosterEntry>
        {
            Entry("NameOnly"),
            Entry("BannedBob", "76561198000000099"),
        };

        var filtered = RosterPresentation.FilterActive(
            roster,
            ["76561198000000099"]);

        Assert.Single(filtered);
        Assert.Equal("NameOnly", filtered[0].DisplayName);
    }

    [Fact]
    public void Filter_active_with_no_bans_returns_the_input_roster()
    {
        var roster = new List<PlayerRosterEntry>
        {
            Entry("Alice", "76561198000000001"),
        };

        Assert.Same(roster, RosterPresentation.FilterActive(roster, []));
    }

    [Fact]
    public void Build_banned_rows_fills_in_last_known_name_from_roster()
    {
        var roster = new List<PlayerRosterEntry>
        {
            Entry("BannedBob", "76561198000000099"),
        };

        var rows = RosterPresentation.BuildBannedRows(
            ["76561198000000099", "76561198000000123"],
            roster);

        Assert.Equal(2, rows.Count);
        var bob = rows.First(r => r.Id == "76561198000000099");
        Assert.Equal("BannedBob", bob.DisplayName);
        Assert.Equal(BanSource.RawIni, bob.Source);
        Assert.Empty(bob.Notes);
        Assert.Empty(bob.DateBannedText);

        var unknown = rows.First(r => r.Id == "76561198000000123");
        Assert.Empty(unknown.DisplayName); // never seen
    }

    [Fact]
    public void Banned_row_source_text_and_date_text_render_predictably()
    {
        var rawIni = new BannedPlayerRow(
            Id: "76561198000000001",
            DisplayName: "",
            DateBanned: null,
            Source: BanSource.RawIni,
            Notes: "");

        var manual = new BannedPlayerRow(
            Id: "76561198000000001",
            DisplayName: "Manual",
            DateBanned: new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
            Source: BanSource.Manual,
            Notes: "");

        Assert.Equal("raw INI", rawIni.SourceText);
        Assert.Equal(string.Empty, rawIni.DateBannedText);
        Assert.Equal("manual", manual.SourceText);
        Assert.NotEmpty(manual.DateBannedText);
    }

    [Fact]
    public void Is_admin_is_true_only_when_steamid_matches_moderator_list()
    {
        var moderators = new[] { "76561198000000001", "76561198000000003" };

        Assert.True(RosterPresentation.IsAdmin(Entry("Alice", "76561198000000001"), moderators));
        Assert.False(RosterPresentation.IsAdmin(Entry("Bob", "76561198000000002"), moderators));
        // Name-only entries cannot be admins until we know their SteamID64.
        Assert.False(RosterPresentation.IsAdmin(Entry("NameOnly"), moderators));
    }

    [Fact]
    public void Is_admin_is_false_when_moderator_list_is_empty()
    {
        Assert.False(RosterPresentation.IsAdmin(
            Entry("Alice", "76561198000000001"),
            []));
    }
}
