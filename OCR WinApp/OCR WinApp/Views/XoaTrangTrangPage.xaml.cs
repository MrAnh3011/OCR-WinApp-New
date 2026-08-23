using Microsoft.UI.Xaml.Controls;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp.Views
{
    public sealed partial class XoaTrangTrangPage : Page
    {
        public XoaTrangTrangViewModel ViewModel { get; }

        public XoaTrangTrangPage()
        {
            InitializeComponent();
            ViewModel = App.GetService<XoaTrangTrangViewModel>();
        }
    }
}
