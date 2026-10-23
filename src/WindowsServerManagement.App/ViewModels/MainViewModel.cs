using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WindowsServerManagement.App.Services;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.App.ViewModels;

public sealed partial class NavigationItem : ObservableObject
{
    public NavigationItem(string key, string label, string glyph)
    {
        Key = key;
        Label = label;
        Glyph = glyph;
    }

    public string Key { get; }
    public string Label { get; }
    public string Glyph { get; }
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ServerCatalog _catalog;
    private readonly DashboardViewModel _dashboard;
    private readonly ServersViewModel _servers;
    private readonly WebsitesViewModel _websites;
    private readonly LogsViewModel _logs;
    private readonly SettingsViewModel _settings;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IAppSettingsService _appSettings;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private NavigationItem? _selectedNavigation;

    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private string _currentPageTitle = "Dashboard";

    [ObservableProperty]
    private string _currentPageSubtitle = "Overview of your connected Windows servers";

    public MainViewModel(
        ServerCatalog catalog,
        DashboardViewModel dashboard,
        ServersViewModel servers,
        WebsitesViewModel websites,
        LogsViewModel logs,
        SettingsViewModel settings,
        INavigationService navigation,
        IThemeService theme,
        IAppSettingsService appSettings,
        ILogger<MainViewModel> logger)
    {
        _catalog = catalog;
        _dashboard = dashboard;
        _servers = servers;
        _websites = websites;
        _logs = logs;
        _settings = settings;
        _navigation = navigation;
        _theme = theme;
        _appSettings = appSettings;
        _logger = logger;

        NavigationItems =
        [
            new NavigationItem("Dashboard", "Dashboard", "\uE80F"),
            new NavigationItem("Servers", "Servers", "\uE968"),
            new NavigationItem("Websites", "IIS Websites", "\uE774"),
            new NavigationItem("Logs", "Activity Logs", "\uE9D9"),
            new NavigationItem("Settings", "Settings", "\uE713")
        ];

        _navigation.NavigationRequested += OnNavigationRequested;
        _theme.ThemeApplied += OnThemeApplied;
        _selectedNavigation = NavigationItems[0];
        _currentViewModel = _dashboard;
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }
    public ServerCatalog Catalog => _catalog;
    public string AppVersion => "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
    public bool IsDarkTheme => _theme.CurrentMode == ThemeMode.Dark;
    public string ThemeGlyph => IsDarkTheme ? "\uE706" : "\uE708";
    public string ThemeToolTip => IsDarkTheme ? "Switch to light theme" : "Switch to dark theme";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _catalog.InitializeAsync(cancellationToken).ConfigureAwait(true);
        await _settings.InitializeAsync(cancellationToken).ConfigureAwait(true);
        await _logs.InitializeAsync(cancellationToken).ConfigureAwait(true);
        await _dashboard.InitializeAsync(cancellationToken).ConfigureAwait(true);
        await _websites.InitializeAsync(cancellationToken).ConfigureAwait(true);
        SelectPage("Dashboard");
    }

    [RelayCommand]
    private void Navigate(string? pageKey)
    {
        if (!string.IsNullOrWhiteSpace(pageKey))
        {
            SelectPage(pageKey);
        }
    }

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        var next = IsDarkTheme ? ThemeMode.Light : ThemeMode.Dark;
        _theme.Apply(next);
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeGlyph));
        OnPropertyChanged(nameof(ThemeToolTip));

        try
        {
            var current = _appSettings.Current.Clone();
            current.Theme = next;
            await _appSettings.SaveAsync(current, CancellationToken.None).ConfigureAwait(true);
            _settings.SyncTheme();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not persist the theme preference.");
        }
    }

    partial void OnSelectedNavigationChanged(NavigationItem? value)
    {
        if (value is null)
        {
            return;
        }

        ApplyPage(value.Key);
    }

    private void OnThemeApplied(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeGlyph));
        OnPropertyChanged(nameof(ThemeToolTip));
    }

    private void OnNavigationRequested(object? sender, NavigationRequest request)
    {
        SelectPage(request.PageKey);
    }

    private void SelectPage(string pageKey)
    {
        var item = NavigationItems.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, pageKey, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        if (ReferenceEquals(SelectedNavigation, item))
        {
            ApplyPage(item.Key);
            return;
        }

        SelectedNavigation = item;
    }

    private void ApplyPage(string pageKey)
    {
        switch (pageKey)
        {
            case "Servers":
                CurrentViewModel = _servers;
                CurrentPageTitle = "Servers";
                CurrentPageSubtitle = "Manage SSH connection profiles and connection state";
                break;
            case "Websites":
                CurrentViewModel = _websites;
                CurrentPageTitle = "IIS Websites";
                CurrentPageSubtitle = "Create and manage websites on the selected remote server";
                _ = _websites.OnActivatedAsync();
                break;
            case "Logs":
                CurrentViewModel = _logs;
                CurrentPageTitle = "Activity Logs";
                CurrentPageSubtitle = "Audit trail of connections and IIS operations";
                break;
            case "Settings":
                CurrentViewModel = _settings;
                CurrentPageTitle = "Settings";
                CurrentPageSubtitle = "Defaults, appearance, timeouts, and logging";
                break;
            default:
                CurrentViewModel = _dashboard;
                CurrentPageTitle = "Dashboard";
                CurrentPageSubtitle = "Overview of your connected Windows servers";
                _ = _dashboard.RefreshAsync();
                break;
        }
    }
}
