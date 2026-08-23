using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class TachGtGtkPage : Page
    {
        public TachGtGtkViewModel ViewModel { get; }

        public TachGtGtkPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<TachGtGtkViewModel>();
        }
    }
}
