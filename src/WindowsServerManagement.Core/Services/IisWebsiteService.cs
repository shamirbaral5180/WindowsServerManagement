using System.Collections.Concurrent;
using WindowsServerManagement.Core.Exceptions;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services.Remote;
using WindowsServerManagement.Core.Services.Validation;

namespace WindowsServerManagement.Core.Services;

public sealed class IisWebsiteService : IIisWebsiteService
{
    private readonly IServerSessionManager _sessions;
    private readonly ConcurrentDictionary<Guid, CapabilityCacheEntry> _capabilities = new();

    public IisWebsiteService(IServerSessionManager sessions)
    {
        _sessions = sessions;
        _sessions.StatusChanged += OnSessionStatusChanged;
    }

    public async Task<CapabilityReport> GetCapabilitiesAsync(Guid serverId, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _capabilities.TryGetValue(serverId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Report;
        }

        var report = await ExecuteForDataAsync<CapabilityReport>(serverId, PowerShellScripts.Capabilities(), cancellationToken)
            .ConfigureAwait(false);
        _capabilities[serverId] = new CapabilityCacheEntry(report, DateTimeOffset.UtcNow.AddMinutes(2));
        return report;
    }

    public async Task<IReadOnlyList<IisWebsite>> GetWebsitesAsync(Guid serverId, string serverName, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteForDataAsync<WebsiteListResponse>(serverId, PowerShellScripts.ListWebsites(), cancellationToken)
            .ConfigureAwait(false);

        foreach (var website in response.Sites)
        {
            website.ServerId = serverId;
            website.ServerName = serverName;
            website.Bindings ??= [];
        }

        return response.Sites;
    }

    public async Task<WebsiteCreationResult> CreateWebsiteAsync(Guid serverId, WebsiteCreateRequest request, CancellationToken cancellationToken = default)
    {
        var validation = WebsiteValidator.ValidateCreateRequest(request);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        await EnsureManagementReadyAsync(serverId, cancellationToken).ConfigureAwait(false);

        var response = await ExecuteForDataAsync<CreateWebsiteResponse>(
                serverId,
                PowerShellScripts.CreateWebsite(request),
                cancellationToken)
            .ConfigureAwait(false);

        return new WebsiteCreationResult
        {
            Success = true,
            SiteName = response.SiteName,
            FolderPath = response.FolderPath,
            ApplicationPool = response.ApplicationPool,
            Binding = IisBindingBuilder.BuildDisplay(request.Protocol, request.Port, request.Hostname),
            SiteCreated = true,
            FolderCreated = response.FolderCreated,
            AppPoolCreated = response.AppPoolCreated,
            State = response.SiteState,
            Warnings = response.Warnings ?? []
        };
    }

    public Task<WebsiteOperationResult> StartWebsiteAsync(Guid serverId, string siteName, CancellationToken cancellationToken = default)
    {
        ValidateSiteName(siteName);
        return RunWebsiteOperationAsync(serverId, PowerShellScripts.StartWebsite(siteName), cancellationToken);
    }

    public Task<WebsiteOperationResult> StopWebsiteAsync(Guid serverId, string siteName, CancellationToken cancellationToken = default)
    {
        ValidateSiteName(siteName);
        return RunWebsiteOperationAsync(serverId, PowerShellScripts.StopWebsite(siteName), cancellationToken);
    }

    public Task<WebsiteOperationResult> RestartWebsiteAsync(Guid serverId, string siteName, CancellationToken cancellationToken = default)
    {
        ValidateSiteName(siteName);
        return RunWebsiteOperationAsync(serverId, PowerShellScripts.RestartWebsite(siteName), cancellationToken);
    }

    public async Task<WebsiteDeleteResult> DeleteWebsiteAsync(Guid serverId, string siteName, CancellationToken cancellationToken = default)
    {
        ValidateSiteName(siteName);
        await EnsureManagementReadyAsync(serverId, cancellationToken).ConfigureAwait(false);
        var response = await ExecuteForDataAsync<SiteOperationResponse>(
                serverId,
                PowerShellScripts.DeleteWebsite(siteName),
                cancellationToken)
            .ConfigureAwait(false);

        return new WebsiteDeleteResult { Success = true, SiteName = response.SiteName };
    }

