using System.Windows;
using System.Windows.Input;
using WindowsServerManagement.App.ViewModels;

namespace WindowsServerManagement.App.Views.Dialogs;

public partial class DiagnosticsDialog : Window
{
    public DiagnosticsDialog(DiagnosticsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
