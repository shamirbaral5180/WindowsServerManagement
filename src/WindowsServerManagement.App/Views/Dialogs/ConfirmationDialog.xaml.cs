using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WindowsServerManagement.App.Views.Dialogs;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(string title, string message, string confirmText, string cancelText, bool dangerous)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        if (dangerous)
        {
            ConfirmButton.Style = (Style)FindResource("DangerButton");
            IconGlyph.Text = "\uE7BA";
            IconGlyph.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            var iconBackground = FindResource("DangerSoftBrush") as System.Windows.Media.Brush;
            if (iconBackground is not null && IconGlyph.Parent is Border iconBorder)
            {
                iconBorder.Background = iconBackground;
            }
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
