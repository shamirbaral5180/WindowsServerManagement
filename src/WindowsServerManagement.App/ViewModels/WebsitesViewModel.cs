using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WindowsServerManagement.App.Services;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services;

namespace WindowsServerManagement.App.ViewModels;

public sealed partial class WebsitesViewModel : ObservableObject
{
    private readonly ServerCatalog _catalog;
    private readonly WebsiteManagementService _websitesService;
    private readonly IDialogService _dialogs;
    private readonly IAppSettingsService _settings;
    private readonly IExternalLauncher _launcher;
    private readonly ILogger<WebsitesViewModel> _logger;
    private readonly Dictionary<ServerItemViewModel, PropertyChangedEventHandler> _serverHandlers = [];
    private CancellationTokenSource? _operationSource;
    private Guid? _loadedServerId;
    private bool _initialized;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private IisWebsite? _selectedWebsite;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Select a connected server to load its IIS websites.";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _successMessage;

    [ObservableProperty]
    private string _filteredCountText = "0 websites";

    public WebsitesViewModel(
        ServerCatalog catalog,
        WebsiteManagementService websitesService,
        IDialogService dialogs,
        IAppSettingsService settings,
        IExternalLauncher launcher,
        ILogger<WebsitesViewModel> logger)
    {
        _catalog = catalog;
        _websitesService = websitesService;
        _dialogs = dialogs;
        _settings = settings;
        _launcher = launcher;
        _logger = logger;

        WebsitesView = CollectionViewSource.GetDefaultView(Websites);
        WebsitesView.Filter = FilterWebsite;

        _catalog.PropertyChanged += OnCatalogPropertyChanged;
        _catalog.Servers.CollectionChanged += OnServersChanged;
        foreach (var server in _catalog.Servers)
        {
            Attach(server);
        }
    }

    public ObservableCollection<ServerItemViewModel> Servers => _catalog.Servers;
    public ObservableCollection<IisWebsite> Websites { get; } = [];
    public ICollectionView WebsitesView { get; }

    public ServerItemViewModel? SelectedServer
    {
        get => _catalog.SelectedServer;
        set => _catalog.SelectedServer = value;
    }

