using System.Windows;
using System.Windows.Input;
using WindowsServerManagement.App.ViewModels;

namespace WindowsServerManagement.App.Views.Dialogs;

public partial class BindingsEditorDialog : Window
{
    private readonly BindingsEditorViewModel _viewModel;

    public BindingsEditorDialog(BindingsEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += (_, success) => DialogResult = success;
        Closing += OnClosing;
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
