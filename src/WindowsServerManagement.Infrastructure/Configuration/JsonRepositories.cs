using System.Text.Json;
using Microsoft.Extensions.Logging;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.Infrastructure.Configuration;

public sealed class JsonServerProfileRepository : IServerProfileRepository
{
    private readonly ApplicationPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<JsonServerProfileRepository> _logger;

    public JsonServerProfileRepository(ApplicationPaths paths, ILogger<JsonServerProfileRepository> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ServerProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var content = await JsonFileStore.ReadAsync(_paths.ProfilesFile, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                return [];
            }

            var profiles = JsonSerializer.Deserialize<List<ServerProfile>>(content, JsonDefaults.Options) ?? [];
            return profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "The server profile file is invalid.");
            throw new InvalidOperationException(
                $"The server profile file '{_paths.ProfilesFile}' is invalid. Repair or remove it before starting the application.",
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ServerProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var index = profiles.FindIndex(item => item.Id == profile.Id);
            if (index >= 0)
            {
                profiles[index] = profile;
            }
            else
            {
                profiles.Add(profile);
            }

            var json = JsonSerializer.Serialize(profiles, JsonDefaults.Options);
            await JsonFileStore.WriteAtomicAsync(_paths.ProfilesFile, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profiles = await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            profiles.RemoveAll(profile => profile.Id == profileId);
            var json = JsonSerializer.Serialize(profiles, JsonDefaults.Options);
            await JsonFileStore.WriteAtomicAsync(_paths.ProfilesFile, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ServerProfile>> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        var content = await JsonFileStore.ReadAsync(_paths.ProfilesFile, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<ServerProfile>>(content, JsonDefaults.Options) ?? [];
    }
}

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly ApplicationPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<AppSettingsService> _logger;
    private AppSettings _current = new();

    public AppSettingsService(ApplicationPaths paths, ILogger<AppSettingsService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public AppSettings Current => _current;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var content = await JsonFileStore.ReadAsync(_paths.SettingsFile, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                _current = new AppSettings();
                _current.Normalize();
                return _current.Clone();
            }

            try
            {
                _current = JsonSerializer.Deserialize<AppSettings>(content, JsonDefaults.Options) ?? new AppSettings();
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Settings file is invalid. Default settings will be used.");
                _current = new AppSettings();
            }

            _current.Normalize();
            return _current.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            settings.Normalize();
            _current = settings.Clone();
            var json = JsonSerializer.Serialize(_current, JsonDefaults.Options);
            await JsonFileStore.WriteAtomicAsync(_paths.SettingsFile, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
