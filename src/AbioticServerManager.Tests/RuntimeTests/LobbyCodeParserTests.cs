using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Tests.RuntimeTests;

/// <summary>
/// §4.9: the lobby-code parser, verified against a verbatim line from a real
/// Facility Overseer dedicated-server <c>AbioticFactor.log</c>.
/// </summary>
public class LobbyCodeParserTests
{
    [Fact]
    public void Parses_short_code_from_real_session_log_line()
    {
        const string line =
            "[2026.05.21-21.24.50:928][  1]LogOnlineSession: EOS: " +
            "EOS_SessionModification_AddAttribute() named (ShortCode) with value (O8TXQ)";

        Assert.Equal("O8TXQ", LobbyCodeParser.TryParse(line));
    }

    [Theory]
    [InlineData("EOS_SessionModification_AddAttribute() named (PlayerCount) with value (1)")]
    [InlineData("LogOnlineSession: Display: OSS: Session creation completed.")]
    [InlineData("LogNet: Login request: ?Name=S7razzy??ConnectID=76561198104903704")]
    [InlineData("LogMemory: tick complete")]
    [InlineData("")]
    [InlineData(null)]
    public void Returns_null_for_non_short_code_lines(string? line) =>
        Assert.Null(LobbyCodeParser.TryParse(line));

    [Fact]
    public void Empty_parenthesised_value_yields_null()
    {
        const string line =
            "EOS_SessionModification_AddAttribute() named (ShortCode) with value ()";

        Assert.Null(LobbyCodeParser.TryParse(line));
    }
}
