using System.Text.Json;
using Microsoft.Extensions.Logging;
using WindowsServerManagement.Infrastructure.Configuration;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.Infrastructure.Storage;

public sealed class JsonActivityLogRepository : IActivityLogRepository
{
    private const long MaximumFileBytes = 10 * 1024 * 1024;
    private readonly ApplicationPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions LineOptions = new(JsonDefaults.Options) { WriteIndented = false };
    private readonly ILogger<JsonActivityLogRepository> _logger;

    public JsonActivityLogRepository(ApplicationPaths paths, ILogger<JsonActivityLogRepository> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task AppendAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RotateIfNeeded();
            var line = JsonSerializer.Serialize(entry, LineOptions);
            await File.AppendAllTextAsync(_paths.ActivityFile, line + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ActivityLogEntry>> LoadRecentAsync(int maximumCount, CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0)
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_paths.ActivityFile))
            {
                return [];
            }

            var entries = new Queue<ActivityLogEntry>(maximumCount);
            await foreach (var line in File.ReadLinesAsync(_paths.ActivityFile, cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<ActivityLogEntry>(line, LineOptions);
                    if (entry is null)
                    {
                        continue;
                    }

                    entries.Enqueue(entry);
                    while (entries.Count > maximumCount)
                    {
                        entries.Dequeue();
                    }
                }
                catch (JsonException)
                {
                    _logger.LogDebug("Skipped an invalid activity log line.");
                }
            }

            return entries.Reverse().ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_paths.ActivityFile) || new FileInfo(_paths.ActivityFile).Length < MaximumFileBytes)
            {
                return;
            }

            var archive = Path.Combine(
                _paths.RootDirectory,
                $"activity-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            File.Move(_paths.ActivityFile, archive);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Activity log rotation failed. The existing log will continue to be appended.");
        }
    }
}

public sealed class ActivityLogService : IActivityLogService
{
    private readonly IActivityLogRepository _repository;
    private readonly ILogger<ActivityLogService> _logger;

    public ActivityLogService(IActivityLogRepository repository, ILogger<ActivityLogService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public event EventHandler<ActivityLogEntry>? EntryAdded;

    public async Task LogAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            await _repository.AppendAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Could not persist the activity log entry.");
        }

        EntryAdded?.Invoke(this, entry);
    }

    public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken = default)
    {
        return _repository.LoadRecentAsync(maximumCount, cancellationToken);
    }
}
