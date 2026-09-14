using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class GcnVbdBnPage : Page
    {
        public GcnVbdBnViewModel ViewModel { get; }

        public GcnVbdBnPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<GcnVbdBnViewModel>();
        }
    }
}
