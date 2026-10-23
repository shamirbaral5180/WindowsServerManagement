using System.Windows;
using System.Windows.Input;
using WindowsServerManagement.App.ViewModels;

namespace WindowsServerManagement.App.Views.Dialogs;

public partial class CreateWebsiteDialog : Window
{
    private readonly CreateWebsiteViewModel _viewModel;

    public CreateWebsiteDialog(CreateWebsiteViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnCloseRequested(object? sender, bool success)
    {
        DialogResult = success;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            e.Cancel = true;
            _viewModel.CancelIfBusy();
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
