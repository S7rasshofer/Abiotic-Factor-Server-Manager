using AbioticServerManager.Core.Admin;
using AbioticServerManager.Core.Models;
using Microsoft.Extensions.Logging;

namespace AbioticServerManager.Infrastructure.Persistence;

/// <summary>
/// Edits the real sectioned <c>Admin.ini</c> via <see cref="AdminIniBanEditor"/>.
/// Reuses <see cref="IAdminListService.ResolveAdminIniPath"/> so the ban list and
/// the moderator list always target the same file. Atomic write; never throws.
/// </summary>
public sealed class PlayerBanService : IPlayerBanService
{
    private readonly IAdminListService _adminList;
    private readonly ILogger<PlayerBanService> _logger;

    public PlayerBanService(IAdminListService adminList, ILogger<PlayerBanService> logger)
    {
        _adminList = adminList;
        _logger = logger;
    }

    public IReadOnlyList<string> ListBans(ServerInstance instance)
    {
        var path = _adminList.ResolveAdminIniPath(instance);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? AdminIniBanEditor.ListBans(File.ReadAllText(path))
            : [];
    }

    public BanResult Ban(ServerInstance instance, string playerId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new BanResult
            {
                Success = false,
                Message =
                    $"\"{displayName}\" has no captured SteamID/connect ID yet, so it " +
                    "cannot be banned reliably. Have them reconnect (the ID is captured " +
                    "from the server log on login), then try again.",
            };
        }

        return Edit(
            instance,
            text => AdminIniBanEditor.AddBan(text, playerId),
            already: text => AdminIniBanEditor.IsBanned(text, playerId),
            ok: $"\"{displayName}\" ({playerId}) was added to the ban list.",
            alreadyMsg: $"\"{displayName}\" ({playerId}) is already banned.");
    }

    public BanResult Unban(ServerInstance instance, string playerId) =>
        Edit(
            instance,
            text => AdminIniBanEditor.RemoveBan(text, playerId),
            already: text => !AdminIniBanEditor.IsBanned(text, playerId),
            ok: $"{playerId} was removed from the ban list.",
            alreadyMsg: $"{playerId} was not in the ban list.");

    private BanResult Edit(
        ServerInstance instance,
        Func<string, string> transform,
        Func<string, bool> already,
        string ok,
        string alreadyMsg)
    {
        var path = _adminList.ResolveAdminIniPath(instance);
        if (string.IsNullOrWhiteSpace(path))
        {
            return new BanResult
            {
                Success = false,
                Message = "Could not resolve the server's Admin.ini path. Prepare/Update " +
                          "the server first.",
            };
        }

        try
        {
            var original = File.Exists(path) ? File.ReadAllText(path) : "";
            if (already(original))
            {
                return new BanResult { Success = true, Message = alreadyMsg };
            }

            var updated = transform(original);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var temp = path + ".tmp";
            File.WriteAllText(temp, updated);
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }

            return new BanResult { Success = true, Message = ok };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Ban edit failed for {Path}", path);
            return new BanResult
            {
                Success = false,
                Message = "Could not update the ban list: " + ex.Message,
            };
        }
    }
}
