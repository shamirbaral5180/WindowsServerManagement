using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsServerManagement.App.Services;
using WindowsServerManagement.Core.Exceptions;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.App.ViewModels;

public sealed partial class BindingRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _protocol = "http";

    [ObservableProperty]
    private string _portText = "80";

    [ObservableProperty]
    private string _hostname = string.Empty;

    [ObservableProperty]
    private string _certificateThumbprint = string.Empty;

    public IReadOnlyList<string> Protocols { get; } = ["http", "https"];
    public bool IsHttps => string.Equals(Protocol, "https", StringComparison.OrdinalIgnoreCase);

    partial void OnProtocolChanged(string value)
    {
        OnPropertyChanged(nameof(IsHttps));
    }

    public WebsiteBinding ToBinding()
    {
        if (!int.TryParse(PortText, out var port))
        {
            throw new ValidationException($"Binding port '{PortText}' is not a number.");
        }

        return new WebsiteBinding
        {
            Protocol = Protocol.Trim().ToLowerInvariant(),
            Port = port,
            Hostname = Hostname.Trim(),
            IpAddress = "*",
            CertificateThumbprint = CertificateThumbprint.Trim(),
            BindingInformation = $"*:{port}:{Hostname.Trim()}"
        };
    }
}

public sealed partial class BindingsEditorViewModel : ObservableObject
{
    private readonly ServerItemViewModel _server;
    private readonly IisWebsite _website;
    private readonly WebsiteManagementService _websitesService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage = "Edit the bindings below. HTTPS bindings require a certificate thumbprint.";

    public BindingsEditorViewModel(
        ServerItemViewModel server,
        IisWebsite website,
        WebsiteManagementService websitesService)
    {
        _server = server;
        _website = website;
        _websitesService = websitesService;

        foreach (var binding in website.Bindings)
        {
            Bindings.Add(new BindingRowViewModel
            {
                Protocol = binding.Protocol,
                PortText = binding.Port.ToString(),
                Hostname = binding.Hostname,
                CertificateThumbprint = binding.CertificateThumbprint
            });
        }

        if (Bindings.Count == 0)
        {
            Bindings.Add(new BindingRowViewModel { Protocol = "http", PortText = "80" });
        }
    }

    public event EventHandler<bool>? CloseRequested;

    public ObservableCollection<BindingRowViewModel> Bindings { get; } = [];
    public string WebsiteName => _website.Name;
    public string ServerName => _server.Name;
    public string PhysicalPath => _website.PhysicalPath;
    public bool CanRemove => Bindings.Count > 1;

    [RelayCommand]
    private void AddBinding()
    {
        Bindings.Add(new BindingRowViewModel
        {
            Protocol = "http",
            PortText = Bindings.Count == 1 && Bindings[0].PortText == "80" ? "8080" : "80"
        });
        OnPropertyChanged(nameof(CanRemove));
    }

    [RelayCommand]
    private void RemoveBinding(BindingRowViewModel? row)
    {
        if (row is null || Bindings.Count <= 1)
        {
            return;
        }

        Bindings.Remove(row);
        OnPropertyChanged(nameof(CanRemove));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        var succeeded = false;
        try
        {
            var bindings = Bindings.Select(row => row.ToBinding()).ToList();
            await _websitesService.UpdateBindingsAsync(_server, _website.Name, bindings, CancellationToken.None)
                .ConfigureAwait(true);
            succeeded = true;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception is AppException ? exception.Message : "Binding update failed. " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (succeeded)
        {
            CloseRequested?.Invoke(this, true);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsBusy)
        {
            CloseRequested?.Invoke(this, false);
        }
    }
}
