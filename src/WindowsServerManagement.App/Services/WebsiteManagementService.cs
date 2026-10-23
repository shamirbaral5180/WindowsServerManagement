using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;
using WindowsServerManagement.Core.Exceptions;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services.Validation;

namespace WindowsServerManagement.App.Services;

public sealed record WebsiteCreationProgress(string Message, int Percent);

public sealed class WebsiteManagementService
{
    private readonly IIisWebsiteService _iis;
    private readonly ServerCatalog _catalog;
    private readonly IActivityLogService _activity;
    private readonly IAppSettingsService _settings;
    private readonly ILogger<WebsiteManagementService> _logger;

    public WebsiteManagementService(
        IIisWebsiteService iis,
        ServerCatalog catalog,
        IActivityLogService activity,
        IAppSettingsService settings,
        ILogger<WebsiteManagementService> logger)
    {
        _iis = iis;
        _catalog = catalog;
        _activity = activity;
        _settings = settings;
        _logger = logger;
        _catalog.ServerConnected += OnServerConnected;
    }

    public event EventHandler<Guid>? WebsitesChanged;

    public async Task<IReadOnlyList<IisWebsite>> RefreshWebsitesAsync(
        ServerItemViewModel server,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        await _catalog.EnsureConnectedAsync(server, cancellationToken).ConfigureAwait(true);
        var websites = await _iis.GetWebsitesAsync(server.Id, server.Name, cancellationToken).ConfigureAwait(true);
        foreach (var website in websites)
        {
            website.ServerHost = server.Profile.Host;
        }

        UpdateStats(server, websites);
        return websites;
    }

