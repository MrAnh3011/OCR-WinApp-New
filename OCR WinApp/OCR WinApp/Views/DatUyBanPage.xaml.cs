using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class DatUyBanPage : Page
    {
        public DatUyBanViewModel ViewModel { get; }

        public DatUyBanPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<DatUyBanViewModel>();
        }
    }
}
