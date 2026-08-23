using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class GcnNewPage : Page
    {
        public GcnNewViewModel ViewModel { get; }

        public GcnNewPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<GcnNewViewModel>();
        }
    }
}
