using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DesktopCalendar;

internal sealed class WallpaperHost
{
    private const int SpiSetDesktopWallpaper = 0x0014;
    private const int SpiGetDesktopWallpaper = 0x0073;
    private const int SpifUpdateIniFile = 0x01;
    private const int SpifSendWinIniChange = 0x02;
    private readonly string _statePath;
    private readonly string _wallpaperPath;
    private readonly string _backupPath;
    private readonly string _cleanWallpaperPath;

    public WallpaperHost(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        _statePath = Path.Combine(appDataDirectory, "wallpaper-state.json");
        _wallpaperPath = Path.Combine(appDataDirectory, "desktop-calendar-wallpaper.bmp");
        _backupPath = Path.Combine(appDataDirectory, "wallpaper-backup");
        _cleanWallpaperPath = Path.Combine(appDataDirectory, "desktop-calendar-clean-wallpaper.png");
    }

    public bool Apply(Window window, FrameworkElement visual, out string message)
    {
        var prepared = Prepare(window, visual, out message);
        return prepared is not null && ApplyPrepared(prepared, out message);
    }

    internal PreparedWallpaper? Prepare(Window window, FrameworkElement visual, out string message)
    {
        try
        {
            var currentState = CaptureCurrentState();
            var existingState = LoadState();
            var state = ShouldUseExistingState(existingState) ? existingState! : currentState;

            if (!IsTemporaryWallpaper(currentState.WallpaperPath))
            {
                state = currentState;
                BackupWallpaper(state);
            }

            File.WriteAllText(_statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            }));

            visual.UpdateLayout();
            var source = PresentationSource.FromVisual(window);
            var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            var screen = Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            var x = (int)Math.Round(window.Left * transform.M11) - screen.Left;
            var y = (int)Math.Round(window.Top * transform.M22) - screen.Top;

