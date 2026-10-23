using ThemeMode = WindowsServerManagement.Core.Models.ThemeMode;
using System.Windows;
using Microsoft.Win32;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.App.Services;

public interface IThemeService
{
    event EventHandler? ThemeApplied;

    ThemeMode CurrentMode { get; }
    void Apply(ThemeMode mode);
}

public sealed class ThemeService : IThemeService
{
    private const string LightUri = "Themes/Light.xaml";
    private const string DarkUri = "Themes/Dark.xaml";

    public event EventHandler? ThemeApplied;

    public ThemeMode CurrentMode { get; private set; } = ThemeMode.Light;

    public void Apply(ThemeMode mode)
    {
        CurrentMode = mode;
        var effectiveMode = mode == ThemeMode.System ? GetSystemTheme() : mode;
        var uri = new Uri(effectiveMode == ThemeMode.Dark ? DarkUri : LightUri, UriKind.Relative);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries
            .Where(dictionary => dictionary.Source is not null &&
                                 dictionary.Source.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var dictionary in existing)
        {
            dictionaries.Remove(dictionary);
        }

        var themeDictionary = new ResourceDictionary { Source = uri };
        dictionaries.Insert(0, themeDictionary);
        ThemeApplied?.Invoke(this, EventArgs.Empty);
    }

    private static ThemeMode GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0 ? ThemeMode.Dark : ThemeMode.Light;
        }
        catch
        {
            return ThemeMode.Light;
        }
    }
}

public interface IDialogService
{
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Confirm",
        string cancelText = "Cancel",
        bool dangerous = false);

    Task ShowMessageAsync(string title, string message, bool isError = false);

    Task<ServerProfile?> ShowServerEditorAsync(ServerProfile? existing);

    Task<bool> ShowCreateWebsiteAsync(ServerItemViewModel server);

    Task<bool> ShowEditBindingsAsync(ServerItemViewModel server, IisWebsite website);

    Task ShowDiagnosticsAsync(ServerItemViewModel server, CapabilityReport report);
}

