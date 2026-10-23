using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WindowsServerManagement.App.Views.Dialogs;

public partial class MessageDialog : Window
{
    public MessageDialog(string title, string message, bool isError)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        if (isError)
        {
            IconGlyph.Text = "\uEA39";
            IconGlyph.Foreground = (Brush)FindResource("DangerBrush");
            IconBackground.Background = (Brush)FindResource("DangerSoftBrush");
        }
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
