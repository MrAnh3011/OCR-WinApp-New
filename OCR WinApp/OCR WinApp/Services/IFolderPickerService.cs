using System.Collections.Generic;
using System.Threading.Tasks;

namespace OCR_WinApp.Services;

/// <summary>Mở hộp thoại chọn thư mục/tệp (gắn với cửa sổ chính). Trả về null/rỗng nếu hủy.</summary>
public interface IFolderPickerService
{
    Task<string?> PickFolderAsync();

    /// <summary>Liệt kê các tệp có phần mở rộng cho phép trong thư mục, có thể gồm toàn bộ thư mục con.</summary>
    IReadOnlyList<string> EnumerateFiles(string folder, IEnumerable<string> extensions, bool recursive);

    /// <summary>Chọn nhiều tệp theo phần mở rộng cho phép (ví dụ ".pdf"). Trả về danh sách đường dẫn.</summary>
    Task<IReadOnlyList<string>> PickFilesAsync(IEnumerable<string> extensions);

    /// <summary>Mở thư mục bằng Explorer. Nuốt lỗi để không làm hỏng luồng gọi.</summary>
    void OpenFolder(string path);
}
