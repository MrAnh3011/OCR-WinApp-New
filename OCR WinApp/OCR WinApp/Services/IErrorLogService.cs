using System;

namespace OCR_WinApp.Services;

public interface IErrorLogService
{
    void LogException(string screenKey, string source, Exception exception);
}
