using System.Windows;
using System.Windows.Input;
using WindowsServerManagement.App.ViewModels;

namespace WindowsServerManagement.App.Views.Dialogs;

public partial class ServerEditorDialog : Window
{
    private readonly ServerEditorViewModel _viewModel;

    public ServerEditorDialog(ServerEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = viewModel.WindowTitle;
        viewModel.CloseRequested += OnCloseRequested;
        Closing += OnClosing;
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
