using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsServerManagement.App.Services;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly ServerCatalog _catalog;
    private readonly IActivityLogService _activity;
    private readonly INavigationService _navigation;
    private readonly Dictionary<ServerItemViewModel, PropertyChangedEventHandler> _serverHandlers = [];
    private bool _initialized;

    [ObservableProperty]
    private int _totalServers;

    [ObservableProperty]
    private int _connectedServers;

    [ObservableProperty]
    private int _totalWebsites;

    [ObservableProperty]
    private int _runningWebsites;

    [ObservableProperty]
    private int _stoppedWebsites;

    [ObservableProperty]
    private string _lastUpdatedText = "Not refreshed";

    public DashboardViewModel(
        ServerCatalog catalog,
        IActivityLogService activity,
        INavigationService navigation)
    {
        _catalog = catalog;
        _activity = activity;
        _navigation = navigation;

        _catalog.Servers.CollectionChanged += OnServersChanged;
        foreach (var server in _catalog.Servers)
        {
            Attach(server);
        }

        _activity.EntryAdded += OnEntryAdded;
    }

    public ObservableCollection<ActivityLogEntry> RecentOperations { get; } = [];
    public bool HasRecentOperations => RecentOperations.Count > 0;
    public bool HasNoRecentOperations => RecentOperations.Count == 0;
    public string ConnectedSummary => $"{ConnectedServers} of {TotalServers} connected";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Recalculate();
        try
        {
            var recent = await _activity.GetRecentAsync(8, cancellationToken).ConfigureAwait(true);
            RunOnUi(() =>
            {
                RecentOperations.Clear();
                foreach (var entry in recent)
                {
                    RecentOperations.Add(entry);
                }

                RaiseRecentProperties();
                LastUpdatedText = "Updated " + DateTime.Now.ToString("HH:mm:ss");
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private void GoToServers() => _navigation.Navigate("Servers");

    [RelayCommand]
    private void GoToWebsites() => _navigation.Navigate("Websites");

    [RelayCommand]
    private void CreateWebsite() => _navigation.Navigate("Websites");

    [RelayCommand]
    private void ViewLogs() => _navigation.Navigate("Logs");

    private void OnServersChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

        Recalculate();
    }

    private void Attach(ServerItemViewModel server)
    {
        if (_serverHandlers.ContainsKey(server))
        {
            return;
        }

        PropertyChangedEventHandler handler = (_, _) => Recalculate();
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

    private void Recalculate()
    {
        RunOnUi(() =>
        {
            TotalServers = _catalog.Servers.Count;
            ConnectedServers = _catalog.Servers.Count(server => server.IsConnected);
            TotalWebsites = _catalog.Servers.Sum(server => server.WebsiteCount);
            RunningWebsites = _catalog.Servers.Sum(server => server.RunningWebsiteCount);
            StoppedWebsites = _catalog.Servers.Sum(server => server.StoppedWebsiteCount);
            OnPropertyChanged(nameof(ConnectedSummary));
        });
    }

    private void OnEntryAdded(object? sender, ActivityLogEntry entry)
    {
        RunOnUi(() =>
        {
            RecentOperations.Insert(0, entry);
            while (RecentOperations.Count > 8)
            {
                RecentOperations.RemoveAt(RecentOperations.Count - 1);
            }

            RaiseRecentProperties();
            LastUpdatedText = "Updated " + DateTime.Now.ToString("HH:mm:ss");
        });
    }

    private void RaiseRecentProperties()
    {
        OnPropertyChanged(nameof(HasRecentOperations));
        OnPropertyChanged(nameof(HasNoRecentOperations));
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
