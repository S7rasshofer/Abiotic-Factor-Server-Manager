using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Admin;

public sealed record BanResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Bans/unbans players by editing the dedicated server's <c>Admin.ini</c>
/// <c>[BannedPlayers]</c> section. Abiotic Factor reads this file on start, so a
/// ban applied while the server is running needs a restart to take effect (the
/// caller decides whether to offer that).
/// </summary>
public interface IPlayerBanService
{
    BanResult Ban(ServerInstance instance, string playerId, string displayName);

    BanResult Unban(ServerInstance instance, string playerId);

    IReadOnlyList<string> ListBans(ServerInstance instance);
}
