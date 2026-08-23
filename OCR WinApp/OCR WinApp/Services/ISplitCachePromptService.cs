using System.Threading.Tasks;

namespace OCR_WinApp.Services;

public enum SplitCacheChoice { UseExisting, Rescan, Cancel }

public interface ISplitCachePromptService
{
    Task<SplitCacheChoice> AskAsync();
}
