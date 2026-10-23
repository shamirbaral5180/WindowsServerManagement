using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsServerManagement.App.ViewModels;

namespace WindowsServerManagement.App;

public partial class MainWindow : Window
{
    private const uint MonitorDefaultToNearest = 2;

    private bool _isFullScreen;
    private WindowState _stateBeforeFullScreen;
    private ResizeMode _resizeModeBeforeFullScreen;
    private Rect _boundsBeforeFullScreen;
    private bool _topmostBeforeFullScreen;
    private double _maxWidthBeforeFullScreen;
    private double _maxHeightBeforeFullScreen;

    public MainWindow()
    {
        InitializeComponent();
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
        StateChanged += OnWindowStateChanged;
        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateWindowStateVisuals();
        if (DataContext is MainViewModel viewModel && viewModel.NavigationItems.Count > 0)
        {
            viewModel.SelectedNavigation = viewModel.NavigationItems[0];
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullScreen();
    }

    private void TitleArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _isFullScreen)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            return;
        }

        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        if (_isFullScreen)
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    private void EnterFullScreen()
    {
        _stateBeforeFullScreen = WindowState;
        _resizeModeBeforeFullScreen = ResizeMode;
        _topmostBeforeFullScreen = Topmost;
        _maxWidthBeforeFullScreen = MaxWidth;
        _maxHeightBeforeFullScreen = MaxHeight;
        _boundsBeforeFullScreen = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        _isFullScreen = true;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.NoResize;
        ApplyFullScreenBounds();
        Topmost = true;
        UpdateFullScreenVisuals();
    }

    private void ExitFullScreen()
    {
        if (!_isFullScreen)
        {
            return;
        }

        _isFullScreen = false;
        Topmost = _topmostBeforeFullScreen;
        WindowState = WindowState.Normal;
        Left = _boundsBeforeFullScreen.Left;
        Top = _boundsBeforeFullScreen.Top;
        Width = _boundsBeforeFullScreen.Width;
        Height = _boundsBeforeFullScreen.Height;
        MaxWidth = _maxWidthBeforeFullScreen;
        MaxHeight = _maxHeightBeforeFullScreen;
        ResizeMode = _resizeModeBeforeFullScreen;
        WindowState = _stateBeforeFullScreen;
        UpdateFullScreenVisuals();
    }

    private void ApplyFullScreenBounds()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                Left = 0;
                Top = 0;
                Width = SystemParameters.PrimaryScreenWidth;
                Height = SystemParameters.PrimaryScreenHeight;
                return;
            }

            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var topLeft = transform.Transform(new Point(info.rcMonitor.Left, info.rcMonitor.Top));
            var bottomRight = transform.Transform(new Point(info.rcMonitor.Right, info.rcMonitor.Bottom));
            Left = topLeft.X;
            Top = topLeft.Y;
            Width = Math.Max(1, bottomRight.X - topLeft.X);
            Height = Math.Max(1, bottomRight.Y - topLeft.Y);
        }
        catch
        {
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        UpdateWindowStateVisuals();
    }

    private void UpdateWindowStateVisuals()
    {
        if (MaxGlyph is null)
        {
            return;
        }

        MaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
        UpdateFullScreenVisuals();
    }

    private void UpdateFullScreenVisuals()
    {
        if (FullScreenGlyph is null)
        {
            return;
        }

        FullScreenGlyph.Text = _isFullScreen ? "\uE73F" : "\uE740";
        FullScreenButton.ToolTip = _isFullScreen ? "Exit full screen (F11 or Esc)" : "Full screen (F11)";
        MinimizeButton.IsEnabled = !_isFullScreen;
        MaximizeButton.IsEnabled = !_isFullScreen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
