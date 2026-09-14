using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using OCR.Business.Ai;
using OCR.Business.Auth;
using OCR.Business.BlankPage;
using OCR.Business.Configuration;
using OCR.Business.DocxVbd;
using OCR.Business.Export;
using OCR.Business.IlisUb;
using OCR.Business.Models;
using OCR.Business.Ocr;
using OCR.Business.NewGcn;
using OCR.Business.Notifications;
using OCR.Business.Pdf;
using OCR.Business.Processors;
using OCR.Business.SerialRename;
using OCR.Business.Split;
using OCR.Business.UyBan;
using OCR.Business.VbdBn;
using OCR.Business.VietBdGcn;
using OCR_WinApp.Navigation;
using OCR_WinApp.Services;
using OCR_WinApp.ViewModels;

namespace OCR_WinApp
{
    /// <summary>
    /// Application entry point. Dựng DI host và quản lý cửa sổ chính.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>Cửa sổ chính của ứng dụng (dùng cho picker, theme...).</summary>
        public static Window? MainWindow { get; private set; }

        /// <summary>Service container của ứng dụng.</summary>
        public IServiceProvider Services { get; }

        public static new App Current => (App)Application.Current;

        public App()
        {
            // Anti-tamper: phát hiện debugger / tool reverse-engineering (chỉ Release) trước mọi việc khác.
            OCR_WinApp.Security.IntegrityHelper.Check();

            InitializeComponent();
            Services = ConfigureServices();
            Services.GetRequiredService<IAppExceptionHandlerService>().Initialize(this);
        }

