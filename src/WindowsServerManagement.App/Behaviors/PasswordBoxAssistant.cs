using System.Windows;
using System.Windows.Controls;

namespace WindowsServerManagement.App.Behaviors;

public static class PasswordBoxAssistant
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword",
        typeof(string),
        typeof(PasswordBoxAssistant),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private static readonly DependencyProperty UpdatingPasswordProperty = DependencyProperty.RegisterAttached(
        "UpdatingPassword",
        typeof(bool),
        typeof(PasswordBoxAssistant),
        new PropertyMetadata(false));

    public static string GetBoundPassword(DependencyObject element)
    {
        return (string)element.GetValue(BoundPasswordProperty);
    }

    public static void SetBoundPassword(DependencyObject element, string value)
    {
        element.SetValue(BoundPasswordProperty, value);
    }

    private static bool GetUpdatingPassword(DependencyObject element)
    {
        return (bool)element.GetValue(UpdatingPasswordProperty);
    }

    private static void SetUpdatingPassword(DependencyObject element, bool value)
    {
        element.SetValue(UpdatingPasswordProperty, value);
    }

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox passwordBox)
        {
            return;
        }

        passwordBox.PasswordChanged -= OnPasswordChanged;
        if (!GetUpdatingPassword(passwordBox))
        {
            passwordBox.Password = e.NewValue as string ?? string.Empty;
        }

        passwordBox.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox)
        {
            return;
        }

        SetUpdatingPassword(passwordBox, true);
        SetBoundPassword(passwordBox, passwordBox.Password);
        SetUpdatingPassword(passwordBox, false);
    }
}
