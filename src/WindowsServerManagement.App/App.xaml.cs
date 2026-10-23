using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WindowsServerManagement.App.Services;
using WindowsServerManagement.App.ViewModels;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Services;
using WindowsServerManagement.Infrastructure.Configuration;
using WindowsServerManagement.Infrastructure.Logging;
using WindowsServerManagement.Infrastructure.Security;
using WindowsServerManagement.Infrastructure.Ssh;
using WindowsServerManagement.Infrastructure.Storage;

namespace WindowsServerManagement.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private ILogger<App>? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            var paths = new ApplicationPaths();
            var services = new ServiceCollection();

            services.AddSingleton(paths);
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddDebug();
            });

            services.AddSingleton<IAppSettingsService, AppSettingsService>();
            services.AddSingleton<IServerProfileRepository, JsonServerProfileRepository>();
            services.AddSingleton<ISecretStore, DpapiSecretStore>();
            services.AddSingleton<IActivityLogRepository, JsonActivityLogRepository>();
            services.AddSingleton<IActivityLogService, ActivityLogService>();
            services.AddSingleton<IServerSessionManager, SshSessionManager>();
            services.AddSingleton<IIisWebsiteService, IisWebsiteService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IExternalLauncher, ExternalLauncher>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<ServerCatalog>();
            services.AddSingleton<WebsiteManagementService>();
            services.AddSingleton<DashboardViewModel>();
            services.AddSingleton<ServersViewModel>();
            services.AddSingleton<WebsitesViewModel>();
            services.AddSingleton<LogsViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<MainViewModel>();

            _services = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            var settingsService = _services.GetRequiredService<IAppSettingsService>();
            await settingsService.LoadAsync().ConfigureAwait(true);

            var loggerFactory = _services.GetRequiredService<ILoggerFactory>();
            loggerFactory.AddProvider(new FileLoggerProvider(paths, settingsService));
            _logger = _services.GetRequiredService<ILogger<App>>();
            _logger.LogInformation("Application starting. Data directory: {DataDirectory}.", paths.RootDirectory);

            var theme = _services.GetRequiredService<IThemeService>();
            theme.Apply(settingsService.Current.Theme);

            var mainViewModel = _services.GetRequiredService<MainViewModel>();
            await mainViewModel.InitializeAsync().ConfigureAwait(true);

            var window = new MainWindow
            {
                DataContext = mainViewModel
            };
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            _logger?.LogCritical(exception, "Startup failed.");
            MessageBox.Show(
                "The application could not start." + Environment.NewLine + Environment.NewLine + exception.Message,
                "Server Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Application exiting.");
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled dispatcher exception.");
        MessageBox.Show(
            "An unexpected error occurred." + Environment.NewLine + Environment.NewLine + e.Exception.Message,
            "Server Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger?.LogCritical(exception, "Unhandled application exception.");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }
}
