using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Win32 P/Invoke declarations for hidden desktop management, process launching,
/// window enumeration, and PrintWindow-based screenshot capture.
/// </summary>
internal static class NativeMethods {
    // ── Desktop management ──

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDesktop(
        string lpszDesktop, IntPtr lpszDevice, IntPtr pDevmode,
        uint dwFlags, uint dwDesiredAccess, IntPtr lpsa);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // ── Process creation ──

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    private const uint STILL_ACTIVE = 259;

    // ── Window enumeration ──

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

    // ── Window messaging ──

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CONTEXTMENU = 0x007B;

    /// <summary>
    /// Send WM_CONTEXTMENU to a window. Works on hidden desktops because it uses
    /// the message queue, not input simulation. lParam=-1 indicates keyboard trigger.
    /// </summary>
    public static void SendContextMenuMessage(IntPtr hWnd) {
        SendMessage(hWnd, WM_CONTEXTMENU, hWnd, new IntPtr(-1));
    }

    // ── Window capture ──

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    // ── Structs ──

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT {
        public int Left, Top, Right, Bottom;
    }

    // ── Constants ──

    private const uint GENERIC_ALL = 0x10000000;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    /// <summary>
    /// Close a native handle. Used by callers who receive handles from LaunchOnDesktop.
    /// </summary>
    public static void CloseNativeHandle(IntPtr handle) {
        if (handle != IntPtr.Zero)
            CloseHandle(handle);
    }

    /// <summary>
    /// Get the exit code of a process using its native handle.
    /// Returns null if the process is still running or the call fails.
    /// </summary>
    public static int? GetExitCode(IntPtr processHandle) {
        if (processHandle == IntPtr.Zero)
            return null;
        if (!GetExitCodeProcess(processHandle, out var exitCode))
            return null;
        return exitCode == STILL_ACTIVE ? null : (int)exitCode;
    }

    // ── Hidden Desktop API ──

    /// <summary>
    /// Create a hidden desktop within WinSta0 for running processes with complete
    /// UI isolation from the user's visible desktop.
    /// </summary>
    /// <returns>Desktop handle, or IntPtr.Zero on failure.</returns>
    public static IntPtr CreateHiddenDesktop(string name) {
        return CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, GENERIC_ALL, IntPtr.Zero);
    }

    /// <summary>
    /// Close a hidden desktop handle. Does not affect running processes — they
    /// continue on the desktop until they exit.
    /// </summary>
    public static bool CloseHiddenDesktop(IntPtr hDesktop) {
        return hDesktop != IntPtr.Zero && CloseDesktop(hDesktop);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;

    /// <summary>
    /// Result of launching a process on a hidden desktop.
    /// ProcessHandle must be closed by the caller after obtaining a Process object.
    /// </summary>
    public readonly struct DesktopLaunchResult {
        public int Pid { get; init; }
        public IntPtr ProcessHandle { get; init; }
        public System.IO.StreamReader? Stderr { get; init; }
    }

    /// <summary>
    /// Launch a process on a specific desktop via CreateProcess with stderr piped.
    /// Returns the PID and a readable stderr stream, or pid=-1 on failure.
    /// </summary>
    public static DesktopLaunchResult LaunchOnDesktop(
        string desktopName, string commandLine, string? workingDirectory = null) {

        // Create an inheritable pipe for stderr
        var sa = new SECURITY_ATTRIBUTES {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true
        };

        if (!CreatePipe(out var stderrRead, out var stderrWrite, ref sa, 0))
            return new DesktopLaunchResult { Pid = -1 };

        // Only the write end should be inherited by the child
        SetHandleInformation(stderrRead, HANDLE_FLAG_INHERIT, 0);

        var si = new STARTUPINFO {
            lpDesktop = $"WinSta0\\{desktopName}",
            dwFlags = STARTF_USESTDHANDLES,
            hStdError = stderrWrite,
            hStdOutput = IntPtr.Zero,
            hStdInput = IntPtr.Zero
        };
        si.cb = Marshal.SizeOf<STARTUPINFO>();

        if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero,
                true, 0, IntPtr.Zero, workingDirectory, ref si, out var pi)) {
            CloseHandle(stderrRead);
            CloseHandle(stderrWrite);
            return new DesktopLaunchResult { Pid = -1 };
        }

        CloseHandle(pi.hThread);
        // Close the write end in the parent so reads will EOF when child exits
        CloseHandle(stderrWrite);

        var stderrStream = new System.IO.FileStream(
            new Microsoft.Win32.SafeHandles.SafeFileHandle(stderrRead, ownsHandle: true),
            System.IO.FileAccess.Read);
        var reader = new System.IO.StreamReader(stderrStream);

        // Keep pi.hProcess open — caller must close it after obtaining a Process object.
        // This prevents the process zombie from being cleaned up before we can read ExitCode.
        return new DesktopLaunchResult {
            Pid = (int)pi.dwProcessId,
            ProcessHandle = pi.hProcess,
            Stderr = reader
        };
    }

    /// <summary>
    /// Find the main window HWND for a given PID on a hidden desktop.
    /// Returns IntPtr.Zero if not found.
    /// </summary>
    public static IntPtr FindWindowOnDesktop(IntPtr hDesktop, int pid) {
        IntPtr found = IntPtr.Zero;

        EnumDesktopWindows(hDesktop, (hwnd, lParam) => {
            GetWindowThreadProcessId(hwnd, out uint windowPid);
            if ((int)windowPid == pid) {
                var sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, sb.Capacity);
                if (sb.Length > 0) {
                    found = hwnd;
                    return false; // stop enumeration
                }
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    // ── Desktop Thread Switching ──

    /// <summary>
    /// Save the current thread's desktop handle, switch to the given desktop,
    /// execute an action, then restore the original desktop.
    /// The MCP server is a console app with no windows, so SetThreadDesktop always succeeds.
    /// </summary>
    public static T WithDesktop<T>(IntPtr hDesktop, Func<T> action) {
        var original = GetThreadDesktop(GetCurrentThreadId());
        SetThreadDesktop(hDesktop);
        try {
            return action();
        }
        finally {
            SetThreadDesktop(original);
        }
    }

    /// <summary>
    /// Non-generic overload for void actions.
    /// </summary>
    public static void WithDesktop(IntPtr hDesktop, Action action) {
        var original = GetThreadDesktop(GetCurrentThreadId());
        SetThreadDesktop(hDesktop);
        try {
            action();
        }
        finally {
            SetThreadDesktop(original);
        }
    }

    // ── PrintWindow Capture ──

    /// <summary>
    /// Capture a window to a Bitmap using PrintWindow with PW_RENDERFULLCONTENT.
    /// Works for off-screen windows, occluded windows, and windows on hidden desktops.
    /// </summary>
    public static Bitmap? CaptureWindow(IntPtr hWnd) {
        if (hWnd == IntPtr.Zero)
            return null;
        if (!GetWindowRect(hWnd, out var rect))
            return null;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return null;

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var gfx = Graphics.FromImage(bmp);
        var hdc = gfx.GetHdc();
        try {
            // PW_RENDERFULLCONTENT (flag 2) is required for hidden desktops.
            // Flag 0 (GDI) returns blank on non-active desktops.
            if (!PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT))
                PrintWindow(hWnd, hdc, 0); // fallback for older Windows versions
        }
        finally {
            gfx.ReleaseHdc(hdc);
        }

        return bmp;
    }
}