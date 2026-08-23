using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class DoiTenSerialPage : Page
    {
        public DoiTenSerialViewModel ViewModel { get; }

        public DoiTenSerialPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<DoiTenSerialViewModel>();
        }
    }
}
