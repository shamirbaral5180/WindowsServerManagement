using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.Core.Interfaces;

public interface IServerProfileRepository
{
    Task<IReadOnlyList<ServerProfile>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ServerProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default);
}

public interface ISecretStore
{
    Task<ServerSecret?> GetAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task SaveAsync(Guid profileId, ServerSecret secret, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default);
}

public interface IAppSettingsService
{
    AppSettings Current { get; }
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface IActivityLogRepository
{
    Task AppendAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityLogEntry>> LoadRecentAsync(int maximumCount, CancellationToken cancellationToken = default);
}

public interface IActivityLogService
{
    event EventHandler<ActivityLogEntry>? EntryAdded;
    Task LogAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken = default);
}
