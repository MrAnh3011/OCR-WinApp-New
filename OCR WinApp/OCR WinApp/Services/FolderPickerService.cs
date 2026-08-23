using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace OCR_WinApp.Services;

public sealed class FolderPickerService : IFolderPickerService
{
    public IReadOnlyList<string> EnumerateFiles(string folder, IEnumerable<string> extensions, bool recursive) =>
        FolderFileEnumerator.Enumerate(folder, extensions, recursive);

    public async Task<string?> PickFolderAsync()
    {
        if (App.MainWindow is null) return null;

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow(picker);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(IEnumerable<string> extensions)
    {
        if (App.MainWindow is null) return Array.Empty<string>();

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        var exts = extensions.Select(e => e.StartsWith('.') ? e : "." + e).ToList();
        if (exts.Count == 0) picker.FileTypeFilter.Add("*");
        else foreach (var e in exts) picker.FileTypeFilter.Add(e);

        InitializeWithWindow(picker);

        var files = await picker.PickMultipleFilesAsync();
        return files.Select(f => f.Path).ToList();
    }

    // WinUI 3 desktop: picker phải gắn với HWND của cửa sổ chính.
    private static void InitializeWithWindow(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Không mở được thư mục không phải lỗi nghiệp vụ — bỏ qua.
        }
    }
}
