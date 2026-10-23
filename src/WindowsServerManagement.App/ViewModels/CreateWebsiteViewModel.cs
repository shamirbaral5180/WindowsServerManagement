using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsServerManagement.App.Services;
using WindowsServerManagement.Core.Exceptions;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services;
using WindowsServerManagement.Core.Services.Validation;

namespace WindowsServerManagement.App.ViewModels;

public sealed partial class CreateWebsiteViewModel : ObservableObject
{
    private readonly ServerItemViewModel _server;
    private readonly WebsiteManagementService _websitesService;
    private readonly IAppSettingsService _settings;
    private CancellationTokenSource? _operationSource;
    private string _lastSuggestedPath = string.Empty;
    private string _lastSuggestedPool = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _physicalPath = string.Empty;

    [ObservableProperty]
    private string _portText;

    [ObservableProperty]
    private string _hostname = string.Empty;

    [ObservableProperty]
    private bool _useHttps;

    [ObservableProperty]
    private IisCertificateInfo? _selectedCertificate;

    [ObservableProperty]
    private string _certificateThumbprint = string.Empty;

    [ObservableProperty]
    private string _applicationPoolName = string.Empty;

    [ObservableProperty]
    private string _applicationPoolRuntimeVersion = "v4.0";

    [ObservableProperty]
    private string _applicationPoolPipelineMode = "Integrated";

    [ObservableProperty]
    private bool _startApplicationPoolImmediately = true;

    [ObservableProperty]
    private bool _useExistingFolder;

    [ObservableProperty]
    private bool _startImmediately = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _statusMessage = "Fill in the website details and create it on the selected server.";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _resultMessage;

    [ObservableProperty]
    private string? _warningsText;

    [ObservableProperty]
    private bool _isLoadingCertificates;

    [ObservableProperty]
    private string _certificateStatus = "Select HTTPS to load server certificates.";

    public CreateWebsiteViewModel(
        ServerItemViewModel server,
        WebsiteManagementService websitesService,
        IAppSettingsService settings)
    {
        _server = server;
        _websitesService = websitesService;
        _settings = settings;
        _portText = settings.Current.DefaultWebsitePort.ToString();
        IisRoot = settings.Current.DefaultIisRoot;
        ApplicationPoolRuntimeVersion = settings.Current.DefaultApplicationPoolRuntimeVersion;
        ApplicationPoolPipelineMode = settings.Current.DefaultApplicationPoolPipelineMode;
    }

    public event EventHandler<bool>? CloseRequested;

