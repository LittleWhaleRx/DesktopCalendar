using System.Runtime.InteropServices;

namespace DesktopCalendar;

internal static class DesktopHost
{
    private const int SmtoNormal = 0x0000;
    private const uint WorkerWMessage = 0x052C;

    public static bool AttachToDesktop(IntPtr windowHandle)
    {
        var desktopWorker = FindDesktopWorker();
        if (desktopWorker == IntPtr.Zero)
        {
            return false;
        }

        return SetParent(windowHandle, desktopWorker) != IntPtr.Zero;
    }

    public static void Detach(IntPtr windowHandle)
    {
        SetParent(windowHandle, IntPtr.Zero);
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

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

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
