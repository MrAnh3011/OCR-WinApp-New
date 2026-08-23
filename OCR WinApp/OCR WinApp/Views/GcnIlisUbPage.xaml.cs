using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class GcnIlisUbPage : Page
    {
        public GcnIlisUbViewModel ViewModel { get; }

        public GcnIlisUbPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<GcnIlisUbViewModel>();
        }
    }
}
