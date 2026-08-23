using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Windows.UI;
using WinRT;

namespace OCR_WinApp.Helpers;

/// <summary>
/// Nền blur (acrylic) tô theo một màu tint — dùng để áp tông màu của flavor lên nền app.
/// Theo theme Sáng/Tối và trạng thái active của cửa sổ.
/// </summary>
public sealed class TintedAcrylicBackdrop : IDisposable
{
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;
    private Window? _window;
    private FrameworkElement? _root;
    private object? _dispatcherQueueController;

    public void Attach(Window window, Color tint, double tintOpacity, double luminosityOpacity)
    {
        if (!DesktopAcrylicController.IsSupported()) return;

        _window = window;
        EnsureDispatcherQueueController();

        _configuration = new SystemBackdropConfiguration { IsInputActive = true };

        window.Activated += OnActivated;
        window.Closed += OnClosed;
        if (window.Content is FrameworkElement root)
        {
            _root = root;
            root.ActualThemeChanged += OnThemeChanged;
            SetTheme(root.ActualTheme);
        }

        _controller = new DesktopAcrylicController
        {
            TintColor = tint,
            FallbackColor = tint,
            TintOpacity = (float)tintOpacity,
            LuminosityOpacity = (float)luminosityOpacity
        };
        _controller.SetSystemBackdropConfiguration(_configuration);
        _controller.AddSystemBackdropTarget(window.As<ICompositionSupportsSystemBackdrop>());
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_configuration is not null)
            _configuration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => SetTheme(sender.ActualTheme);

    private void SetTheme(ElementTheme theme)
    {
        if (_configuration is null) return;
        _configuration.Theme = theme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => SystemBackdropTheme.Default
        };
    }

    private void OnClosed(object sender, WindowEventArgs args) => Dispose();

    private void EnsureDispatcherQueueController()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null) return;
        if (_dispatcherQueueController is not null) return;

        var options = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2,    // DQTYPE_THREAD_CURRENT
            apartmentType = 2  // DQTAT_COM_STA
        };
        CreateDispatcherQueueController(options, ref _dispatcherQueueController);
    }

    public void Dispose()
    {
        _controller?.Dispose();
        _controller = null;
        _configuration = null;

        if (_root is not null)
        {
            _root.ActualThemeChanged -= OnThemeChanged;
            _root = null;
        }

        if (_window is not null)
        {
            _window.Activated -= OnActivated;
            _window.Closed -= OnClosed;
            _window = null;
        }

        if (_dispatcherQueueController is Windows.System.DispatcherQueueController controller)
            _ = controller.ShutdownQueueAsync();

        _dispatcherQueueController = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        [In, Out, MarshalAs(UnmanagedType.IUnknown)] ref object? dispatcherQueueController);
}
