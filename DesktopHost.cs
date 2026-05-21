using System.Runtime.InteropServices;

namespace DesktopCalendar;

internal static class DesktopHost
{
    private const int SmtoNormal = 0x0000;
    private const uint WorkerWMessage = 0x052C;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = 0x80000000L;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly Dictionary<IntPtr, WindowStyleSnapshot> Snapshots = new();

    public static DesktopAttachResult AttachToDesktop(IntPtr windowHandle)
    {
        try
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return DesktopAttachResult.Fail("窗口句柄无效");
            }

            var desktopWorker = FindDesktopWorker();
            if (desktopWorker == IntPtr.Zero)
            {
                return DesktopAttachResult.Fail("没有找到桌面 WorkerW 窗口");
            }

            SaveSnapshot(windowHandle);

            var style = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
            var exStyle = GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
            SetWindowLongPtr(windowHandle, GwlStyle, new IntPtr((style & ~WsPopup) | WsChild));
            SetWindowLongPtr(windowHandle, GwlExStyle, new IntPtr((exStyle & ~WsExAppWindow) | WsExToolWindow));

            SetLastError(0);
            var previousParent = SetParent(windowHandle, desktopWorker);
            var error = Marshal.GetLastWin32Error();

            if (previousParent == IntPtr.Zero && error != 0)
            {
                Restore(windowHandle);
                return DesktopAttachResult.Fail($"SetParent 失败，Win32 错误码 {error}");
            }

            SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
            return DesktopAttachResult.Success();
        }
        catch (Exception ex)
        {
            Restore(windowHandle);
            return DesktopAttachResult.Fail(ex.Message);
        }
    }

    public static void Detach(IntPtr windowHandle)
    {
        Restore(windowHandle);
    }

    private static IntPtr FindDesktopWorker()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        SendMessageTimeout(
            progman,
            WorkerWMessage,
            IntPtr.Zero,
            IntPtr.Zero,
            SmtoNormal,
            1000,
            out _);

        var worker = IntPtr.Zero;
        EnumWindows((topHandle, _) =>
        {
            var shellView = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView == IntPtr.Zero)
            {
                return true;
            }

            worker = FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", null);
            return false;
        }, IntPtr.Zero);

        return worker == IntPtr.Zero ? progman : worker;
    }

    private static void SaveSnapshot(IntPtr windowHandle)
    {
        if (Snapshots.ContainsKey(windowHandle))
        {
            return;
        }

        Snapshots[windowHandle] = new WindowStyleSnapshot(
            GetParent(windowHandle),
            GetWindowLongPtr(windowHandle, GwlStyle),
            GetWindowLongPtr(windowHandle, GwlExStyle));
    }

    private static void Restore(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
        {
            return;
        }

        if (!Snapshots.TryGetValue(windowHandle, out var snapshot))
        {
            SetParent(windowHandle, IntPtr.Zero);
            return;
        }

        SetParent(windowHandle, snapshot.Parent);
        SetWindowLongPtr(windowHandle, GwlStyle, snapshot.Style);
        SetWindowLongPtr(windowHandle, GwlExStyle, snapshot.ExStyle);
        SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        Snapshots.Remove(windowHandle);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private sealed record WindowStyleSnapshot(IntPtr Parent, IntPtr Style, IntPtr ExStyle);

    public sealed record DesktopAttachResult(bool Attached, string Message)
    {
        public static DesktopAttachResult Success()
        {
            return new DesktopAttachResult(true, "已嵌入桌面");
        }

        public static DesktopAttachResult Fail(string message)
        {
            return new DesktopAttachResult(false, message);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void SetLastError(int dwErrCode);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        int flags,
        int timeout,
        out IntPtr result);
}