        /// <summary>Lấy một service đã đăng ký. Dùng trong code-behind của View để nạp ViewModel.</summary>
        public static T GetService<T>() where T : class
            => Current.Services.GetRequiredService<T>();

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.AddDebug());

            // Tầng nghiệp vụ (OCR.Business). Mọi cấu hình API/model/OCR do Business đọc (AppSettingsLoader).
            // ─── TẠM TẮT API BACKEND (https://raovatphuly.vn:8080/api) ───────────────────────────────────
            // App KHÔNG gọi mạng cho xác thực & hạn mức: luôn dùng LocalAuthService (nhận mọi tài khoản/mật
            // khẩu không rỗng; RecordOcrCreditAsync trả Success không phát request; CurrentSession.Quota = null
            // nên donut hạn mức ở Trang chủ hiển thị rỗng — HomeViewModel đã có nhánh xử lý null).
            // BẬT LẠI: bỏ comment 5 dòng dưới, xoá dòng AddSingleton<IAuthService, LocalAuthService>() cuối
            // khối này, rồi dán URL vào "Api.BaseUrl" trong appsettings.business.json.
            // var authOptions = AppSettingsLoader.LoadAuthApi();
            // if (string.IsNullOrWhiteSpace(authOptions.BaseUrl))
            //     services.AddSingleton<IAuthService, LocalAuthService>();
            // else
            //     services.AddSingleton<IAuthService>(_ => new ApiAuthService(authOptions));
            services.AddSingleton<IAuthService, LocalAuthService>();
            // ─────────────────────────────────────────────────────────────────────────────────────────────
            services.AddSingleton<IOcrEngine, LocalOcrEngine>();
            services.AddSingleton<IPdfRenderer, PdfRenderer>();
            services.AddSingleton<IPdfRotationNormalizer, PdfRotationNormalizer>();
            services.AddSingleton<IResultExporter, ExcelExporter>();
            services.AddSingleton<IResultExporter, JsonExporter>();
            services.AddSingleton<IDocumentProcessor, PlainTextProcessor>();

            // AI provider DUNG CHUNG (1 profile: Provider + Url + ApiKey + Model) cho Dat Uy Ban / Tach GCN / GCN New.
            services.AddSingleton(AppSettingsLoader.LoadAiProvider());
            services.AddSingleton<IGeminiFileApiService, GeminiFileApiService>();
            services.AddSingleton<IGeminiUploadPipeline, GeminiUploadPipeline>();
            services.AddSingleton<IAiModelClient, AiModelClient>();

            // Báo cáo kết quả nền khi luồng Start của các màn hình AI hoàn tất.
            services.AddSingleton(AppSettingsLoader.LoadExportNotification());
            services.AddSingleton<HttpClient>();
            services.AddSingleton<INotifierErrorLogService, NotifierErrorLogService>();
            services.AddSingleton<IExportResultNotifier, ExportResultNotifier>();

            // Pipeline Đất Uỷ Ban (tách PDF 2 trang → OCR trang 1 → đổi tên → Excel + PDF con).
            services.AddSingleton(AppSettingsLoader.LoadUyBan());
            services.AddSingleton<IUyBanSplitService, UyBanSplitService>();
            services.AddSingleton<IUyBanExtractService, UyBanExtractService>();
            services.AddSingleton<IUyBanExcelExporter, UyBanExcelExporter>();

            // Pipeline Tách GCN (API đọc PDF → cắt PDF theo bộ GCN/GT/GTK).
            services.AddSingleton(AppSettingsLoader.LoadSplitGcn());
            services.AddSingleton<ISplitGcnService, SplitGcnService>();
            services.AddSingleton<ISplitRunCacheService, SplitRunCacheService>();

            // Pipeline OCR GCN iLIS — gửi PDF/ảnh → envelope JSON → Excel_FormMau_v5.
            services.AddSingleton(AppSettingsLoader.LoadNewGcn());
            services.AddSingleton(AppSettingsLoader.LoadGeminiUpload());
            services.AddSingleton(sp => new NewGcnRunCacheService(sp.GetRequiredService<NewGcnOptions>().TempDir));
            services.AddSingleton<INewGcnExtractService, NewGcnExtractService>();
            services.AddSingleton<INewGcnExcelExporter, NewGcnExcelExporter>();

            // Pipeline OCR GCN VietBD — TÁCH RIÊNG hoàn toàn khỏi màn iLIS: prompt, schema, cache,
            // options và exporter đều riêng. Chỉ dùng chung IAiModelClient + luồng upload Gemini.
            services.AddSingleton(AppSettingsLoader.LoadVietBdGcn());
            services.AddSingleton(sp => new VietBdGcnRunCacheService(sp.GetRequiredService<VietBdGcnOptions>().TempDir));
            services.AddSingleton<IVietBdGcnExtractService, VietBdGcnExtractService>();
            services.AddSingleton<IVietBdGcnExcelExporter, VietBdGcnExcelExporter>();

            // Màn OCR GCN VBD-BN — lô GCN TÁI DÙNG extract/prompt/schema VietBD nguyên trạng;
            // lô GTK có luồng CCCD riêng. Cache root riêng vbdbn-temp (không lẫn màn VietBD).
            services.AddSingleton(AppSettingsLoader.LoadVbdBn());
            services.AddSingleton<ICccdExtractService, CccdExtractService>();
            services.AddSingleton<IVbdBnExcelExporter, VbdBnExcelExporter>();
            services.AddSingleton<GcnVbdBnViewModel>(sp => new GcnVbdBnViewModel(
                sp.GetRequiredService<IFolderPickerService>(),
                sp.GetRequiredService<IVietBdGcnExtractService>(),
                sp.GetRequiredService<ICccdExtractService>(),
                sp.GetRequiredService<IVbdBnExcelExporter>(),
                sp.GetRequiredService<IExportResultNotifier>(),
                sp.GetRequiredService<IErrorLogService>(),
                sp.GetRequiredService<IGeminiFileApiService>(),
                sp.GetRequiredService<IGeminiUploadPipeline>(),
                sp.GetRequiredService<IAuthService>(),
                new VietBdGcnRunCacheService(sp.GetRequiredService<VbdBnOptions>().TempDir),
                sp.GetRequiredService<ISplitCachePromptService>(),
                sp.GetRequiredService<IMaXaPromptService>(),
                sp.GetRequiredService<VbdBnOptions>(),
                sp.GetRequiredService<IPdfRenderer>()));

            // Màn "Convert docx to Excel VBD" — parse docx Sổ cấp GCN THUẦN CODE (không AI, không quota),
            // tái dùng nguyên IVietBdGcnExcelExporter + template VietBD đã đăng ký ở trên.
            services.AddSingleton(AppSettingsLoader.LoadDocxVbd());
            services.AddSingleton<IDocxVbdConvertService, DocxVbdConvertService>();
            services.AddSingleton<DocxVbdViewModel>();

            // Màn OCR GCN iLis-UB — DÙNG CHUNG prompt/schema/extract service/exporter Excel/cache JSON
            // với màn iLIS (chỉ khác screenKey khi lấy workspace). Riêng phần dựng cây thư mục kết quả
            // và quy tắc gộp/đổi tên là của riêng màn này.
            services.AddSingleton(AppSettingsLoader.LoadIlisUbGcn());
            services.AddSingleton<IGcnFolderRulesLoader, GcnFolderRulesLoader>();
            services.AddSingleton<IGcnTreeExporter, GcnTreeExporter>();

            // Màn "Xóa trang trắng" — KHÔNG dùng AI, không quota, không cache. Chỉ render trang bằng
            // Windows.Data.Pdf, đo tỉ lệ điểm ảnh có mực rồi ghi lại PDF bằng PdfSharp.
            services.AddSingleton(AppSettingsLoader.LoadBlankPage());
            services.AddSingleton<IBlankPageScanner, BlankPageScanner>();
            services.AddSingleton<IBlankPageTreeExporter, BlankPageTreeExporter>();

            // Màn "Đổi tên theo Serial" — KHÔNG dùng AI: serial đọc bằng OCR offline (IOcrEngine →
            // Windows.Media.Ocr). Bộ dựng cây RIÊNG, không dùng chung với IGcnTreeExporter của iLis-UB.
            services.AddSingleton(AppSettingsLoader.LoadSerialRename());
            services.AddSingleton<ISerialReader, SerialReader>();
            services.AddSingleton<ISerialRenameTreeExporter, SerialRenameTreeExporter>();

            // Dịch vụ tầng App
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddSingleton<IFolderPickerService, FolderPickerService>();
            services.AddSingleton<IErrorLogService, ErrorLogService>();
            services.AddSingleton<IAppExceptionHandlerService, AppExceptionHandlerService>();
            services.AddSingleton<ISplitCachePromptService, SplitCachePromptService>();
            services.AddSingleton<IMaXaPromptService, MaXaPromptService>();
            services.AddSingleton<IBrandService, BrandService>();
            services.AddSingleton<ILoginPreferencesService, LoginPreferencesService>();

            // ViewModels
            services.AddTransient<LoginViewModel>();
            services.AddTransient<ShellViewModel>();
            services.AddTransient<HomeViewModel>();
            // Các màn OCR đăng ký SINGLETON: chuyển màn hình khác vẫn giữ phiên chạy ngầm,
            // quay lại thấy nguyên trạng; chỉ kết thúc khi tắt app.
            services.AddSingleton<DatUyBanViewModel>();
            services.AddSingleton<TachGcnViewModel>();
            services.AddSingleton<TachGcnNewViewModel>();
            services.AddSingleton<TachGtGtkViewModel>();
            services.AddSingleton<GcnNewViewModel>();
            services.AddSingleton<GcnVietBdViewModel>();
            services.AddSingleton<GcnIlisUbViewModel>();
            services.AddSingleton<XoaTrangTrangViewModel>();
            services.AddSingleton<DoiTenSerialViewModel>();
            services.AddTransient<SettingsViewModel>();

            return services.BuildServiceProvider();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Nạp flavor (tông màu/logo/slogan) TRƯỚC khi tạo cửa sổ để nền blur + logo lên đúng.
            var brand = GetService<IBrandService>();
            brand.Initialize();

            // Màu flavor + sắc độ hover/pressed cho nút nhấn dùng tông thương hiệu (vd: nút Đăng nhập).
            var accent = brand.TintColor;
            Resources["FlavorAccentColor"] = accent;
            Resources["FlavorAccentColorHover"] = Darken(accent, 0.92);
            Resources["FlavorAccentColorPressed"] = Darken(accent, 0.83);

            MainWindow = new MainWindow();
            GetService<IThemeSelectorService>().Initialize();
            MainWindow.Activate();
        }

        /// <summary>Làm tối một màu theo hệ số (0..1) để tạo sắc độ hover/pressed.</summary>
        private static Windows.UI.Color Darken(Windows.UI.Color c, double factor) => Windows.UI.Color.FromArgb(
            c.A,
            (byte)(c.R * factor),
            (byte)(c.G * factor),
            (byte)(c.B * factor));
    }
}
