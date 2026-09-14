using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class DocxVbdPage : Page
    {
        public DocxVbdViewModel ViewModel { get; }

        public DocxVbdPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<DocxVbdViewModel>();
        }
    }
}
