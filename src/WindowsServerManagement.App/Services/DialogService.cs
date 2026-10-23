using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WindowsServerManagement.App.ViewModels;
using WindowsServerManagement.App.Views.Dialogs;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.App.Services;

public sealed class DialogService : IDialogService
{
    private readonly IServiceProvider _provider;

    public DialogService(IServiceProvider provider)
    {
        _provider = provider;
    }

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Confirm",
        string cancelText = "Cancel",
        bool dangerous = false)
    {
        var dialog = new ConfirmationDialog(title, message, confirmText, cancelText, dangerous)
        {
            Owner = GetOwner()
        };
        return Task.FromResult(dialog.ShowDialog() == true);
    }

    public Task ShowMessageAsync(string title, string message, bool isError = false)
    {
        var dialog = new MessageDialog(title, message, isError)
        {
            Owner = GetOwner()
        };
        dialog.ShowDialog();
        return Task.CompletedTask;
    }

    public Task<ServerProfile?> ShowServerEditorAsync(ServerProfile? existing)
    {
        var viewModel = new ServerEditorViewModel(_provider.GetRequiredService<ServerCatalog>(), this, existing);
        var dialog = new ServerEditorDialog(viewModel)
        {
            Owner = GetOwner()
        };
        dialog.ShowDialog();
        return Task.FromResult(viewModel.Result);
    }

    public Task<bool> ShowCreateWebsiteAsync(ServerItemViewModel server)
    {
        var viewModel = ActivatorUtilities.CreateInstance<CreateWebsiteViewModel>(_provider, server);
        var dialog = new CreateWebsiteDialog(viewModel)
        {
            Owner = GetOwner()
        };
        return Task.FromResult(dialog.ShowDialog() == true);
    }

    public Task<bool> ShowEditBindingsAsync(ServerItemViewModel server, IisWebsite website)
    {
        var viewModel = ActivatorUtilities.CreateInstance<BindingsEditorViewModel>(_provider, server, website);
        var dialog = new BindingsEditorDialog(viewModel)
        {
            Owner = GetOwner()
        };
        return Task.FromResult(dialog.ShowDialog() == true);
    }

    public Task ShowDiagnosticsAsync(ServerItemViewModel server, CapabilityReport report)
    {
        var dialog = new DiagnosticsDialog(new DiagnosticsViewModel(server, report))
        {
            Owner = GetOwner()
        };
        dialog.ShowDialog();
        return Task.CompletedTask;
    }

    private static Window? GetOwner()
    {
        return Application.Current.Windows
                   .OfType<Window>()
                   .FirstOrDefault(window => window.IsActive)
               ?? Application.Current.MainWindow;
    }
}
