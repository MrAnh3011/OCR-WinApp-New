using OCR.Business.Ai;
using OCR.Business.Auth;
using OCR.Business.Models;
using OCR.Business.Notifications;
using OCR.Business.Split;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

/// <summary>
/// Man "OCR Tach GCN New": dung prompt rieng chi nhan biet GCN/GT/GTK, khong nhan biet parcel_count.
/// </summary>
public partial class TachGcnNewViewModel : TachGcnViewModel
{
    public TachGcnNewViewModel(
        IFolderPickerService folderPicker,
        ISplitGcnService split,
        ISplitRunCacheService cache,
        ISplitCachePromptService cachePrompt,
        IExportResultNotifier notifier,
        IErrorLogService errorLog,
        IGeminiFileApiService geminiFiles,
        IGeminiUploadPipeline uploadPipeline,
        IAuthService auth,
        SplitGcnOptions opt)
        : base(
            folderPicker,
            split,
            cache,
            cachePrompt,
            notifier,
            errorLog,
            geminiFiles,
            uploadPipeline,
            auth,
            opt,
            SplitGcnVariant.New,
            "tach-gcn-new",
            "OCR Tách GCN New",
            "Chọn thư mục chứa PDF gốc → tách thành các bộ GCN/GT/GTK bằng prompt mới. Màn này không tách 1 GCN nhiều thửa thành nhiều file.")
    {
    }
}
