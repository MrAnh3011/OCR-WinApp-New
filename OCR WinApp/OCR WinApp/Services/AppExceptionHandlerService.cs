using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace OCR_WinApp.Services;

public sealed class AppExceptionHandlerService : IAppExceptionHandlerService
{
    private readonly IErrorLogService _errorLog;
    private int _initialized;

    public AppExceptionHandlerService(IErrorLogService errorLog)
    {
        _errorLog = errorLog;
    }

    public void Initialize(Application app)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;

        app.UnhandledException += OnApplicationUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
    }

    private void OnApplicationUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _errorLog.LogException("app", "Application.UnhandledException", e.Exception);
    }

    private void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _errorLog.LogException("app", "AppDomain.CurrentDomain.UnhandledException", exception);
        else
            _errorLog.LogException(
                "app",
                "AppDomain.CurrentDomain.UnhandledException",
                new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception."));
    }

    private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _errorLog.LogException("app", "TaskScheduler.UnobservedTaskException", e.Exception);
    }
}
