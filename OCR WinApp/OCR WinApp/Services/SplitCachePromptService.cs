using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace OCR_WinApp.Services;

public sealed class SplitCachePromptService : ISplitCachePromptService
{
    public async Task<SplitCacheChoice> AskAsync()
    {
        if (App.MainWindow?.Content?.XamlRoot is null) return SplitCacheChoice.Cancel;

        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = "Dữ liệu quét cũ",
            Content = "Thư mục này đã được quét và vẫn còn dữ liệu cũ. Bạn có muốn sử dụng lại dữ liệu cũ không?",
            PrimaryButtonText = "Có — Dùng dữ liệu cũ",
            SecondaryButtonText = "Không — Quét lại",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Primary
        };

        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => SplitCacheChoice.UseExisting,
            ContentDialogResult.Secondary => SplitCacheChoice.Rescan,
            _ => SplitCacheChoice.Cancel
        };
    }
}
