using System.Runtime.InteropServices;

namespace DesktopCalendar;

internal static class DesktopHost
{
    private const int GwlExStyle = -20;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly Dictionary<IntPtr, WindowStyleSnapshot> Snapshots = new();

    public static DesktopAttachResult AttachToDesktop(IntPtr windowHandle)
    {
        try
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return DesktopAttachResult.Fail("Invalid window handle");
            }

            SaveSnapshot(windowHandle);

            var exStyle = GetWindowLongPtr(windowHandle, GwlExStyle).ToInt64();
            SetWindowLongPtr(windowHandle, GwlExStyle, new IntPtr((exStyle & ~WsExAppWindow) | WsExToolWindow));
            SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
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
            SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            Snapshots.Remove(windowHandle);
        }
    }

    private sealed record WindowStyleSnapshot(IntPtr Parent, IntPtr ExStyle);

    public sealed record DesktopAttachResult(bool Attached, string Message)
    {
        public static DesktopAttachResult Success()
        {
            return new DesktopAttachResult(true, "Pinned to desktop");
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
}
