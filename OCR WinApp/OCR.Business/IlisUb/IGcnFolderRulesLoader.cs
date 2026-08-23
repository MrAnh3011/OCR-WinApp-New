namespace OCR.Business.IlisUb;

/// <summary>Đọc + kiểm tra file JSON quy tắc gộp/đổi tên của màn OCR GCN iLis-UB.</summary>
public interface IGcnFolderRulesLoader
{
    /// <param name="rulesPath">Đường dẫn tuyệt đối, hoặc tương đối thư mục exe.</param>
    /// <exception cref="System.InvalidOperationException">File thiếu, JSON sai, hoặc cấu hình không hợp lệ.</exception>
    GcnFolderRules Load(string rulesPath);
}
