using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using OCR_WinApp.Helpers;
using OCR_WinApp.Services;
using OCR_WinApp.Views;

namespace OCR_WinApp
{
    /// <summary>
    /// Cửa sổ chính: nền blur tô theo tông màu flavor + chrome gọn + RootFrame.
    /// Khởi động hiển thị màn Đăng nhập; đăng nhập xong điều hướng RootFrame sang ShellPage.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly TintedAcrylicBackdrop _backdrop = new();
        private FrameworkElement? _root;

        public MainWindow()
        {
            InitializeComponent();

            var brand = App.GetService<IBrandService>();
            Title = brand.DisplayName;

            // Icon cửa sổ + taskbar theo flavor (icon.ico).
            try
            {
                if (System.IO.File.Exists(brand.IconIcoPath))
                    AppWindow.SetIcon(brand.IconIcoPath);
            }
            catch { /* không để lỗi icon chặn khởi động */ }

            // Cho nội dung đi sát mép trên; ShellPage sẽ đăng ký vùng kéo trong menu.
            ExtendsContentIntoTitleBar = true;

            // Nền blur tô theo tông màu + độ blur của flavor đang chọn.
            _backdrop.Attach(this, brand.TintColor, brand.TintOpacity, brand.LuminosityOpacity);

            if (Content is FrameworkElement root)
            {
                _root = root;
                root.ActualThemeChanged += OnActualThemeChanged;
            }

            Closed += OnClosed;

            RootFrame.Navigate(typeof(LoginPage));
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
            => TitleBarHelper.ApplyCaptionButtonColors(this, sender.ActualTheme);

        private void OnClosed(object sender, WindowEventArgs args)
        {
            Closed -= OnClosed;

            if (_root is not null)
            {
                _root.ActualThemeChanged -= OnActualThemeChanged;
                _root = null;
            }

            _backdrop.Dispose();
            Application.Current.Exit();
            ScheduleForceExitFallback();
        }

        private static void ScheduleForceExitFallback()
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500)).ConfigureAwait(false);
                Environment.Exit(0);
            });
        }
    }
}
