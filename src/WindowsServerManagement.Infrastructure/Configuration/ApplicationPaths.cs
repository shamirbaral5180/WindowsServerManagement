using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsServerManagement.Infrastructure.Configuration;

public sealed class ApplicationPaths
{
    public ApplicationPaths()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsServerManagement"))
    {
    }

    public ApplicationPaths(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(RootDirectory);

        ProfilesFile = Path.Combine(RootDirectory, "servers.json");
        SettingsFile = Path.Combine(RootDirectory, "settings.json");
        SecretsFile = Path.Combine(RootDirectory, "secrets.dat");
        ActivityFile = Path.Combine(RootDirectory, "activity.jsonl");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        Directory.CreateDirectory(LogsDirectory);
    }

    public string RootDirectory { get; }
    public string ProfilesFile { get; }
    public string SettingsFile { get; }
    public string SecretsFile { get; }
    public string ActivityFile { get; }
    public string LogsDirectory { get; }
}

internal static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

internal static class JsonFileStore
{
    public static async Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