    public bool IsServerConnected => SelectedServer?.IsConnected == true;
    public bool HasSelectedWebsite => SelectedWebsite is not null;
    public bool HasWebsites => Websites.Count > 0;
    public bool HasNoWebsites => Websites.Count == 0;
    public bool HasVisibleWebsites => WebsitesView.Cast<IisWebsite>().Any();
    public string ServerName => SelectedServer?.Name ?? "No server selected";
    public string ServerEndpoint => SelectedServer?.Endpoint ?? string.Empty;
    public string EmptyMessage => SelectedServer is null
        ? "Select a server to manage IIS websites."
        : !SelectedServer.IsConnected
            ? "The selected server is disconnected. Connect it to load IIS websites."
            : HasWebsites
                ? "No websites match the current search."
                : "No IIS websites were found. Create one to get started.";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _initialized = true;
        if (SelectedServer is not null && SelectedServer.IsConnected)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    public async Task OnActivatedAsync()
    {
        if (!_initialized || SelectedServer is null || !SelectedServer.IsConnected)
        {
            RaiseStateProperties();
            return;
        }

        if (_loadedServerId != SelectedServer.Id && !IsBusy)
        {
            await RefreshWebsitesAsync(SelectedServer, showSuccess: false, CancellationToken.None).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var server = SelectedServer;
        if (server is null)
        {
            ErrorMessage = "Select a server first.";
            return;
        }

        await RefreshWebsitesAsync(server, showSuccess: true, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CreateWebsiteAsync()
    {
        var server = SelectedServer;
        if (server is null)
        {
            await _dialogs.ShowMessageAsync("Create website", "Select a server first.", isError: true)
                .ConfigureAwait(true);
            return;
        }

        if (!await EnsureConnectedAsync(server).ConfigureAwait(true))
        {
            return;
        }

        var created = await _dialogs.ShowCreateWebsiteAsync(server).ConfigureAwait(true);
        if (created)
        {
            await RefreshWebsitesAsync(server, showSuccess: false, CancellationToken.None).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task StartWebsiteAsync()
    {
        var server = SelectedServer;
        var website = SelectedWebsite;
        if (server is null || website is null)
        {
            return;
        }

        await RunOperationAsync(
                $"Starting '{website.Name}'...",
                async cancellationToken =>
                {
                    await _websitesService.StartWebsiteAsync(server, website.Name, cancellationToken).ConfigureAwait(true);
                    await ReloadAfterOperationAsync(server, cancellationToken).ConfigureAwait(true);
                    SuccessMessage = $"Website '{website.Name}' started.";
                })
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StopWebsiteAsync()
    {
        var server = SelectedServer;
        var website = SelectedWebsite;
        if (server is null || website is null)
        {
            return;
        }

        await RunOperationAsync(
                $"Stopping '{website.Name}'...",
                async cancellationToken =>
                {
                    await _websitesService.StopWebsiteAsync(server, website.Name, cancellationToken).ConfigureAwait(true);
                    await ReloadAfterOperationAsync(server, cancellationToken).ConfigureAwait(true);
                    SuccessMessage = $"Website '{website.Name}' stopped.";
                })
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestartWebsiteAsync()
    {
        var server = SelectedServer;
        var website = SelectedWebsite;
        if (server is null || website is null)
        {
            return;
        }

        await RunOperationAsync(
                $"Restarting '{website.Name}'...",
                async cancellationToken =>
                {
                    await _websitesService.RestartWebsiteAsync(server, website.Name, cancellationToken).ConfigureAwait(true);
                    await ReloadAfterOperationAsync(server, cancellationToken).ConfigureAwait(true);
                    SuccessMessage = $"Website '{website.Name}' restarted.";
                })
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteWebsiteAsync()
    {
        var server = SelectedServer;
        var website = SelectedWebsite;
        if (server is null || website is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
                "Delete IIS website?",
                $"Delete the IIS website '{website.Name}' from '{server.Name}'?\n\nThis removes the IIS site configuration. It does NOT delete the website files at:\n{website.PhysicalPath}\n\nThis action cannot be undone.",
                "Delete website",
                "Cancel",
                dangerous: true)
            .ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var deleted = false;
        await RunOperationAsync(
                $"Deleting '{website.Name}'...",
                async cancellationToken =>
                {
                    await _websitesService.DeleteWebsiteAsync(server, website.Name, cancellationToken).ConfigureAwait(true);
                    deleted = true;
                    await ReloadAfterOperationAsync(server, cancellationToken).ConfigureAwait(true);
                })
            .ConfigureAwait(true);

        if (!deleted)
        {
            return;
        }

        var folderPath = website.PhysicalPath;
        var root = _settings.Current.DefaultIisRoot;
        if (!WebsitePathSafety.IsUnderRoot(folderPath, root))
        {
            await _dialogs.ShowMessageAsync(
                    "Website deleted",
                    $"The IIS website '{website.Name}' was deleted. Its files remain at:\n{folderPath}\n\nThe folder is outside the configured IIS root, so this tool will not delete it.",
                    isError: false)
                .ConfigureAwait(true);
            return;
        }

        var deleteFolder = await _dialogs.ConfirmAsync(
                "Delete website files too?",
                $"The IIS website was deleted. Do you also want to permanently delete this folder and everything inside it?\n\n{folderPath}\n\nThis is a separate destructive action and cannot be undone.",
                "Delete folder permanently",
                "Keep files",
                dangerous: true)
            .ConfigureAwait(true);
        if (!deleteFolder)
        {
            await _dialogs.ShowMessageAsync(
                    "Website deleted",
                    $"The IIS website '{website.Name}' was deleted. The physical folder was preserved at:\n{folderPath}",
                    isError: false)
                .ConfigureAwait(true);
            return;
        }

        await RunOperationAsync(
                $"Deleting folder '{folderPath}'...",
                async cancellationToken =>
                {
                    await _websitesService.DeleteFolderAsync(server, website.Name, folderPath, cancellationToken)
                        .ConfigureAwait(true);
                    await ReloadAfterOperationAsync(server, cancellationToken).ConfigureAwait(true);
                    SuccessMessage = $"Website and folder deleted: {folderPath}";
                })
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EditBindingsAsync()
    {
        var server = SelectedServer;
        var website = SelectedWebsite;
        if (server is null || website is null)
        {
            return;
        }

        if (!await EnsureConnectedAsync(server).ConfigureAwait(true))
        {
            return;
        }

        var updated = await _dialogs.ShowEditBindingsAsync(server, website).ConfigureAwait(true);
        if (updated)
        {
            await RefreshWebsitesAsync(server, showSuccess: false, CancellationToken.None).ConfigureAwait(true);
            SuccessMessage = $"Bindings updated for '{website.Name}'.";
        }
    }

    [RelayCommand]
    private async Task ViewDetailsAsync()
    {
        var website = SelectedWebsite;
        if (website is null)
        {
            return;
        }

        var bindings = website.Bindings.Count == 0
            ? "None"
            : string.Join(Environment.NewLine, website.Bindings.Select(binding => "  • " + binding.Display));
        var details = $"""
                       Server: {website.ServerName}
                       Website: {website.Name}
                       State: {website.State}
                       Physical path: {website.PhysicalPath}
                       Application pool: {website.ApplicationPool}

                       Bindings:
                       {bindings}
                       """;
        await _dialogs.ShowMessageAsync("Website configuration", details, isError: false).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenInBrowserAsync()
    {
        var website = SelectedWebsite;
        if (website is null)
        {
            return;
        }

        var url = website.PrimaryUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            await _dialogs.ShowMessageAsync(
                    "Open website",
                    "This website has no usable binding or server hostname to build a URL.",
                    isError: true)
                .ConfigureAwait(true);
            return;
        }

        try
        {
            _launcher.OpenUrl(url);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not open {Url}.", url);
            await _dialogs.ShowMessageAsync("Open website", exception.Message, isError: true).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void ClearNotifications()
    {
        ErrorMessage = null;
        SuccessMessage = null;
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _operationSource?.Cancel();
    }

    partial void OnSearchTextChanged(string value)
    {
        WebsitesView.Refresh();
        UpdateFilteredState();
    }

    partial void OnSelectedWebsiteChanged(IisWebsite? value)
    {
        OnPropertyChanged(nameof(HasSelectedWebsite));
    }

    private async Task RefreshWebsitesAsync(
        ServerItemViewModel server,
        bool showSuccess,
        CancellationToken cancellationToken)
    {
        if (!server.IsConnected)
        {
            Websites.Clear();
            _loadedServerId = null;
            StatusMessage = "Connect the selected server to load IIS websites.";
            RaiseStateProperties();
            return;
        }

        await RunOperationAsync(
                "Loading IIS websites and server readiness...",
                async token =>
                {
                    var capabilities = await _catalog.RefreshCapabilitiesAsync(server, token).ConfigureAwait(true);
                    var websites = await _websitesService.RefreshWebsitesAsync(server, token).ConfigureAwait(true);
                    ReplaceWebsites(websites);
                    _loadedServerId = server.Id;
                    StatusMessage = $"Loaded {websites.Count} IIS website(s) from {server.Name}.";
                    if (showSuccess)
                    {
                        SuccessMessage = $"Website list refreshed. {websites.Count} site(s) found.";
                    }

                    if (capabilities is { IsManagementReady: false })
                    {
                        ErrorMessage = "Connected, but IIS management is not ready: " +
                                       string.Join(" ", capabilities.Problems);
                    }
                })
            .ConfigureAwait(true);
    }

    private async Task RunOperationAsync(string status, Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        SuccessMessage = null;
        StatusMessage = status;
        var source = new CancellationTokenSource();
        _operationSource = source;
        try
        {
            await operation(source.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled.";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Website operation failed.");
            ErrorMessage = exception.Message;
            StatusMessage = "Operation failed.";
        }
        finally
        {
            if (ReferenceEquals(_operationSource, source))
            {
                _operationSource = null;
            }

            source.Dispose();
            IsBusy = false;
        }
    }

    private async Task ReloadAfterOperationAsync(ServerItemViewModel server, CancellationToken cancellationToken)
    {
        try
        {
            var websites = await _websitesService.RefreshWebsitesAsync(server, cancellationToken).ConfigureAwait(true);
            ReplaceWebsites(websites);
            _loadedServerId = server.Id;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "The website list could not be refreshed after an operation.");
            ErrorMessage = "The operation completed, but the website list could not be refreshed: " + exception.Message;
        }
    }

    private async Task<bool> EnsureConnectedAsync(ServerItemViewModel server)
    {
        if (server.IsConnected)
        {
            return true;
        }

        var confirmed = await _dialogs.ConfirmAsync(
                "Server is not connected",
                $"'{server.Name}' is not connected. Connect now?",
                "Connect",
                "Cancel")
            .ConfigureAwait(true);
        if (!confirmed)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Connecting to {server.Name}...";
            await _catalog.EnsureConnectedAsync(server, CancellationToken.None).ConfigureAwait(true);
            StatusMessage = "Connected.";
            return true;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            StatusMessage = "Connection failed.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReplaceWebsites(IEnumerable<IisWebsite> websites)
    {
        var selectedName = SelectedWebsite?.Name;
        Websites.Clear();
        foreach (var website in websites.OrderBy(site => site.Name, StringComparer.OrdinalIgnoreCase))
        {
            Websites.Add(website);
        }

        SelectedWebsite = Websites.FirstOrDefault(site =>
                               string.Equals(site.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                           ?? Websites.FirstOrDefault();
        WebsitesView.Refresh();
        UpdateFilteredState();
    }

    private bool FilterWebsite(object obj)
    {
        if (obj is not IisWebsite website)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var search = SearchText.Trim();
        return website.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               website.PhysicalPath.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               website.ApplicationPool.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               website.State.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               website.Bindings.Any(binding => binding.Display.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateFilteredState()
    {
        var visible = WebsitesView.Cast<IisWebsite>().Count();
        FilteredCountText = visible == Websites.Count
            ? $"{visible} website(s)"
            : $"{visible} of {Websites.Count} website(s)";
        OnPropertyChanged(nameof(HasWebsites));
        OnPropertyChanged(nameof(HasNoWebsites));
        OnPropertyChanged(nameof(HasVisibleWebsites));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerCatalog.SelectedServer))
        {
            OnSelectedServerChanged();
        }
    }

    private void OnSelectedServerChanged()
    {
        Websites.Clear();
        SelectedWebsite = null;
        ErrorMessage = null;
        SuccessMessage = null;
        _loadedServerId = null;
        RaiseStateProperties();

        if (SelectedServer is { IsConnected: true } server)
        {
            _ = RefreshWebsitesAsync(server, showSuccess: false, CancellationToken.None);
        }
        else
        {
            StatusMessage = SelectedServer is null
                ? "Select a server to manage IIS websites."
                : "The selected server is disconnected. Connect it to load IIS websites.";
        }
    }

    private void OnServersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ServerItemViewModel server in e.OldItems)
            {
                Detach(server);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ServerItemViewModel server in e.NewItems)
            {
                Attach(server);
            }
        }
    }

    private void Attach(ServerItemViewModel server)
    {
        if (_serverHandlers.ContainsKey(server))
        {
            return;
        }

        PropertyChangedEventHandler handler = (_, args) => OnServerPropertyChanged(server, args);
        _serverHandlers[server] = handler;
        server.PropertyChanged += handler;
    }

    private void Detach(ServerItemViewModel server)
    {
        if (_serverHandlers.Remove(server, out var handler))
        {
            server.PropertyChanged -= handler;
        }
    }

    private void OnServerPropertyChanged(ServerItemViewModel server, PropertyChangedEventArgs args)
    {
        if (!ReferenceEquals(server, SelectedServer))
        {
            return;
        }

        if (args.PropertyName is nameof(ServerItemViewModel.IsConnected) or nameof(ServerItemViewModel.ConnectionState))
        {
            RunOnUi(() =>
            {
                RaiseStateProperties();
                if (server.IsConnected && !IsBusy && Websites.Count == 0)
                {
                    _ = RefreshWebsitesAsync(server, showSuccess: false, CancellationToken.None);
                }
                else if (!server.IsConnected)
                {
                    Websites.Clear();
                    _loadedServerId = null;
                    StatusMessage = "The selected server is disconnected.";
                    RaiseStateProperties();
                }
            });
        }
    }

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(IsServerConnected));
        OnPropertyChanged(nameof(ServerName));
        OnPropertyChanged(nameof(ServerEndpoint));
        OnPropertyChanged(nameof(HasWebsites));
        OnPropertyChanged(nameof(HasNoWebsites));
        OnPropertyChanged(nameof(HasVisibleWebsites));
        OnPropertyChanged(nameof(EmptyMessage));
        UpdateFilteredState();
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
