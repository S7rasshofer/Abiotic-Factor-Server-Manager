using AbioticServerManager.Core.Config;
using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Tests.ConfigTests;

public class ConfigValidatorTests
{
    private static readonly ConfigValidator Validator = new();

    private static ServerInstance Valid() => new()
    {
        SteamServerName = "Test",
        WorldSaveName = "Cascade",
        AdminPassword = "admin",
        MaxPlayers = 6,
        GamePort = 7777,
        QueryPort = 27015,
    };

    private static bool HasCode(IEnumerable<DiagnosticMessage> r, string code) =>
        r.Any(m => m.Code == code);

    [Fact]
    public void Valid_config_reports_success()
    {
        var result = Validator.Validate(Valid(), []);
        Assert.True(HasCode(result, "CONFIG_OK"));
        Assert.DoesNotContain(result, m => m.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Detects_empty_names()
    {
        var instance = Valid();
        instance.SteamServerName = "";
        instance.WorldSaveName = "  ";

        var result = Validator.Validate(instance, []);

        Assert.True(HasCode(result, "SERVER_NAME_EMPTY"));
        Assert.True(HasCode(result, "WORLD_NAME_EMPTY"));
    }

    [Fact]
    public void Detects_max_players_out_of_range()
    {
        var instance = Valid();
        instance.MaxPlayers = 99;

        Assert.True(HasCode(Validator.Validate(instance, []), "MAX_PLAYERS_RANGE"));
    }

    [Fact]
    public void Warns_when_max_players_above_recommended()
    {
        var instance = Valid();
        instance.MaxPlayers = 12;

        Assert.True(HasCode(Validator.Validate(instance, []), "MAX_PLAYERS_RECOMMENDED"));
    }

    [Fact]
    public void Detects_identical_game_and_query_port()
    {
        var instance = Valid();
        instance.QueryPort = instance.GamePort;

        Assert.True(HasCode(Validator.Validate(instance, []), "PORT_CONFLICT"));
    }

    [Fact]
    public void Detects_port_conflict_with_other_world()
    {
        var a = Valid();
        var b = Valid();
        b.Id = Guid.NewGuid().ToString("N");
        b.DisplayName = "Other";

        Assert.True(HasCode(Validator.Validate(a, [b]), "PORT_CONFLICT"));
    }

    [Fact]
    public void Warns_when_admin_password_missing()
    {
        var instance = Valid();
        instance.AdminPassword = "";

        Assert.True(HasCode(Validator.Validate(instance, []), "ADMIN_PASSWORD_EMPTY"));
    }

    [Fact]
    public void Flags_use_local_ips()
    {
        var instance = Valid();
        instance.UseLocalIps = true;

        Assert.True(HasCode(Validator.Validate(instance, []), "USE_LOCAL_IPS_WARNING"));
    }
}
