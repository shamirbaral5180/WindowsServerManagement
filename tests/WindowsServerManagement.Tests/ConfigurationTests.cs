using Microsoft.Extensions.Logging.Abstractions;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Infrastructure.Configuration;
using WindowsServerManagement.Infrastructure.Security;
using WindowsServerManagement.Infrastructure.Storage;

namespace WindowsServerManagement.Tests;

public sealed class ConfigurationTests : IDisposable
{
    private readonly string _root;

    public ConfigurationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wsm-tests-" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task ProfileRepository_RoundTripsProfiles()
    {
        var paths = new ApplicationPaths(_root);
        var repository = new JsonServerProfileRepository(paths, NullLogger<JsonServerProfileRepository>.Instance);
        var profile = new ServerProfile
        {
            Name = "Production",
            Host = "prod.example.com",
            Username = "admin",
            Port = 2222
        };

        await repository.SaveAsync(profile);
        var loaded = await repository.LoadAsync();

        Assert.Single(loaded);
        Assert.Equal("Production", loaded[0].Name);
        Assert.Equal(2222, loaded[0].Port);

        profile.Name = "Production Renamed";
        await repository.SaveAsync(profile);
        loaded = await repository.LoadAsync();
        Assert.Single(loaded);
        Assert.Equal("Production Renamed", loaded[0].Name);

        await repository.DeleteAsync(profile.Id);
        loaded = await repository.LoadAsync();
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task SettingsService_AppliesDefaultsAndNormalizesValues()
    {
        var paths = new ApplicationPaths(_root);
        var service = new AppSettingsService(paths, NullLogger<AppSettingsService>.Instance);

        var loaded = await service.LoadAsync();

        Assert.Equal(ThemeMode.Light, loaded.Theme);
        Assert.Equal(@"C:\inetpub", loaded.DefaultIisRoot);

        loaded.ConnectionTimeoutSeconds = 1;
        loaded.CommandTimeoutSeconds = 5000;
        loaded.DefaultWebsitePort = 0;
        await service.SaveAsync(loaded);

        var reloaded = await service.LoadAsync();
        Assert.Equal(5, reloaded.ConnectionTimeoutSeconds);
        Assert.Equal(900, reloaded.CommandTimeoutSeconds);
        Assert.Equal(1, reloaded.DefaultWebsitePort);
    }

    [Fact]
    public async Task SecretStore_EncryptsSecretAtRest()
    {
        var paths = new ApplicationPaths(_root);
        var store = new DpapiSecretStore(paths, NullLogger<DpapiSecretStore>.Instance);
        var profileId = Guid.NewGuid();

        await store.SaveAsync(profileId, new ServerSecret { Password = "super-secret-value" });
        var raw = await File.ReadAllTextAsync(paths.SecretsFile);
        Assert.DoesNotContain("super-secret-value", raw, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(raw));

        var secret = await store.GetAsync(profileId);
        Assert.NotNull(secret);
        Assert.Equal("super-secret-value", secret!.Password);

        await store.DeleteAsync(profileId);
        Assert.Null(await store.GetAsync(profileId));
    }

    [Fact]
    public async Task ActivityRepository_ReturnsNewestFirstAndLimitsCount()
    {
        var paths = new ApplicationPaths(_root);
        var repository = new JsonActivityLogRepository(paths, NullLogger<JsonActivityLogRepository>.Instance);

        for (var index = 0; index < 5; index++)
        {
            await repository.AppendAsync(new ActivityLogEntry
            {
                Timestamp = DateTimeOffset.Now.AddSeconds(index),
                ServerName = "Server",
                Operation = "Op " + index,
                Status = ActivityStatus.Info,
                Message = "Message " + index
            });
        }

        var recent = await repository.LoadRecentAsync(3);

        Assert.Equal(3, recent.Count);
        Assert.Equal("Op 4", recent[0].Operation);
        Assert.Equal("Op 3", recent[1].Operation);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }
}