    public bool CanCreate => !IsBusy && !IsComplete;
    public bool IsHttps => UseHttps;
    public bool HasResult => IsComplete;
    public string ServerSummary => $"{_server.Name} | {_server.Endpoint}";
    public string IisRoot { get; }
    public IReadOnlyList<string> Protocols { get; } = ["HTTP", "HTTPS"];
    public IReadOnlyList<string> RuntimeOptions { get; } = ["v4.0", "v2.0", "No Managed Code"];
    public IReadOnlyList<string> PipelineOptions { get; } = ["Integrated", "Classic"];
    public ObservableCollection<IisCertificateInfo> Certificates { get; } = [];
    public string SelectedProtocol
    {
        get => UseHttps ? "HTTPS" : "HTTP";
        set => UseHttps = string.Equals(value, "HTTPS", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InitializeAsync()
    {
        UpdateSuggestions(force: true);
        if (UseHttps)
        {
            await LoadCertificatesAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        ResultMessage = null;
        WarningsText = null;
        IsComplete = false;

        if (!int.TryParse(PortText, out var port))
        {
            ErrorMessage = "Port must be a number.";
            return;
        }

        var request = BuildRequest(port);
        var validation = WebsiteValidator.ValidateCreateRequest(request);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ErrorText;
            return;
        }

        _operationSource = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 2;
        StatusMessage = "Starting website creation...";
        OnPropertyChanged(nameof(CanCreate));

        try
        {
            var progress = new Progress<WebsiteCreationProgress>(update =>
            {
                ProgressPercent = Math.Clamp(update.Percent, 0, 100);
                StatusMessage = update.Message;
            });

            var result = await _websitesService.CreateWebsiteAsync(_server, request, progress, _operationSource.Token)
                .ConfigureAwait(true);

            ProgressPercent = 100;
            IsComplete = true;
            StatusMessage = "Website created successfully.";
            ResultMessage = $"""
                             IIS website: {result.SiteName}
                             Physical folder: {result.FolderPath}
                             Application pool: {result.ApplicationPool}
                             Binding: {result.Binding}
                             State: {result.State}
                             Folder created: {(result.FolderCreated ? "Yes" : "No (existing folder reused)")}
                             Application pool created: {(result.AppPoolCreated ? "Yes" : "No (existing pool reused)")}
                             """;
            if (result.Warnings.Count > 0)
            {
                WarningsText = string.Join(Environment.NewLine, result.Warnings.Select(warning => "- " + warning));
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
            ErrorMessage = "Website creation was cancelled. The remote server may contain partially created resources.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Website creation failed.";
            ErrorMessage = exception is AppException
                ? exception.Message
                : "Website creation failed. " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            _operationSource?.Dispose();
            _operationSource = null;
            OnPropertyChanged(nameof(CanCreate));
        }
    }

    [RelayCommand]
    private async Task RefreshCertificatesAsync()
    {
        await LoadCertificatesAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelOrClose()
    {
        if (IsBusy)
        {
            _operationSource?.Cancel();
            StatusMessage = "Cancelling...";
            return;
        }

        CloseRequested?.Invoke(this, IsComplete);
    }

    public void CancelIfBusy()
    {
        _operationSource?.Cancel();
    }

    partial void OnNameChanged(string value)
    {
        UpdateSuggestions(force: false);
    }

    partial void OnUseHttpsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsHttps));
        OnPropertyChanged(nameof(SelectedProtocol));
        if (value && Certificates.Count == 0)
        {
            _ = LoadCertificatesAsync();
        }
    }

    partial void OnSelectedCertificateChanged(IisCertificateInfo? value)
    {
        if (!string.IsNullOrWhiteSpace(value?.Thumbprint))
        {
            CertificateThumbprint = value.Thumbprint;
        }
    }

    private void UpdateSuggestions(bool force)
    {
        var name = Name.Trim();
        var suggestedPath = WebsitePathSafety.BuildPhysicalPath(IisRoot, name, out var path, out _)
            ? path
            : string.Empty;

        if (force || string.IsNullOrWhiteSpace(PhysicalPath) ||
            string.Equals(PhysicalPath, _lastSuggestedPath, StringComparison.OrdinalIgnoreCase))
        {
            PhysicalPath = suggestedPath;
            _lastSuggestedPath = suggestedPath;
        }

        var pattern = _settings.Current.DefaultApplicationPoolPattern;
        var suggestedPool = string.IsNullOrWhiteSpace(pattern) ? name : pattern.Replace("{site}", name, StringComparison.OrdinalIgnoreCase);
        if (force || string.IsNullOrWhiteSpace(ApplicationPoolName) ||
            string.Equals(ApplicationPoolName, _lastSuggestedPool, StringComparison.Ordinal))
        {
            ApplicationPoolName = suggestedPool;
            _lastSuggestedPool = suggestedPool;
        }
    }

    private WebsiteCreateRequest BuildRequest(int port)
    {
        var physicalPath = PhysicalPath.Trim();
        var folderName = Path.GetFileName(physicalPath.TrimEnd('\\'));
        return new WebsiteCreateRequest
        {
            Name = Name.Trim(),
            FolderName = string.IsNullOrWhiteSpace(folderName) ? Name.Trim() : folderName,
            PhysicalPath = physicalPath,
            Port = port,
            Hostname = Hostname.Trim(),
            UseHttps = UseHttps,
            CertificateThumbprint = WebsiteValidator.NormalizeThumbprint(CertificateThumbprint),
            ApplicationPoolName = ApplicationPoolName.Trim(),
            IisRoot = IisRoot,
            ApplicationPoolRuntimeVersion = ApplicationPoolRuntimeVersion,
            ApplicationPoolPipelineMode = ApplicationPoolPipelineMode,
            StartApplicationPoolImmediately = StartApplicationPoolImmediately,
            UseExistingFolder = UseExistingFolder,
            StartImmediately = StartImmediately
        };
    }

    private async Task LoadCertificatesAsync()
    {
        if (IsLoadingCertificates || !_server.IsConnected)
        {
            CertificateStatus = _server.IsConnected
                ? CertificateStatus
                : "Connect to the server to load its certificates.";
            return;
        }

        IsLoadingCertificates = true;
        CertificateStatus = "Loading certificates from LocalMachine\\My...";
        try
        {
            var certificates = await _websitesService.GetCertificatesAsync(_server, CancellationToken.None)
                .ConfigureAwait(true);
            Certificates.Clear();
            foreach (var certificate in certificates.OrderBy(cert => cert.Subject, StringComparer.OrdinalIgnoreCase))
            {
                Certificates.Add(certificate);
            }

            CertificateStatus = Certificates.Count == 0
                ? "No certificates with private keys were found in LocalMachine\\My."
                : $"{Certificates.Count} certificate(s) found. Select one or enter a thumbprint.";
            if (!string.IsNullOrWhiteSpace(CertificateThumbprint))
            {
                SelectedCertificate = Certificates.FirstOrDefault(cert =>
                    string.Equals(cert.Thumbprint, WebsiteValidator.NormalizeThumbprint(CertificateThumbprint), StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (Exception exception)
        {
            CertificateStatus = "Could not load certificates: " + exception.Message;
        }
        finally
        {
            IsLoadingCertificates = false;
        }
    }
}
