using System.IO;

namespace WindowsServerManagement.App.Services;

public sealed record NavigationRequest(string PageKey);

public interface INavigationService
{
    event EventHandler<NavigationRequest>? NavigationRequested;
    void Navigate(string pageKey);
}

public sealed class NavigationService : INavigationService
{
    public event EventHandler<NavigationRequest>? NavigationRequested;

    public void Navigate(string pageKey)
    {
        NavigationRequested?.Invoke(this, new NavigationRequest(pageKey));
    }
}

public interface IExternalLauncher
{
    void OpenUrl(string url);
    void OpenFolder(string path);
}

public sealed class ExternalLauncher : IExternalLauncher
{
    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Only HTTP and HTTPS URLs can be opened.");
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
    }

    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            throw new DirectoryNotFoundException("The folder does not exist.");
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
        {
            UseShellExecute = true
        });
    }
}
