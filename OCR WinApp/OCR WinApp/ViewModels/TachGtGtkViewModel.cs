using OCR.Business.Ai;
using OCR.Business.Auth;
using OCR.Business.Models;
using OCR.Business.Notifications;
using OCR.Business.Split;
using OCR_WinApp.Services;

namespace OCR_WinApp.ViewModels;

/// <summary>
/// Man "OCR Tach GT/GTK": xu ly PDF khong co GCN, dat ten theo so to/so thua lay tu don dau ho so.
/// </summary>
public partial class TachGtGtkViewModel : TachGcnViewModel
{
    public TachGtGtkViewModel(
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
            SplitGcnVariant.NoGcn,
            "tach-gt-gtk",
            "OCR Tách GT/GTK Không GCN",
            "Chọn thư mục chứa PDF không có GCN → nhận biết đơn đầu hồ sơ, lấy số tờ/số thửa ở mục 3 và tách thành GT/GTK.",
            copyGcnToOcrOnExport: false)
    {
    }
}
