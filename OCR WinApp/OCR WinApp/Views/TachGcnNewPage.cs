using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class TachGcnNewPage : Page
    {
        public TachGcnNewViewModel ViewModel { get; }

        public TachGcnNewPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<TachGcnNewViewModel>();
        }
    }
}