            message = "已准备桌面背景";
            return new PreparedWallpaper(state, RenderVisual(window, visual), screen, x, y);
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return null;
        }
    }

    internal bool ApplyPrepared(PreparedWallpaper prepared, out string message)
    {
        try
        {
            using var wallpaper = RenderWallpaperWithCalendar(prepared);
            wallpaper.Save(_wallpaperPath, ImageFormat.Bmp);
            SetWallpaperStyle("10", "0");

            if (!SetWallpaper(_wallpaperPath))
            {
                message = "桌面背景设置失败";
                return false;
            }

            message = "已渲染到桌面背景，点击托盘图标恢复";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
        finally
        {
            prepared.Dispose();
        }
    }

    public void Restore()
    {
        if (!File.Exists(_statePath))
        {
            return;
        }

        try
        {
            var state = JsonSerializer.Deserialize<WallpaperState>(File.ReadAllText(_statePath));
            if (state is null)
            {
                return;
            }

            var restorePath = ResolveRestorePath(state);
            SetWallpaperStyle(state.WallpaperStyle, state.TileWallpaper);
            if (!string.IsNullOrWhiteSpace(restorePath))
            {
                SetWallpaper(restorePath);
            }
        }
        finally
        {
            TryDelete(_statePath);
            TryDelete(_wallpaperPath);
            TryDelete(_backupPath);
        }
    }

    private WallpaperState? LoadState()
    {
        try
        {
            return File.Exists(_statePath)
                ? JsonSerializer.Deserialize<WallpaperState>(File.ReadAllText(_statePath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldUseExistingState(WallpaperState? state)
    {
        return state is not null && !IsTemporaryWallpaper(state.WallpaperPath);
    }

    private static WallpaperState CaptureCurrentState()
    {
        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return new WallpaperState
        {
            WallpaperPath = GetCurrentWallpaperPath(desktop),
            WallpaperStyle = desktop?.GetValue("WallpaperStyle") as string ?? "10",
            TileWallpaper = desktop?.GetValue("TileWallpaper") as string ?? "0"
        };
    }

    private static string GetCurrentWallpaperPath(RegistryKey? desktop)
    {
        var buffer = new System.Text.StringBuilder(1024);
        if (SystemParametersInfo(SpiGetDesktopWallpaper, buffer.Capacity, buffer, 0) && !string.IsNullOrWhiteSpace(buffer.ToString()))
        {
            return buffer.ToString();
        }

        return desktop?.GetValue("WallPaper") as string ?? string.Empty;
    }

    private void BackupWallpaper(WallpaperState state)
    {
        if (string.IsNullOrWhiteSpace(state.WallpaperPath) || !File.Exists(state.WallpaperPath))
        {
            return;
        }

        try
        {
            File.Copy(state.WallpaperPath, _backupPath, true);
            state.BackupPath = _backupPath;
        }
        catch
        {
            state.BackupPath = string.Empty;
        }
    }

    private string ResolveRestorePath(WallpaperState state)
    {
        if (!string.IsNullOrWhiteSpace(state.BackupPath) && File.Exists(state.BackupPath))
        {
            return state.BackupPath;
        }

        if (!IsTemporaryWallpaper(state.WallpaperPath) && !string.IsNullOrWhiteSpace(state.WallpaperPath) && File.Exists(state.WallpaperPath))
        {
            return state.WallpaperPath;
        }

        return CreateCleanWallpaper();
    }

    private string CreateCleanWallpaper()
    {
        var screen = Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        using var bitmap = new Bitmap(screen.Width, screen.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.FromArgb(16, 20, 24));
        }

        bitmap.Save(_cleanWallpaperPath, ImageFormat.Png);
        return _cleanWallpaperPath;
    }

    private static Bitmap RenderWallpaperWithCalendar(PreparedWallpaper prepared)
    {
        var screen = prepared.Screen;
        var output = new Bitmap(screen.Width, screen.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(System.Drawing.Color.Black);
            DrawExistingWallpaper(graphics, screen, prepared.State);
            graphics.DrawImage(prepared.Calendar, prepared.X, prepared.Y, prepared.Calendar.Width, prepared.Calendar.Height);
        }

        return output;
    }

    private static void DrawExistingWallpaper(Graphics graphics, Rectangle screen, WallpaperState state)
    {
        if (string.IsNullOrWhiteSpace(state.WallpaperPath) || !File.Exists(state.WallpaperPath) || IsTemporaryWallpaper(state.WallpaperPath))
        {
            return;
        }

        try
        {
            using var original = Image.FromFile(state.WallpaperPath);
            var destination = CalculateWallpaperDestination(original, screen, state);
            graphics.DrawImage(original, destination);
        }
        catch
        {
            // Keep a neutral background if the current wallpaper cannot be decoded.
        }
    }

    private static Rectangle CalculateWallpaperDestination(Image image, Rectangle screen, WallpaperState state)
    {
        if (state.WallpaperStyle == "2")
        {
            return new Rectangle(0, 0, screen.Width, screen.Height);
        }

        var scale = state.WallpaperStyle == "6"
            ? Math.Min(screen.Width / (double)image.Width, screen.Height / (double)image.Height)
            : Math.Max(screen.Width / (double)image.Width, screen.Height / (double)image.Height);

        var width = (int)Math.Round(image.Width * scale);
        var height = (int)Math.Round(image.Height * scale);
        return new Rectangle((screen.Width - width) / 2, (screen.Height - height) / 2, width, height);
    }

    private static Bitmap RenderVisual(Window window, FrameworkElement visual)
    {
        visual.UpdateLayout();
        var source = PresentationSource.FromVisual(window);
        var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var width = Math.Max(1, (int)Math.Round(visual.ActualWidth * transform.M11));
        var height = Math.Max(1, (int)Math.Round(visual.ActualHeight * transform.M22));

        var bitmap = new RenderTargetBitmap(width, height, 96 * transform.M11, 96 * transform.M22, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var rendered = Image.FromStream(stream);
        return new Bitmap(rendered);
    }

    private static void SetWallpaperStyle(string style, string tile)
    {
        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
        desktop?.SetValue("WallpaperStyle", style);
        desktop?.SetValue("TileWallpaper", tile);
    }

    private static bool SetWallpaper(string path)
    {
        return SystemParametersInfo(SpiSetDesktopWallpaper, 0, path, SpifUpdateIniFile | SpifSendWinIniChange);
    }

    private static bool IsTemporaryWallpaper(string path)
    {
        var fileName = Path.GetFileName(path);
        return !string.IsNullOrWhiteSpace(path)
            && (fileName.Equals("desktop-calendar-wallpaper.png", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("desktop-calendar-wallpaper.bmp", StringComparison.OrdinalIgnoreCase));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, string pvParam, int fWinIni);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, System.Text.StringBuilder pvParam, int fWinIni);

    internal sealed class WallpaperState
    {
        public string WallpaperPath { get; set; } = string.Empty;
        public string WallpaperStyle { get; set; } = "10";
        public string TileWallpaper { get; set; } = "0";
        public string BackupPath { get; set; } = string.Empty;
    }

    internal sealed class PreparedWallpaper : IDisposable
    {
        public PreparedWallpaper(WallpaperState state, Bitmap calendar, Rectangle screen, int x, int y)
        {
            State = state;
            Calendar = calendar;
            Screen = screen;
            X = x;
            Y = y;
        }

        public WallpaperState State { get; }
        public Bitmap Calendar { get; }
        public Rectangle Screen { get; }
        public int X { get; }
        public int Y { get; }

        public void Dispose()
        {
            Calendar.Dispose();
        }
    }
}
