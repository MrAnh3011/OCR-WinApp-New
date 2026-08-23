using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OCR_WinApp.Security;

/// <summary>
/// Kiểm tra toàn vẹn khi khởi động (port từ GcnOcrApp.IntegrityHelper):
///   1. Phát hiện debugger đang attach.
///   2. Phát hiện tool reverse-engineering đang chạy.
/// Vi phạm → FailFast (không thể bị catch). Chỉ chạy ở Release (#if !DEBUG).
/// </summary>
internal static class IntegrityHelper
{
    private static readonly string[] s_badProcesses =
    {
        "dnspy", "dnspy-x86", "ilspy", "justdecompile",
        "de4dot", "de4dot-x64",
        "x64dbg", "x32dbg", "ollydbg", "windbg",
        "ida", "ida64", "idaq", "idaq64",
        "procmon", "procmon64",
        "wireshark",
        "cheatengine-x86_64", "cheatengine-i386",
        "fiddler", "httpdebuggerui",
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Check()
    {
#if !DEBUG
        CheckDebugger();
        CheckProcesses();
#endif
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckDebugger()
    {
        if (Debugger.IsAttached) Terminate();
        try { if (NativeMethods.IsDebuggerPresent()) Terminate(); } catch { }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckProcesses()
    {
        try
        {
            var running = Process.GetProcesses()
                                 .Select(p => { try { return p.ProcessName.ToLowerInvariant(); } catch { return ""; } })
                                 .ToHashSet();
            foreach (var bad in s_badProcesses)
                if (running.Contains(bad)) Terminate();
        }
        catch { /* không để lỗi chặn startup */ }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Terminate() => Environment.FailFast(null);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsDebuggerPresent();
    }
}