    public async Task<WebsiteCreationResult> CreateWebsiteAsync(
        ServerItemViewModel server,
        WebsiteCreateRequest request,
        IProgress<WebsiteCreationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progress?.Report(new WebsiteCreationProgress("Validating website, folder, binding, and application pool...", 8));
            var validation = WebsiteValidator.ValidateCreateRequest(request);
            if (!validation.IsValid)
            {
                throw new ValidationException(validation.Errors);
            }

            progress?.Report(new WebsiteCreationProgress("Connecting to the selected server...", 18));
            await _catalog.EnsureConnectedAsync(server, cancellationToken).ConfigureAwait(true);

            progress?.Report(new WebsiteCreationProgress("Verifying Windows, PowerShell, IIS, and administrator rights...", 30));
            var capabilities = await _iis.GetCapabilitiesAsync(server.Id, forceRefresh: true, cancellationToken)
                .ConfigureAwait(true);
            if (!capabilities.IsManagementReady)
            {
                throw new RemoteCapabilityException(
                    "The server is not ready for IIS management. " + string.Join(" ", capabilities.Problems),
                    capabilities.Problems);
            }

            progress?.Report(new WebsiteCreationProgress("Creating folder, application pool, website, binding, and ACL...", 50));
            var result = await _iis.CreateWebsiteAsync(server.Id, request, cancellationToken).ConfigureAwait(true);

            progress?.Report(new WebsiteCreationProgress("Refreshing the IIS website list...", 88));
            try
            {
                var websites = await _iis.GetWebsitesAsync(server.Id, server.Name, cancellationToken).ConfigureAwait(true);
                UpdateStats(server, websites);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result.Warnings.Add("The website was created, but refreshing the site list failed: " + exception.Message);
            }

            stopwatch.Stop();
            await LogAsync(
                server,
                "Create website",
                ActivityStatus.Success,
                $"Created IIS website '{result.SiteName}' at '{result.FolderPath}'.",
                result.Binding,
                stopwatch.ElapsedMilliseconds,
                cancellationToken).ConfigureAwait(true);

            progress?.Report(new WebsiteCreationProgress("Website created successfully.", 100));
            WebsitesChanged?.Invoke(this, server.Id);
            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            await LogAsync(
                server,
                "Create website",
                ActivityStatus.Warning,
                "Website creation was cancelled. The remote server may contain partially created resources.",
                null,
                stopwatch.ElapsedMilliseconds,
                CancellationToken.None).ConfigureAwait(true);
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            await LogAsync(
                server,
                "Create website",
                ActivityStatus.Error,
                "Website creation failed.",
                exception.Message,
                stopwatch.ElapsedMilliseconds,
                CancellationToken.None).ConfigureAwait(true);
            throw;
        }
    }

    public Task<WebsiteOperationResult> StartWebsiteAsync(ServerItemViewModel server, string siteName, CancellationToken cancellationToken = default)
    {
        return RunWebsiteOperationAsync(
            server,
            siteName,
            "Start website",
            (id, ct) => _iis.StartWebsiteAsync(id, siteName, ct),
            cancellationToken);
    }

    public Task<WebsiteOperationResult> StopWebsiteAsync(ServerItemViewModel server, string siteName, CancellationToken cancellationToken = default)
    {
        return RunWebsiteOperationAsync(
            server,
            siteName,
            "Stop website",
            (id, ct) => _iis.StopWebsiteAsync(id, siteName, ct),
            cancellationToken);
    }

    public Task<WebsiteOperationResult> RestartWebsiteAsync(ServerItemViewModel server, string siteName, CancellationToken cancellationToken = default)
    {
        return RunWebsiteOperationAsync(
            server,
            siteName,
            "Restart website",
            (id, ct) => _iis.RestartWebsiteAsync(id, siteName, ct),
            cancellationToken);
    }

    public async Task<WebsiteDeleteResult> DeleteWebsiteAsync(
        ServerItemViewModel server,
        string siteName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _catalog.EnsureConnectedAsync(server, cancellationToken).ConfigureAwait(true);
            var result = await _iis.DeleteWebsiteAsync(server.Id, siteName, cancellationToken).ConfigureAwait(true);
            stopwatch.Stop();

            await LogAsync(
                server,
                "Delete website",
                ActivityStatus.Warning,
                $"Deleted IIS website '{siteName}'. Website files were not deleted.",
                null,
                stopwatch.ElapsedMilliseconds,
                cancellationToken).ConfigureAwait(true);

            await RefreshStatsSafeAsync(server, cancellationToken).ConfigureAwait(true);
            WebsitesChanged?.Invoke(this, server.Id);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            await LogAsync(
                server,
                "Delete website",
                ActivityStatus.Error,
                $"Failed to delete IIS website '{siteName}'.",
                exception.Message,
                stopwatch.ElapsedMilliseconds,
                CancellationToken.None).ConfigureAwait(true);
            throw;
        }
    }

    public async Task<FolderDeleteResult> DeleteFolderAsync(
        ServerItemViewModel server,
        string siteName,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _catalog.EnsureConnectedAsync(server, cancellationToken).ConfigureAwait(true);
            var result = await _iis.DeleteWebsiteFolderAsync(
                    server.Id,
                    siteName,
                    folderPath,
                    _settings.Current.DefaultIisRoot,
                    cancellationToken)
                .ConfigureAwait(true);

            stopwatch.Stop();
            await LogAsync(
                server,
                "Delete website folder",
                ActivityStatus.Warning,
                $"Permanently deleted website folder '{folderPath}'.",
                null,
                stopwatch.ElapsedMilliseconds,
                cancellationToken).ConfigureAwait(true);

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            await LogAsync(
                server,
                "Delete website folder",
                ActivityStatus.Error,
                $"Failed to delete website folder '{folderPath}'.",
                exception.Message,
                stopwatch.ElapsedMilliseconds,
                CancellationToken.None).ConfigureAwait(true);
            throw;
        }
    }

    public async Task<IReadOnlyList<IisCertificateInfo>> GetCertificatesAsync(
        ServerItemViewModel server,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        await _catalog.EnsureConnectedAsync(server, cancellationToken).ConfigureAwait(true);
        return await _iis.GetCertificatesAsync(server.Id, cancellationToken).ConfigureAwait(true);
    }

    public async Task<BindingsUpdateResult> UpdateBindingsAsync(
        ServerItemViewModel server,
        string siteName,
        IReadOnlyList<WebsiteBinding> bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var validation = WebsiteValidator.ValidateBindings(bindings);
            if (!validation.IsValid)
            {
                throw new ValidationException(validation.Errors);
            }

            await _catalog.EnsureConnectedAsync(server, cancellationToken).ConfigureAwait(true);
            var result = await _iis.UpdateBindingsAsync(server.Id, siteName, bindings, cancellationToken)
                .ConfigureAwait(true);

            stopwatch.Stop();
            await LogAsync(
                server,
                "Edit website bindings",
                ActivityStatus.Success,
                $"Updated bindings for IIS website '{siteName}'.",
                string.Join(", ", result.Bindings.Select(binding => binding.Display)),
                stopwatch.ElapsedMilliseconds,
                cancellationToken).ConfigureAwait(true);

            WebsitesChanged?.Invoke(this, server.Id);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            await LogAsync(
                server,
                "Edit website bindings",
                ActivityStatus.Error,
                $"Failed to update bindings for IIS website '{siteName}'.",
                exception.Message,
                stopwatch.ElapsedMilliseconds,
                CancellationToken.None).ConfigureAwait(true);
            throw;
        }
    }

    private async Task<WebsiteOperationResult> RunWebsiteOperationAsync(
        ServerItemViewModel server,
        string siteName,
        string operation,
        Func<Guid, CancellationToken, Task<WebsiteOperationResult>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _catalog.EnsureConnectedAsync(server, cancellationToken).ConfigureAwait(true);
            var result = await action(server.Id, cancellationToken).ConfigureAwait(true);
            stopwatch.Stop();

            await LogAsync(
                server,
                operation,
                ActivityStatus.Success,
                $"{operation} completed for '{siteName}'. State: {result.State}.",
                null,
                stopwatch.ElapsedMilliseconds,
                cancellationToken).ConfigureAwait(true);

            await RefreshStatsSafeAsync(server, cancellationToken).ConfigureAwait(true);
            WebsitesChanged?.Invoke(this, server.Id);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            await LogAsync(
                server,
                operation,
                ActivityStatus.Error,
                $"{operation} failed for '{siteName}'.",
                exception.Message,
                stopwatch.ElapsedMilliseconds,
                CancellationToken.None).ConfigureAwait(true);
            throw;
        }
    }

    private async Task RefreshStatsSafeAsync(ServerItemViewModel server, CancellationToken cancellationToken)
    {
        try
        {
            var websites = await _iis.GetWebsitesAsync(server.Id, server.Name, cancellationToken).ConfigureAwait(true);
            UpdateStats(server, websites);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Could not refresh website statistics for profile {ProfileId}.", server.Id);
        }
    }

    private void UpdateStats(ServerItemViewModel server, IReadOnlyList<IisWebsite> websites)
    {
        var running = websites.Count(website => website.IsRunning);
        server.SetWebsiteStats(websites.Count, running, websites.Count - running);
    }

    private void OnServerConnected(object? sender, ServerItemViewModel server)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300).ConfigureAwait(false);
                var websites = await _iis.GetWebsitesAsync(server.Id, server.Name, CancellationToken.None).ConfigureAwait(false);
                var running = websites.Count(website => website.IsRunning);
                await RunOnUiAsync(() => server.SetWebsiteStats(websites.Count, running, websites.Count - running))
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Background website statistics refresh failed for profile {ProfileId}.", server.Id);
            }
        });
    }

    private async Task LogAsync(
        ServerItemViewModel server,
        string operation,
        ActivityStatus status,
        string message,
        string? details,
        long durationMs,
        CancellationToken cancellationToken)
    {
        await _activity.LogAsync(new ActivityLogEntry
        {
            ServerId = server.Id,
            ServerName = server.Name,
            Operation = operation,
            Status = status,
            Message = message,
            Details = details,
            DurationMs = durationMs
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }
}
