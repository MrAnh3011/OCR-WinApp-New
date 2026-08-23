using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OCR_WinApp.Services;

/// <summary>ContentDialog Fluent nhập mã xã cho cả lô ở màn OCR GCN VietBD.</summary>
public sealed class MaXaPromptService : IMaXaPromptService
{
    public async Task<string?> AskAsync(string? defaultValue = null)
    {
        if (App.MainWindow?.Content?.XamlRoot is null) return null;

        var input = new TextBox
        {
            PlaceholderText = "Ví dụ: 11407",
            Text = defaultValue ?? "",
            SelectionStart = (defaultValue ?? "").Length
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "Giấy chứng nhận không in mã xã của thửa đất nên không thể đọc bằng OCR. "
                   + "Nhập mã xã áp cho toàn bộ lô này; giá trị sẽ được ghi vào cột Mã xã (DDK_maXa) của file Excel.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(input);

        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Title = "Nhập mã xã",
            Content = panel,
            PrimaryButtonText = "Xác nhận",
            CloseButtonText = "Hủy",
            DefaultButton = ContentDialogButton.Primary
        };

        // Nút Xác nhận chỉ bật khi ô nhập có nội dung — mã xã là bắt buộc theo yêu cầu nghiệp vụ.
        dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var value = input.Text?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
