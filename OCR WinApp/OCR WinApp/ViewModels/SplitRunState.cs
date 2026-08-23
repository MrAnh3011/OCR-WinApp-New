using System;
using System.Threading;

namespace OCR_WinApp.ViewModels;

internal sealed class SplitRunState
{
    private readonly int _totalFiles;
    private int _completedFiles;
    private int _filesWithOutput;
    private int _isCompleted;
    private int _isCanceled;

    public SplitRunState(int totalFiles)
    {
        if (totalFiles <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalFiles));

        _totalFiles = totalFiles;
    }

    public double ProgressValue =>
        Math.Min(100, (double)Volatile.Read(ref _completedFiles) / _totalFiles * 100);

    public int CompletedFiles => Volatile.Read(ref _completedFiles);

    public bool CanExport =>
        Volatile.Read(ref _isCompleted) == 1 &&
        Volatile.Read(ref _isCanceled) == 0 &&
        Volatile.Read(ref _filesWithOutput) > 0;

    public void MarkFileCompleted(bool hasOutput)
    {
        if (hasOutput)
            Interlocked.Increment(ref _filesWithOutput);

        Interlocked.Increment(ref _completedFiles);
    }

    public void MarkRunCompleted(bool canceled)
    {
        Volatile.Write(ref _isCanceled, canceled ? 1 : 0);
        Volatile.Write(ref _isCompleted, 1);
    }
}
