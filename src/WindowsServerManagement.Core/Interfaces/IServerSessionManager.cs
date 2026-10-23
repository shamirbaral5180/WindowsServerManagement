using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.Core.Interfaces;

public interface IServerSessionManager
{
    event EventHandler<ServerConnectionStatus>? StatusChanged;

    bool IsConnected(Guid serverId);
    ConnectionState GetState(Guid serverId);

    Task ConnectAsync(
        ServerProfile profile,
        ServerSecret secret,
        HostKeyExpectation hostKeyExpectation,
        CancellationToken cancellationToken = default);

    Task<ConnectionTestResult> TestConnectionAsync(
        ServerProfile profile,
        ServerSecret secret,
        HostKeyExpectation hostKeyExpectation,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(Guid serverId, CancellationToken cancellationToken = default);

    Task<RemoteExecutionResult> ExecuteAsync(
        Guid serverId,
        RemoteScript script,
        CancellationToken cancellationToken = default);
}
