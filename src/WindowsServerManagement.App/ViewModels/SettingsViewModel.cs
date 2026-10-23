using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsServerManagement.App.Services;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services;
using WindowsServerManagement.Infrastructure.Configuration;

namespace WindowsServerManagement.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IExternalLauncher _launcher;
    private readonly IDialogService _dialogs;
    private readonly ApplicationPaths _paths;

    [ObservableProperty]
    private ThemeMode _selectedTheme = ThemeMode.System;

    [ObservableProperty]
    private string _defaultIisRoot = @"C:\inetpub";

    [ObservableProperty]
    private string _applicationPoolPattern = "{site}";

    [ObservableProperty]
    private string _applicationPoolRuntime = "v4.0";

    [ObservableProperty]
    private string _applicationPoolPipeline = "Integrated";

    [ObservableProperty]
    private string _defaultWebsitePortText = "8080";

    [ObservableProperty]
    private string _connectionTimeoutText = "20";

    [ObservableProperty]
    private string _commandTimeoutText = "90";

    [ObservableProperty]
    private bool _enableFileLogging = true;

    [ObservableProperty]
    private string _minimumLogLevel = "Information";

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    public SettingsViewModel(
        IAppSettingsService settings,
        IThemeService theme,
        IExternalLauncher launcher,
        IDialogService dialogs,
        ApplicationPaths paths)
    {
        _settings = settings;
        _theme = theme;
        _launcher = launcher;
        _dialogs = dialogs;
        _paths = paths;
    }

    public IReadOnlyList<ThemeMode> ThemeOptions { get; } = [ThemeMode.Light, ThemeMode.Dark, ThemeMode.System];
    public IReadOnlyList<string> RuntimeOptions { get; } = ["v4.0", "v2.0", "No Managed Code"];
    public IReadOnlyList<string> PipelineOptions { get; } = ["Integrated", "Classic"];
    public IReadOnlyList<string> LogLevelOptions { get; } = ["Debug", "Information", "Warning", "Error"];
    public string DataFolderPath => _paths.RootDirectory;
    public string LogsFolderPath => _paths.LogsDirectory;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _settings.LoadAsync(cancellationToken).ConfigureAwait(true);
        RefreshFromSettings();
    }

    public void RefreshFromSettings()
    {
        var current = _settings.Current;
        SelectedTheme = current.Theme;
        DefaultIisRoot = current.DefaultIisRoot;
        ApplicationPoolPattern = current.DefaultApplicationPoolPattern;
        ApplicationPoolRuntime = current.DefaultApplicationPoolRuntimeVersion;
        ApplicationPoolPipeline = current.DefaultApplicationPoolPipelineMode;
        DefaultWebsitePortText = current.DefaultWebsitePort.ToString();
        ConnectionTimeoutText = current.ConnectionTimeoutSeconds.ToString();
        CommandTimeoutText = current.CommandTimeoutSeconds.ToString();
        EnableFileLogging = current.EnableFileLogging;
        MinimumLogLevel = current.MinimumLogLevel;
    }

    public void SyncTheme()
    {
        SelectedTheme = _settings.Current.Theme;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsSaving)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;

        if (!WebsitePathSafety.IsValidRoot(DefaultIisRoot, out var rootError))
        {
            ErrorMessage = rootError;
            return;
        }

        if (string.IsNullOrWhiteSpace(ApplicationPoolPattern))
        {
            ErrorMessage = "Default application pool pattern is required.";
            return;
        }

        if (!int.TryParse(DefaultWebsitePortText, out var port) || port is < 1 or > 65535)
        {
            ErrorMessage = "Default website port must be between 1 and 65535.";
            return;
        }

        if (!int.TryParse(ConnectionTimeoutText, out var connectionTimeout) || connectionTimeout is < 5 or > 300)
        {
            ErrorMessage = "Connection timeout must be between 5 and 300 seconds.";
            return;
        }

        if (!int.TryParse(CommandTimeoutText, out var commandTimeout) || commandTimeout is < 10 or > 900)
        {
            ErrorMessage = "Command timeout must be between 10 and 900 seconds.";
            return;
        }

        IsSaving = true;
        try
        {
            var updated = _settings.Current.Clone();
            updated.Theme = SelectedTheme;
            updated.DefaultIisRoot = DefaultIisRoot.Trim();
            updated.DefaultApplicationPoolPattern = ApplicationPoolPattern.Trim();
            updated.DefaultApplicationPoolRuntimeVersion = ApplicationPoolRuntime;
            updated.DefaultApplicationPoolPipelineMode = ApplicationPoolPipeline;
            updated.DefaultWebsitePort = port;
            updated.ConnectionTimeoutSeconds = connectionTimeout;
            updated.CommandTimeoutSeconds = commandTimeout;
            updated.EnableFileLogging = EnableFileLogging;
            updated.MinimumLogLevel = MinimumLogLevel;

            await _settings.SaveAsync(updated, CancellationToken.None).ConfigureAwait(true);
            _theme.Apply(updated.Theme);
            RefreshFromSettings();
            StatusMessage = "Settings saved.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task OpenDataFolderAsync()
    {
        try
        {
            _launcher.OpenFolder(_paths.RootDirectory);
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("Open data folder", exception.Message, isError: true).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task OpenLogsFolderAsync()
    {
        try
        {
            _launcher.OpenFolder(_paths.LogsDirectory);
        }
        catch (Exception exception)
        {
            await _dialogs.ShowMessageAsync("Open logs folder", exception.Message, isError: true).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        SelectedTheme = ThemeMode.Light;
        DefaultIisRoot = @"C:\inetpub";
        ApplicationPoolPattern = "{site}";
        ApplicationPoolRuntime = "v4.0";
        ApplicationPoolPipeline = "Integrated";
        DefaultWebsitePortText = "8080";
        ConnectionTimeoutText = "20";
        CommandTimeoutText = "90";
        EnableFileLogging = true;
        MinimumLogLevel = "Information";
        StatusMessage = "Defaults restored in the form. Choose Save to apply them.";
        ErrorMessage = null;
    }
}
