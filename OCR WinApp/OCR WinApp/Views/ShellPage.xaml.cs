using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OCR.Business.Processors;
using OCR_WinApp.Navigation;
using OCR_WinApp.Services;
using OCR_WinApp.ViewModels;
using Windows.UI;

namespace OCR_WinApp.Views
{
    public sealed partial class ShellPage : Page
    {
        private readonly INavigationService _navigation;

        public ShellViewModel ViewModel { get; }

        public ShellPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<ShellViewModel>();
            DataContext = ViewModel;
            _navigation = App.GetService<INavigationService>();
            _navigation.Initialize(ContentFrame);

            var brand = App.GetService<IBrandService>();
            NavView.PaneTitle = brand.DisplayName;
            ApplyMenuTextColor(brand.TextColor);

            // Các màn chức năng OCR là mục CỐ ĐỊNH trong ShellPage.xaml (không sinh động).
        }

        private void ApplyMenuTextColor(Color? textColor)
        {
            if (textColor is not { } color) return;

            var primaryBrush = new SolidColorBrush(color);
            var secondaryBrush = new SolidColorBrush(Color.FromArgb(0xC0, color.R, color.G, color.B));
            var disabledBrush = new SolidColorBrush(Color.FromArgb(0x5C, color.R, color.G, color.B));

            GreetingTextBlock.Foreground = primaryBrush;

            foreach (var key in new[]
            {
                "TextFillColorPrimaryBrush",
                "NavigationViewPaneTitleForeground",
                "NavigationViewItemForeground",
                "NavigationViewItemForegroundPointerOver",
                "NavigationViewItemForegroundChecked",
                "NavigationViewItemForegroundCheckedPointerOver",
                "NavigationViewItemForegroundSelected",
                "NavigationViewItemForegroundSelectedPointerOver",
                "NavigationViewButtonForegroundPointerOver",
                "TitleBarPaneToggleButtonForeground",
                "TitleBarPaneToggleButtonForegroundPointerOver"
            })
            {
                NavView.Resources[key] = primaryBrush;
            }

            foreach (var key in new[]
            {
                "TextFillColorSecondaryBrush",
                "NavigationViewItemHeaderForeground",
                "NavigationViewItemForegroundPressed",
                "NavigationViewItemForegroundCheckedPressed",
                "NavigationViewItemForegroundSelectedPressed",
                "NavigationViewButtonForegroundPressed",
                "TitleBarPaneToggleButtonForegroundPressed"
            })
            {
                NavView.Resources[key] = secondaryBrush;
            }

            foreach (var key in new[]
            {
                "TextFillColorDisabledBrush",
                "NavigationViewItemForegroundDisabled",
                "NavigationViewItemForegroundCheckedDisabled",
                "NavigationViewItemForegroundSelectedDisabled",
                "NavigationViewButtonForegroundDisabled",
                "TitleBarPaneToggleButtonForegroundDisabled"
            })
            {
                NavView.Resources[key] = disabledBrush;
            }
        }

        private void NavView_Loaded(object sender, RoutedEventArgs e)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
            _navigation.NavigateTo(typeof(HomePage));
            // Đồng bộ trạng thái ban đầu (pane có thể đang đóng tùy bề rộng cửa sổ).
            ProfileBlock.Visibility = NavView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        // Ẩn avatar + greeting khi pane thu gọn, hiện lại khi mở.
        private void NavView_PaneOpening(NavigationView sender, object args)
            => ProfileBlock.Visibility = Visibility.Visible;

        private void NavView_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
            => ProfileBlock.Visibility = Visibility.Collapsed;

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer?.Tag is not string tag) return;

            switch (tag)
            {
                case "home":
                    _navigation.NavigateTo(typeof(HomePage));
                    break;
                case "about":
                    _navigation.NavigateTo(typeof(AboutPage));
                    break;
                case "settings":
                    _navigation.NavigateTo(typeof(SettingsPage));
                    break;
                case "dat-uy-ban":
                    _navigation.NavigateTo(typeof(DatUyBanPage));
                    break;
                case "tach-gcn":
                    _navigation.NavigateTo(typeof(TachGcnPage));
                    break;
                case "tach-gcn-new":
                    _navigation.NavigateTo(typeof(TachGcnNewPage));
                    break;
                case "tach-gt-gtk":
                    _navigation.NavigateTo(typeof(TachGtGtkPage));
                    break;
                case "gcn-new":
                    _navigation.NavigateTo(typeof(GcnNewPage));
                    break;
                case "gcn-vietbd":
                    _navigation.NavigateTo(typeof(GcnVietBdPage));
                    break;
                case "gcn-vbd-bn":
                    _navigation.NavigateTo(typeof(GcnVbdBnPage));
                    break;
                case "docx-vbd":
                    _navigation.NavigateTo(typeof(DocxVbdPage));
                    break;
                case "gcn-ilis-ub":
                    _navigation.NavigateTo(typeof(GcnIlisUbPage));
                    break;
                case "xoa-trang-trang":
                    _navigation.NavigateTo(typeof(XoaTrangTrangPage));
                    break;
                case "doi-ten-serial":
                    _navigation.NavigateTo(typeof(DoiTenSerialPage));
                    break;
                case "logout":
                    ViewModel.LogoutCommand.Execute(null);
                    break;
            }
        }
    }
}
