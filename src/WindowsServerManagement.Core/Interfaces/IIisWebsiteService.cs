using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.Core.Interfaces;

public interface IIisWebsiteService
{
    Task<CapabilityReport> GetCapabilitiesAsync(Guid serverId, bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IisWebsite>> GetWebsitesAsync(Guid serverId, string serverName, CancellationToken cancellationToken = default);
    Task<WebsiteCreationResult> CreateWebsiteAsync(Guid serverId, WebsiteCreateRequest request, CancellationToken cancellationToken = default);
    Task<WebsiteOperationResult> StartWebsiteAsync(Guid serverId, string siteName, CancellationToken cancellationToken = default);
    Task<WebsiteOperationResult> StopWebsiteAsync(Guid serverId, string siteName, CancellationToken cancellationToken = default);
    Task<WebsiteOperationResult> RestartWebsiteAsync(Guid serverId, string siteName, CancellationToken cancellationToken = default);
    Task<WebsiteDeleteResult> DeleteWebsiteAsync(Guid serverId, string siteName, CancellationToken cancellationToken = default);
    Task<FolderDeleteResult> DeleteWebsiteFolderAsync(Guid serverId, string siteName, string folderPath, string allowedRoot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IisCertificateInfo>> GetCertificatesAsync(Guid serverId, CancellationToken cancellationToken = default);
    Task<BindingsUpdateResult> UpdateBindingsAsync(Guid serverId, string siteName, IReadOnlyList<WebsiteBinding> bindings, CancellationToken cancellationToken = default);
}