    public async Task<FolderDeleteResult> DeleteWebsiteFolderAsync(
        Guid serverId,
        string siteName,
        string folderPath,
        string allowedRoot,
        CancellationToken cancellationToken = default)
    {
        ValidateSiteName(siteName);
        if (!WebsitePathSafety.IsValidRoot(allowedRoot, out var rootError))
        {
            throw new ValidationException(rootError);
        }

        if (!WebsitePathSafety.IsUnderRoot(folderPath, allowedRoot))
        {
            throw new ValidationException("The folder is outside the configured IIS root. This tool will not delete it.");
        }

        await EnsureManagementReadyAsync(serverId, cancellationToken).ConfigureAwait(false);
        var response = await ExecuteForDataAsync<FolderDeleteResponse>(
                serverId,
                PowerShellScripts.DeleteWebsiteFolder(siteName, folderPath, allowedRoot),
                cancellationToken)
            .ConfigureAwait(false);

        return new FolderDeleteResult { Success = true, FolderPath = response.FolderPath };
    }

    public async Task<IReadOnlyList<IisCertificateInfo>> GetCertificatesAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        var response = await ExecuteForDataAsync<CertificateListResponse>(
                serverId,
                PowerShellScripts.GetCertificates(),
                cancellationToken)
            .ConfigureAwait(false);

        return response.Certificates;
    }

    public async Task<BindingsUpdateResult> UpdateBindingsAsync(
        Guid serverId,
        string siteName,
        IReadOnlyList<WebsiteBinding> bindings,
        CancellationToken cancellationToken = default)
    {
        ValidateSiteName(siteName);
        var validation = WebsiteValidator.ValidateBindings(bindings);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        await EnsureManagementReadyAsync(serverId, cancellationToken).ConfigureAwait(false);
        var response = await ExecuteForDataAsync<BindingsUpdateResponse>(
                serverId,
                PowerShellScripts.UpdateBindings(siteName, bindings),
                cancellationToken)
            .ConfigureAwait(false);

        return new BindingsUpdateResult
        {
            Success = true,
            SiteName = response.SiteName,
            Bindings = response.Bindings ?? [],
            Warnings = []
        };
    }

    private async Task<WebsiteOperationResult> RunWebsiteOperationAsync(Guid serverId, RemoteScript script, CancellationToken cancellationToken)
    {
        await EnsureManagementReadyAsync(serverId, cancellationToken).ConfigureAwait(false);
        var response = await ExecuteForDataAsync<SiteOperationResponse>(serverId, script, cancellationToken)
            .ConfigureAwait(false);

        return new WebsiteOperationResult
        {
            Success = true,
            SiteName = response.SiteName,
            State = response.SiteState
        };
    }

    private async Task EnsureManagementReadyAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var report = await GetCapabilitiesAsync(serverId, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        if (!report.IsManagementReady)
        {
            var problems = report.Problems;
            var message = "The remote server is not ready for IIS management.";
            if (problems.Count > 0)
            {
                message += Environment.NewLine + string.Join(Environment.NewLine, problems.Select(problem => "- " + problem));
            }

            throw new RemoteCapabilityException(message, problems);
        }
    }

    private async Task<T> ExecuteForDataAsync<T>(Guid serverId, RemoteScript script, CancellationToken cancellationToken)
    {
        var result = await _sessions.ExecuteAsync(serverId, script, cancellationToken).ConfigureAwait(false);
        return RemoteResultParser.ParseData<T>(result);
    }

    private void OnSessionStatusChanged(object? sender, ServerConnectionStatus status)
    {
        if (status.State != ConnectionState.Connected)
        {
            _capabilities.TryRemove(status.ServerId, out _);
        }
    }

    private static void ValidateSiteName(string siteName)
    {
        var result = new ValidationResult();
        WebsiteValidator.ValidateSiteName(siteName, result);
        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }

    private sealed record CapabilityCacheEntry(CapabilityReport Report, DateTimeOffset ExpiresAt);

    private sealed class WebsiteListResponse
    {
        public List<IisWebsite> Sites { get; set; } = [];
    }

    private sealed class CreateWebsiteResponse
    {
        public string SiteName { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string ApplicationPool { get; set; } = string.Empty;
        public string SiteState { get; set; } = string.Empty;
        public bool FolderCreated { get; set; }
        public bool AppPoolCreated { get; set; }
        public List<string>? Warnings { get; set; }
    }

    private sealed class SiteOperationResponse
    {
        public string SiteName { get; set; } = string.Empty;
        public string SiteState { get; set; } = string.Empty;
    }

    private sealed class FolderDeleteResponse
    {
        public string FolderPath { get; set; } = string.Empty;
        public bool FolderExisted { get; set; }
    }

    private sealed class CertificateListResponse
    {
        public List<IisCertificateInfo> Certificates { get; set; } = [];
    }

    private sealed class BindingsUpdateResponse
    {
        public string SiteName { get; set; } = string.Empty;
        public List<WebsiteBinding>? Bindings { get; set; }
    }
}
