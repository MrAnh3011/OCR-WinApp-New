using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using OCR_WinApp.Services;
using OCR_WinApp.ViewModels;
using Windows.System;

namespace OCR_WinApp.Views
{
    public sealed partial class LoginPage : Page
    {
        public LoginViewModel ViewModel { get; }

        public LoginPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<LoginViewModel>();
            ViewModel.LoginSucceeded += OnLoginSucceeded;

            // Banner theo flavor (ảnh PNG → đổi theo tham số Flavor).
            var brand = App.GetService<IBrandService>();
            BrandBanner.Source = new BitmapImage(brand.BannerUri);
        }

        private void OnLoginSucceeded(object? sender, EventArgs e)
        {
            // Frame ở đây là RootFrame của MainWindow.
            Frame.Navigate(typeof(ShellPage));
        }

        // Enter trong khung đăng nhập = nhấn nút Đăng nhập (nếu đủ điều kiện).
        private void LoginForm_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;
            if (ViewModel.LoginCommand.CanExecute(null))
            {
                ViewModel.LoginCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
