using AbioticServerManager.Core.Networking;

namespace AbioticServerManager.Core.Services;

/// <summary>
/// Persists the last-seen LAN IPv4 between launches so the app can detect a
/// change and warn that port forwarding may need attention.
/// </summary>
public interface IInternalIpSnapshotStore
{
    Task<InternalIpSnapshot?> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(InternalIpSnapshot snapshot, CancellationToken ct = default);
}
