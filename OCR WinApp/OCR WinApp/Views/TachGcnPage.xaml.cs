using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class TachGcnPage : Page
    {
        public TachGcnViewModel ViewModel { get; }

        public TachGcnPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<TachGcnViewModel>();
        }
    }
}
