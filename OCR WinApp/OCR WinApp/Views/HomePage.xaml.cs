using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class HomePage : Page
    {
        public HomeViewModel ViewModel { get; }

        public HomePage()
        {
            InitializeComponent();
            ViewModel = App.GetService<HomeViewModel>();
            DataContext = ViewModel;
        }
    }
}
