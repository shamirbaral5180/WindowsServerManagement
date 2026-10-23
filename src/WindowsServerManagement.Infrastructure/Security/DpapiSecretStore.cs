using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WindowsServerManagement.Infrastructure.Configuration;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.Infrastructure.Security;

public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WindowsServerManagement.Secrets.v1");
    private readonly ApplicationPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<DpapiSecretStore> _logger;

    public DpapiSecretStore(ApplicationPaths paths, ILogger<DpapiSecretStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<ServerSecret?> GetAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var values = await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            if (!values.TryGetValue(profileId.ToString("N"), out var encrypted))
            {
                return null;
            }

            try
            {
                var protectedBytes = Convert.FromBase64String(encrypted);
                var plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(plaintext);
                return JsonSerializer.Deserialize<ServerSecret>(json, JsonDefaults.Options);
            }
            catch (CryptographicException exception)
            {
                _logger.LogError(exception, "A stored credential could not be decrypted for profile {ProfileId}.", profileId);
                throw new InvalidOperationException(
                    "Stored credentials could not be decrypted. They are protected for the Windows user that saved them. Re-enter the credential for this profile.",
                    exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(Guid profileId, ServerSecret secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var values = await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var key = profileId.ToString("N");
            if (secret.IsEmpty)
            {
                values.Remove(key);
            }
            else
            {
                var json = JsonSerializer.Serialize(secret, JsonDefaults.Options);
                var plaintext = Encoding.UTF8.GetBytes(json);
                var protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
                values[key] = Convert.ToBase64String(protectedBytes);
            }

            var content = JsonSerializer.Serialize(values, JsonDefaults.Options);
            await JsonFileStore.WriteAtomicAsync(_paths.SecretsFile, content, cancellationToken).ConfigureAwait(false);
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
            var values = await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            if (values.Remove(profileId.ToString("N")))
            {
                var content = JsonSerializer.Serialize(values, JsonDefaults.Options);
                await JsonFileStore.WriteAtomicAsync(_paths.SecretsFile, content, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, string>> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        var content = await JsonFileStore.ReadAsync(_paths.SecretsFile, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(content, JsonDefaults.Options)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "The encrypted secret store is invalid.");
            throw new InvalidOperationException(
                $"The encrypted credential store '{_paths.SecretsFile}' is invalid. Re-enter saved credentials for your server profiles.",
                exception);
        }
    }
}
