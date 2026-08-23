using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class GcnVietBdPage : Page
    {
        public GcnVietBdViewModel ViewModel { get; }

        public GcnVietBdPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<GcnVietBdViewModel>();
        }
    }
}
