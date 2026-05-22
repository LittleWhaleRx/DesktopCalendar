using System.Runtime.InteropServices;

namespace DesktopCalendar;

internal static class DesktopHost
{
    private const int GwlExStyle = -20;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const int SwShow = 5;
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly Dictionary<IntPtr, WindowStyleSnapshot> Snapshots = new();

    public static DesktopAttachResult AttachToDesktop(IntPtr windowHandle)
    {
        try
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return DesktopAttachResult.Fail("窗口无效");
            }

            var desktopHost = FindDesktopHost();
            if (desktopHost.Parent == IntPtr.Zero)
            {
                return DesktopAttachResult.Fail("没有找到桌面窗口");
            }

            SaveSnapshot(windowHandle);

            SetParent(windowHandle, desktopHost.Parent);

            var exStyle = GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
            var passiveDesktopStyle = (exStyle & ~WsExAppWindow) | WsExToolWindow | WsExTransparent | WsExNoActivate;
            SetWindowLongPtr(windowHandle, GwlExStyle, new IntPtr(passiveDesktopStyle));

            ShowWindow(windowHandle, SwShow);
            SetWindowPos(
                windowHandle,
                desktopHost.InsertAfter,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged | SwpShowWindow);

            return DesktopAttachResult.Success(desktopHost.Message);
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

    private static DesktopHostWindow FindDesktopHost()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return new DesktopHostWindow(IntPtr.Zero, HwndBottom, "没有找到桌面窗口");
        }

        var shellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (shellView != IntPtr.Zero)
        {
            return new DesktopHostWindow(progman, shellView, "已放到桌面图标下方");
        }

        return new DesktopHostWindow(progman, HwndBottom, "已放到桌面底层");
    }

    private static void SaveSnapshot(IntPtr windowHandle)
    {
        if (Snapshots.ContainsKey(windowHandle))
        {
            return;
        }

        Snapshots[windowHandle] = new WindowStyleSnapshot(
            GetParent(windowHandle),
            GetWindowLongPtr(windowHandle, GwlExStyle));
    }

    private static void Restore(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
        {
            return;
        }

        if (Snapshots.TryGetValue(windowHandle, out var snapshot))
        {
            SetParent(windowHandle, snapshot.Parent);
            SetWindowLongPtr(windowHandle, GwlExStyle, snapshot.ExStyle);
            ShowWindow(windowHandle, SwShow);
            SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
            Snapshots.Remove(windowHandle);
        }
    }

    private sealed record WindowStyleSnapshot(IntPtr Parent, IntPtr ExStyle);

    private sealed record DesktopHostWindow(IntPtr Parent, IntPtr InsertAfter, string Message);

    public sealed record DesktopAttachResult(bool Attached, string Message)
    {
        public static DesktopAttachResult Success(string message)
        {
            return new DesktopAttachResult(true, message);
        }

        public static DesktopAttachResult Fail(string message)
        {
            return new DesktopAttachResult(false, message);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

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
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
