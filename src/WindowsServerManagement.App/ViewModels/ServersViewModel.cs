using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WindowsServerManagement.App.Services;
using WindowsServerManagement.Core.Exceptions;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.App.ViewModels;

public sealed partial class ServersViewModel : ObservableObject
{
    private readonly ServerCatalog _catalog;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ServersViewModel> _logger;
    private readonly Dictionary<Guid, CancellationTokenSource> _operations = [];

    public ServersViewModel(ServerCatalog catalog, IDialogService dialogs, ILogger<ServersViewModel> logger)
    {
        _catalog = catalog;
        _dialogs = dialogs;
        _logger = logger;
        _catalog.Servers.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasServers));
            OnPropertyChanged(nameof(HasNoServers));
        };
        _catalog.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ServerCatalog.SelectedServer))
            {
                OnPropertyChanged(nameof(HasSelectedServer));
            }
        };
    }

    public ServerCatalog Catalog => _catalog;
    public System.Collections.ObjectModel.ObservableCollection<ServerItemViewModel> Servers => _catalog.Servers;

    public bool HasServers => Servers.Count > 0;
    public bool HasNoServers => Servers.Count == 0;
    public bool HasSelectedServer => SelectedServer is not null;

    public ServerItemViewModel? SelectedServer
    {
        get => _catalog.SelectedServer;
        set => _catalog.SelectedServer = value;
    }

    [RelayCommand]
    private async Task AddServerAsync()
    {
        try
        {
            var result = await _dialogs.ShowServerEditorAsync(null).ConfigureAwait(true);
            if (result is not null)
            {
                var item = _catalog.Servers.FirstOrDefault(server => server.Id == result.Id);
                if (item is not null)
                {
                    _catalog.SelectedServer = item;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not open the add-server dialog.");
            await ShowErrorAsync("Add server", exception).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task EditServerAsync(ServerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await _dialogs.ShowServerEditorAsync(item.Profile.Clone()).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not open the edit-server dialog.");
            await ShowErrorAsync("Edit server", exception).ConfigureAwait(true);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ConnectAsync(ServerItemViewModel? item)
    {
        if (item is null || item.IsBusy || _operations.ContainsKey(item.Id))
        {
            return;
        }

        var source = new CancellationTokenSource();
        _operations[item.Id] = source;
        try
        {
            await _catalog.ConnectAsync(item, source.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Connect", exception).ConfigureAwait(true);
        }
        finally
        {
            CompleteOperation(item.Id, source);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DisconnectAsync(ServerItemViewModel? item)
    {
        if (item is null || _operations.ContainsKey(item.Id))
        {
            return;
        }

        var source = new CancellationTokenSource();
        _operations[item.Id] = source;
        try
        {
            await _catalog.DisconnectAsync(item, source.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Disconnect", exception).ConfigureAwait(true);
        }
        finally
        {
            CompleteOperation(item.Id, source);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ReconnectAsync(ServerItemViewModel? item)
    {
        if (item is null || _operations.ContainsKey(item.Id))
        {
            return;
        }

        var source = new CancellationTokenSource();
        _operations[item.Id] = source;
        try
        {
            await _catalog.ReconnectAsync(item, source.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Reconnect", exception).ConfigureAwait(true);
        }
        finally
        {
            CompleteOperation(item.Id, source);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task TestConnectionAsync(ServerItemViewModel? item)
    {
        if (item is null || item.IsBusy || _operations.ContainsKey(item.Id))
        {
            return;
        }

        await RunItemOperationAsync(item, async cancellationToken =>
        {
            if (item.IsConnected)
            {
                var capabilities = await _catalog.RefreshCapabilitiesAsync(item, cancellationToken).ConfigureAwait(true);
                if (capabilities is not null)
                {
                    await _dialogs.ShowDiagnosticsAsync(item, capabilities).ConfigureAwait(true);
                }

                return;
            }

            var draft = item.Profile.Clone();
            var result = await _catalog.TestConnectionAsync(draft, suppliedSecret: null, clearStoredSecret: false, cancellationToken)
                .ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(draft.TrustedHostKeyFingerprint) &&
                !string.Equals(draft.TrustedHostKeyFingerprint, item.Profile.TrustedHostKeyFingerprint, StringComparison.Ordinal))
            {
                item.Profile.TrustedHostKeyFingerprint = draft.TrustedHostKeyFingerprint;
                await _catalog.SaveProfileAsync(item.Profile, suppliedSecret: null, clearStoredSecret: false, cancellationToken)
                    .ConfigureAwait(true);
            }

            if (result.Capabilities is not null)
            {
                item.Capabilities = result.Capabilities;
                await _dialogs.ShowDiagnosticsAsync(item, result.Capabilities).ConfigureAwait(true);
            }
            else
            {
                await _dialogs.ShowMessageAsync(
                        result.Success ? "Connection test" : "Connection test failed",
                        result.Message,
                        isError: !result.Success)
                    .ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DiagnoseAsync(ServerItemViewModel? item)
    {
        if (item is null || item.IsBusy || _operations.ContainsKey(item.Id))
        {
            return;
        }

        await RunItemOperationAsync(item, async cancellationToken =>
        {
            CapabilityReport? capabilities;
            if (item.IsConnected)
            {
                capabilities = await _catalog.RefreshCapabilitiesAsync(item, cancellationToken).ConfigureAwait(true);
            }
            else
            {
                var draft = item.Profile.Clone();
                var result = await _catalog.TestConnectionAsync(
                        draft,
                        suppliedSecret: null,
                        clearStoredSecret: false,
                        cancellationToken)
                    .ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(draft.TrustedHostKeyFingerprint) &&
                    !string.Equals(draft.TrustedHostKeyFingerprint, item.Profile.TrustedHostKeyFingerprint, StringComparison.Ordinal))
                {
                    item.Profile.TrustedHostKeyFingerprint = draft.TrustedHostKeyFingerprint;
                    await _catalog.SaveProfileAsync(item.Profile, suppliedSecret: null, clearStoredSecret: false, cancellationToken)
                        .ConfigureAwait(true);
                }

                capabilities = result.Capabilities;
                if (capabilities is null)
                {
                    await _dialogs.ShowMessageAsync("Connection diagnostics", result.Message, isError: !result.Success)
                        .ConfigureAwait(true);
                    return;
                }
            }

            if (capabilities is not null)
            {
                item.Capabilities = capabilities;
                await _dialogs.ShowDiagnosticsAsync(item, capabilities).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ForgetHostKeyAsync(ServerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
                "Forget trusted SSH host key?",
                $"Forget the trusted host key for '{item.Name}'? The next connection will require you to review and trust the server fingerprint again. Current connections will be closed.",
                "Forget host key",
                "Cancel",
                dangerous: true)
            .ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await RunItemOperationAsync(item, async cancellationToken =>
        {
            await _catalog.ForgetHostKeyAsync(item, cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteServerAsync(ServerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
                "Delete server profile?",
                $"Delete the saved profile '{item.Name}' ({item.Endpoint})? The SSH credential will also be removed from this Windows account. Remote servers and IIS websites are not changed.",
                "Delete profile",
                "Cancel",
                dangerous: true)
            .ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await RunItemOperationAsync(item, async cancellationToken =>
        {
            await _catalog.DeleteProfileAsync(item, cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelOperation(ServerItemViewModel? item)
    {
        if (item is not null && _operations.TryGetValue(item.Id, out var source))
        {
            source.Cancel();
        }
    }

    private async Task RunItemOperationAsync(ServerItemViewModel item, Func<CancellationToken, Task> operation)
    {
        if (_operations.ContainsKey(item.Id))
        {
            return;
        }

        var source = new CancellationTokenSource();
        _operations[item.Id] = source;
        item.IsBusy = true;
        try
        {
            await operation(source.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await ShowErrorAsync("Server operation", exception).ConfigureAwait(true);
        }
        finally
        {
            item.IsBusy = false;
            CompleteOperation(item.Id, source);
        }
    }

    private void CompleteOperation(Guid profileId, CancellationTokenSource source)
    {
        _operations.Remove(profileId);
        source.Dispose();
    }

    private async Task ShowErrorAsync(string title, Exception exception)
    {
        var message = exception is AppException
            ? exception.Message
            : "An unexpected error occurred. " + exception.Message;
        _logger.LogError(exception, "{Title} failed.", title);
        await _dialogs.ShowMessageAsync(title, message, isError: true).ConfigureAwait(true);
    }
}
