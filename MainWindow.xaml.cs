using System.Globalization;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Microsoft.Win32;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;
using WpfSystemColors = System.Windows.SystemColors;

namespace DesktopCalendar;

public partial class MainWindow : Window
{
    private static readonly CultureInfo ZhCn = CultureInfo.GetCultureInfo("zh-CN");
    private const string StartupRegistryName = "DesktopCalendar";
    private readonly string _appDataDirectory;
    private readonly string _dataPath;
    private readonly WallpaperHost _wallpaperHost;
    private CalendarStore _store = new();
    private CalendarTheme _theme = CalendarTheme.Default;
    private DateTime _currentMonth;
    private DateTime _selectedDate;
    private bool _isLoading;
    private bool _isLoaded;
    private bool _isRenderingCalendar;
    private bool _isDesktopMode;
    private bool _isFormattingNote;
    private IntPtr _windowHandle;
    private Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();

        _appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopCalendar");
        _dataPath = Path.Combine(_appDataDirectory, "calendar-data.json");
        _wallpaperHost = new WallpaperHost(_appDataDirectory);

        _selectedDate = DateTime.Today;
        _currentMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;
        _store = CalendarStore.Load(_dataPath);
        RestoreWindowPlacement();

        ThemeCombo.ItemsSource = CalendarTheme.All.Select(theme => theme.DisplayName).ToList();
        _theme = CalendarTheme.Find(_store.Ui.ThemeName);
        ThemeCombo.SelectedIndex = CalendarTheme.IndexOf(_theme.Name);
        StartupCheck.IsChecked = IsStartupEnabled();
        OpacitySlider.Value = _store.Ui.OpacityPercent;
        _store.Ui.DesktopMode = false;

        _isLoading = false;
        _isLoaded = true;

        ApplyTheme();
        ApplyOpacity();
        RenderCalendar();
        StatusText.Text = "可直接在日期格子里输入记录";

        CalendarStore.Save(_dataPath, _store);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ExitDesktopMode();
        SaveWindowPlacement();
        CalendarStore.Save(_dataPath, _store);
        DisposeTrayIcon();
    }

    private void Window_PositionChanged(object sender, EventArgs e)
    {
        if (!_isLoaded || _isLoading || _isDesktopMode)
        {
            return;
        }

        SaveWindowPlacement();
        CalendarStore.Save(_dataPath, _store);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 || _isDesktopMode)
        {
            return;
        }

        DragMove();
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        _currentMonth = _currentMonth.AddMonths(-1);
        RenderCalendar();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _currentMonth = _currentMonth.AddMonths(1);
        RenderCalendar();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _selectedDate = DateTime.Today;
        _currentMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
        RenderCalendar();
    }

    private void DesktopModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isDesktopMode)
        {
            TryEnterDesktopMode();
        }
    }

    private void StartupCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        SetStartupEnabled(StartupCheck.IsChecked == true);
        StatusText.Text = StartupCheck.IsChecked == true ? "已开启开机自启" : "已关闭开机自启";
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var isEnter = e.Key == Key.Enter || (e.Key == Key.System && e.SystemKey == Key.Enter);
        if (!_isDesktopMode && isEnter && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            TryEnterDesktopMode();
            e.Handled = true;
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || !_isLoaded || ThemeCombo.SelectedIndex < 0)
        {
            return;
        }

        _theme = CalendarTheme.All[Math.Clamp(ThemeCombo.SelectedIndex, 0, CalendarTheme.All.Count - 1)];
        _store.Ui.ThemeName = _theme.Name;
        ApplyTheme();
        ApplyOpacity();
        RenderCalendar();
        CalendarStore.Save(_dataPath, _store);
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isLoaded)
        {
            return;
        }

        ApplyOpacity();
        if (!_isLoading)
        {
            _store.Ui.OpacityPercent = OpacitySlider.Value;
            CalendarStore.Save(_dataPath, _store);
        }
    }

    private void RenderCalendar()
    {
        _isRenderingCalendar = true;
        MonthTitle.Text = _currentMonth.ToString("yyyy年M月", ZhCn);
        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();

        for (var column = 0; column < 7; column++)
        {
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = 92
            });
        }

        for (var row = 0; row < 6; row++)
        {
            CalendarGrid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto,
                MinHeight = 104
            });
        }

        var firstDay = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
        var startDate = firstDay.AddDays(-(int)firstDay.DayOfWeek);

        for (var i = 0; i < 42; i++)
        {
            var day = startDate.AddDays(i);
            var cell = CreateDayCell(day);
            Grid.SetRow(cell, i / 7);
            Grid.SetColumn(cell, i % 7);
            CalendarGrid.Children.Add(cell);
        }

        _isRenderingCalendar = false;
    }

    private Border CreateDayCell(DateTime day)
    {
        var isCurrentMonth = day.Month == _currentMonth.Month;
        var isToday = day.Date == DateTime.Today;
        var isSelected = day.Date == _selectedDate.Date;

        var holiday = HolidayCalendar.Get(day);
        var dateText = new TextBlock
        {
            Text = isToday
                ? $"{day.Day.ToString(CultureInfo.InvariantCulture)} 今天"
                : day.Day.ToString(CultureInfo.InvariantCulture),
            FontWeight = isToday || isSelected ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = 15,
            Foreground = isCurrentMonth ? _theme.TextBrush : _theme.MutedBrush
        };

        var dateLine = new Grid();
        dateLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dateLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        dateLine.Children.Add(dateText);

        if (holiday is not null)
        {
            var badge = new TextBlock
            {
                Text = holiday.IsWorkday ? "班" : "休",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = holiday.IsWorkday ? _theme.TextBrush : _theme.HighlightTextBrush,
                Background = holiday.IsWorkday ? _theme.BorderBrush : _theme.AccentBrush,
                Padding = new Thickness(5, 1, 5, 1),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 1);
            dateLine.Children.Add(badge);
        }

        var lunarText = new TextBlock
        {
            Text = holiday?.Name ?? LunarCalendarText.GetDayText(day),
            FontSize = 11,
            Foreground = holiday is null ? _theme.MutedBrush : _theme.AccentBrush,
            Margin = new Thickness(0, 2, 0, 2),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var noteBox = new WpfRichTextBox
        {
            Style = (Style)FindResource("DayNoteBoxStyle"),
            Document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            },
            Tag = day.Date,
            IsEnabled = !_isDesktopMode,
            Opacity = isCurrentMonth ? 1 : 0.72,
            Foreground = _theme.TextBrush,
            CaretBrush = _theme.TextBrush,
            MinHeight = 58,
            MinWidth = 72
        };
        SetNoteText(noteBox, GetDayNote(day));
        noteBox.TextChanged += DayNoteBox_TextChanged;
        noteBox.GotKeyboardFocus += DayNoteBox_GotKeyboardFocus;
        noteBox.PreviewKeyDown += DayNoteBox_PreviewKeyDown;
        noteBox.PreviewTextInput += DayNoteBox_PreviewTextInput;
        noteBox.LostKeyboardFocus += DayNoteBox_LostKeyboardFocus;

        var content = new StackPanel
        {
            Margin = new Thickness(8, 6, 8, 6)
        };
        content.Children.Add(dateLine);
        content.Children.Add(lunarText);
        content.Children.Add(noteBox);

        var cell = new Border
        {
            Tag = day.Date,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            MinHeight = 104,
            Background = BuildDayBackground(isToday, isSelected),
            BorderBrush = isSelected ? _theme.AccentBrush : _theme.BorderBrush,
            Opacity = isCurrentMonth ? 1 : 0.42,
            Child = content
        };
        cell.MouseLeftButtonDown += DayCell_MouseLeftButtonDown;
        return cell;
    }

    private void DayCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDesktopMode || sender is not Border { Tag: DateTime day })
        {
            return;
        }

        if (_selectedDate.Date == day.Date)
        {
            return;
        }

        _selectedDate = day.Date;
        if (day.Month != _currentMonth.Month || day.Year != _currentMonth.Year)
        {
            _currentMonth = new DateTime(day.Year, day.Month, 1);
        }

        RenderCalendar();
    }

    private void DayNoteBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is WpfRichTextBox { Tag: DateTime day })
        {
            _selectedDate = day.Date;
        }
    }

    private void DayNoteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isRenderingCalendar || _isLoading || _isFormattingNote || sender is not WpfRichTextBox { Tag: DateTime day } noteBox)
        {
            return;
        }

        SetDayNote(day, GetNoteText(noteBox));
        CalendarStore.Save(_dataPath, _store);
        StatusText.Text = $"{day:yyyy-MM-dd} 已保存";
    }

    private void DayNoteBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not WpfRichTextBox noteBox || !string.IsNullOrEmpty(GetNoteText(noteBox)) || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        InsertNoteText(noteBox, "- " + e.Text);
        e.Handled = true;
    }

    private void DayNoteBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var isEnter = e.Key == Key.Enter || (e.Key == Key.System && e.SystemKey == Key.Enter);
        if (sender is not WpfRichTextBox noteBox || !isEnter)
        {
            return;
        }

        var prefix = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt
            ? Environment.NewLine + "- "
            : Environment.NewLine + "  · ";
        InsertNoteText(noteBox, prefix);
        e.Handled = true;
    }

    private void DayNoteBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not WpfRichTextBox noteBox)
        {
            return;
        }

        var text = GetNoteText(noteBox);
        var trimmed = text.Trim();
        if (trimmed is "-" or "·")
        {
            SetNoteText(noteBox, string.Empty);
            return;
        }

        SetNoteText(noteBox, text);
    }

    private void InsertNoteText(WpfRichTextBox noteBox, string text)
    {
        var current = GetNoteText(noteBox);
        var selectionStart = GetCaretOffset(noteBox);
        var selectionLength = new TextRange(noteBox.Selection.Start, noteBox.Selection.End).Text.Length;
        var before = current[..Math.Min(selectionStart, current.Length)];
        var afterIndex = Math.Min(selectionStart + selectionLength, current.Length);
        var after = current[afterIndex..];
        var updated = before + text + after;
        SetNoteText(noteBox, updated, selectionStart + text.Length);
        if (noteBox.Tag is DateTime day)
        {
            SetDayNote(day, updated);
            CalendarStore.Save(_dataPath, _store);
        }
    }

    private void SetNoteText(WpfRichTextBox noteBox, string text, int? caretOffset = null)
    {
        _isFormattingNote = true;
        noteBox.Document.Blocks.Clear();

        var lines = NormalizeNoteText(text)
            .Split(Environment.NewLine, StringSplitOptions.None);

        if (lines.Length == 0)
        {
            lines = [string.Empty];
        }

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            var isMain = trimmed.StartsWith("-", StringComparison.Ordinal);
            var isSub = trimmed.StartsWith("·", StringComparison.Ordinal) || line.StartsWith("  ", StringComparison.Ordinal);
            var paragraph = new Paragraph(new Run(line))
            {
                Margin = new Thickness(isSub ? 12 : 0, 0, 0, 0),
                Padding = new Thickness(0),
                FontSize = isMain ? 13.5 : 12,
                FontWeight = isMain ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isMain ? _theme.TextBrush : _theme.MutedBrush,
                LineHeight = isMain ? 18 : 16
            };
            noteBox.Document.Blocks.Add(paragraph);
        }

        noteBox.CaretPosition = GetTextPointerAtOffset(noteBox.Document, caretOffset ?? GetNoteText(noteBox).Length);
        _isFormattingNote = false;
    }

    private static string GetNoteText(WpfRichTextBox noteBox)
    {
        var text = new TextRange(noteBox.Document.ContentStart, noteBox.Document.ContentEnd).Text;
        return NormalizeNoteText(text);
    }

    private static string NormalizeNoteText(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n')
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }

    private static int GetCaretOffset(WpfRichTextBox noteBox)
    {
        return NormalizeNoteText(new TextRange(noteBox.Document.ContentStart, noteBox.CaretPosition).Text).Length;
    }

    private static TextPointer GetTextPointerAtOffset(FlowDocument document, int offset)
    {
        var navigator = document.ContentStart;
        TextPointer? previous = navigator;
        while (navigator is not null)
        {
            var currentOffset = NormalizeNoteText(new TextRange(document.ContentStart, navigator).Text).Length;
            if (currentOffset >= offset)
            {
                return navigator;
            }

            previous = navigator;
            navigator = navigator.GetNextInsertionPosition(LogicalDirection.Forward);
        }

        return previous ?? document.ContentEnd;
    }

    private string GetDayNote(DateTime day)
    {
        return _store.Days.TryGetValue(DayKey(day), out var record) ? record.Note : string.Empty;
    }

    private void SetDayNote(DateTime day, string note)
    {
        var key = DayKey(day);
        if (string.IsNullOrWhiteSpace(note))
        {
            _store.Days.Remove(key);
            return;
        }

        _store.Days[key] = new DayRecord
        {
            Note = note
        };
    }

    private MediaBrush BuildDayBackground(bool isToday, bool isSelected)
    {
        if (isSelected)
        {
            return _theme.SelectedBrush;
        }

        if (isToday)
        {
            return _theme.TodayBrush;
        }

        return _theme.DayBrush;
    }

    private void ApplyOpacity()
    {
        var alpha = (byte)Math.Clamp(OpacitySlider.Value / 100d * 255d, 130d, 245d);
        RootChrome.Background = new SolidColorBrush(MediaColor.FromArgb(alpha, _theme.Surface.R, _theme.Surface.G, _theme.Surface.B));
    }

    private void ApplyTheme()
    {
        Foreground = _theme.TextBrush;
        RootChrome.BorderBrush = _theme.BorderBrush;
        MonthTitle.Foreground = _theme.TextBrush;
        StatusText.Foreground = _theme.MutedBrush;
        ThemeCombo.Foreground = _theme.TextBrush;
        ThemeCombo.Background = _theme.ButtonBrush;
        ThemeCombo.BorderBrush = _theme.BorderBrush;
        ThemeCombo.Resources[WpfSystemColors.WindowBrushKey] = _theme.PopupBrush;
        ThemeCombo.Resources[WpfSystemColors.ControlBrushKey] = _theme.PopupBrush;
        ThemeCombo.Resources[WpfSystemColors.HighlightBrushKey] = _theme.AccentBrush;
        ThemeCombo.Resources[WpfSystemColors.HighlightTextBrushKey] = _theme.HighlightTextBrush;
        ThemeCombo.ItemContainerStyle = CreateThemeComboItemStyle();
        StartupCheck.Foreground = _theme.TextBrush;

        foreach (var button in FindVisualChildren<WpfButton>(RootChrome))
        {
            button.Foreground = _theme.TextBrush;
            button.Background = _theme.ButtonBrush;
            button.BorderBrush = _theme.BorderBrush;
        }

        DesktopModeButton.Foreground = _theme.TextBrush;
        DesktopModeButton.Background = _theme.ButtonBrush;
        DesktopModeButton.BorderBrush = _theme.AccentBrush;
    }

    private void TryEnterDesktopMode()
    {
        SaveWindowPlacement();
        CalendarStore.Save(_dataPath, _store);

        var prepared = _wallpaperHost.Prepare(this, RootChrome, out var message);
        if (prepared is null)
        {
            StatusText.Text = $"{message}，仍保持可操作窗口";
            return;
        }

        _isDesktopMode = true;
        _store.Ui.DesktopMode = true;
        ShowInTaskbar = false;
        DesktopModeButton.Content = "桌面模式中";
        StatusText.Text = "正在生成桌面背景...";
        EnsureTrayIcon();
        Hide();

        Task.Run(() =>
        {
            var ok = _wallpaperHost.ApplyPrepared(prepared, out var applyMessage);
            return (ok, applyMessage);
        }).ContinueWith(task =>
        {
            Dispatcher.Invoke(() =>
            {
                if (!task.Result.ok)
                {
                    _isDesktopMode = false;
                    _store.Ui.DesktopMode = false;
                    ShowInTaskbar = true;
                    DesktopModeButton.Content = "桌面模式";
                    DisposeTrayIcon();
                    Show();
                    RestoreToFront();
                }

                StatusText.Text = task.Result.applyMessage;
            });
        });
    }

    private void ExitDesktopMode()
    {
        if (!_isDesktopMode)
        {
            return;
        }

        _isDesktopMode = false;
        _store.Ui.DesktopMode = false;
        ShowInTaskbar = true;
        DesktopModeButton.Content = "桌面模式";
        StatusText.Text = "正在恢复桌面背景...";
        DisposeTrayIcon();
        Show();
        RestoreToFront();
        RenderCalendar();

        Task.Run(_wallpaperHost.Restore).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() => StatusText.Text = "可直接在日期格子里输入记录");
        });
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("恢复可操作模式", null, (_, _) => Dispatcher.Invoke(ExitDesktopMode));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "桌面台历 - 点击恢复",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.MouseClick += TrayIcon_MouseClick;
    }

    private void TrayIcon_MouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            Dispatcher.Invoke(ExitDesktopMode);
        }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.MouseClick -= TrayIcon_MouseClick;
        _trayIcon.Visible = false;
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.Icon?.Dispose();
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var background = new SolidBrush(System.Drawing.Color.FromArgb(39, 129, 204)))
        using (var header = new SolidBrush(System.Drawing.Color.FromArgb(255, 255, 255)))
        using (var paper = new SolidBrush(System.Drawing.Color.FromArgb(246, 251, 255)))
        using (var accent = new SolidBrush(System.Drawing.Color.FromArgb(255, 105, 92)))
        using (var linePen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(74, 91, 112), 2))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            graphics.FillRectangle(background, new Rectangle(2, 2, 28, 28));
            graphics.FillRectangle(paper, new Rectangle(5, 7, 22, 20));
            graphics.FillRectangle(accent, new Rectangle(5, 7, 22, 5));
            graphics.FillEllipse(header, 9, 4, 4, 7);
            graphics.FillEllipse(header, 19, 4, 4, 7);
            graphics.DrawLine(linePen, 9, 17, 23, 17);
            graphics.DrawLine(linePen, 9, 22, 19, 22);
        }

        var iconHandle = bitmap.GetHicon();
        var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(iconHandle).Clone();
        DestroyIcon(iconHandle);
        bitmap.Dispose();
        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private void RestoreToFront()
    {
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue(StartupRegistryName) is string value && value.Contains(GetExecutablePath(), StringComparison.OrdinalIgnoreCase);
    }

    private static void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(StartupRegistryName, $"\"{GetExecutablePath()}\"");
        }
        else
        {
            key.DeleteValue(StartupRegistryName, false);
        }
    }

    private static string GetExecutablePath()
    {
        return Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "DesktopCalendar.exe");
    }

    private void RestoreWindowPlacement()
    {
        var placement = _store.Ui.Window;
        if (IsUsableNumber(placement.Width) && IsUsableNumber(placement.Height) && placement.Width >= MinWidth && placement.Height >= MinHeight)
        {
            Width = placement.Width;
            Height = placement.Height;
        }

        if (IsUsableNumber(placement.Left) && IsUsableNumber(placement.Top) && placement.Left > -10000 && placement.Top > -10000)
        {
            Left = placement.Left;
            Top = placement.Top;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 42;
            Top = SystemParameters.WorkArea.Top + 42;
        }

        SaveWindowPlacement();
    }

    private void SaveWindowPlacement()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        if (!IsUsableNumber(Left) || !IsUsableNumber(Top) || !IsUsableNumber(Width) || !IsUsableNumber(Height))
        {
            return;
        }

        _store.Ui.Window.Left = Left;
        _store.Ui.Window.Top = Top;
        _store.Ui.Window.Width = Width;
        _store.Ui.Window.Height = Height;
    }

    private Style CreateThemeComboItemStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(ForegroundProperty, _theme.TextBrush));
        style.Setters.Add(new Setter(WpfControl.BackgroundProperty, _theme.PopupBrush));
        style.Setters.Add(new Setter(WpfControl.PaddingProperty, new Thickness(10, 5, 10, 5)));
        style.Setters.Add(new Setter(WpfControl.MinHeightProperty, 28d));

        var selectedTrigger = new Trigger
        {
            Property = ComboBoxItem.IsSelectedProperty,
            Value = true
        };
        selectedTrigger.Setters.Add(new Setter(WpfControl.BackgroundProperty, _theme.AccentBrush));
        selectedTrigger.Setters.Add(new Setter(ForegroundProperty, _theme.HighlightTextBrush));
        style.Triggers.Add(selectedTrigger);

        var hoverTrigger = new Trigger
        {
            Property = ComboBoxItem.IsMouseOverProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(WpfControl.BackgroundProperty, _theme.SelectedBrush));
        hoverTrigger.Setters.Add(new Setter(ForegroundProperty, _theme.TextBrush));
        style.Triggers.Add(hoverTrigger);

        return style;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static string DayKey(DateTime day)
    {
        return day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static bool IsUsableNumber(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public sealed class CalendarStore
{
    public Dictionary<string, DayRecord> Days { get; set; } = new();
    public UiSettings Ui { get; set; } = new();

    public static CalendarStore Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new CalendarStore();
            }

            var json = File.ReadAllText(path);
            var store = JsonSerializer.Deserialize<CalendarStore>(json) ?? new CalendarStore();
            Sanitize(store);
            return store;
        }
        catch
        {
            return new CalendarStore();
        }
    }

    public static void Save(string path, CalendarStore store)
    {
        Sanitize(store);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    private static void Sanitize(CalendarStore store)
    {
        store.Ui ??= new UiSettings();
        store.Ui.Window ??= new WidgetWindowPlacement();
        store.Days ??= new Dictionary<string, DayRecord>();

        foreach (var record in store.Days.Values)
        {
            record.MigrateLegacyText();
        }

        if (!IsFinite(store.Ui.Window.Left))
        {
            store.Ui.Window.Left = 60;
        }

        if (!IsFinite(store.Ui.Window.Top))
        {
            store.Ui.Window.Top = 60;
        }

        if (!IsFinite(store.Ui.Window.Width) || store.Ui.Window.Width < 760)
        {
            store.Ui.Window.Width = 980;
        }

        if (!IsFinite(store.Ui.Window.Height) || store.Ui.Window.Height < 520)
        {
            store.Ui.Window.Height = 650;
        }

        if (!IsFinite(store.Ui.OpacityPercent) || store.Ui.OpacityPercent < 55 || store.Ui.OpacityPercent > 95)
        {
            store.Ui.OpacityPercent = 82;
        }

        if (string.IsNullOrWhiteSpace(store.Ui.ThemeName))
        {
            store.Ui.ThemeName = CalendarTheme.Default.Name;
        }
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public sealed class DayRecord
{
    public string Note { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string Done { get; set; } = string.Empty;

    public void MigrateLegacyText()
    {
        if (!string.IsNullOrWhiteSpace(Note))
        {
            return;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Plan))
        {
            parts.Add(Plan.Trim());
        }

        if (!string.IsNullOrWhiteSpace(Done))
        {
            parts.Add(Done.Trim());
        }

        Note = string.Join(Environment.NewLine, parts);
    }
}

public sealed class UiSettings
{
    public WidgetWindowPlacement Window { get; set; } = new();
    public double OpacityPercent { get; set; } = 82;
    public bool DesktopMode { get; set; }
    public string ThemeName { get; set; } = CalendarTheme.Default.Name;
}

public sealed class WidgetWindowPlacement
{
    public double Left { get; set; } = 60;
    public double Top { get; set; } = 60;
    public double Width { get; set; } = 980;
    public double Height { get; set; } = 650;
}

public sealed record CalendarTheme(
    string Name,
    string DisplayName,
    MediaColor Surface,
    MediaColor Text,
    MediaColor Muted,
    MediaColor Accent,
    MediaColor Button,
    MediaColor Border,
    MediaColor Day,
    MediaColor Today,
    MediaColor Selected)
{
    public static readonly CalendarTheme Default = new(
        "dark-glass",
        "深色玻璃",
        MediaColor.FromRgb(16, 20, 24),
        MediaColor.FromRgb(244, 248, 250),
        MediaColor.FromRgb(159, 178, 188),
        MediaColor.FromRgb(138, 218, 255),
        MediaColor.FromArgb(34, 255, 255, 255),
        MediaColor.FromArgb(56, 255, 255, 255),
        MediaColor.FromArgb(20, 255, 255, 255),
        MediaColor.FromArgb(56, 255, 255, 255),
        MediaColor.FromArgb(82, 83, 177, 221));

    public static IReadOnlyList<CalendarTheme> All { get; } = new[]
    {
        Default,
        new CalendarTheme(
            "warm-paper",
            "暖白纸感",
            MediaColor.FromRgb(242, 236, 222),
            MediaColor.FromRgb(42, 38, 32),
            MediaColor.FromRgb(108, 99, 85),
            MediaColor.FromRgb(166, 94, 58),
            MediaColor.FromArgb(80, 255, 255, 255),
            MediaColor.FromArgb(90, 92, 72, 52),
            MediaColor.FromArgb(44, 255, 255, 255),
            MediaColor.FromArgb(70, 255, 248, 220),
            MediaColor.FromArgb(96, 189, 117, 76)),
        new CalendarTheme(
            "mist-green",
            "雾绿清透",
            MediaColor.FromRgb(18, 46, 42),
            MediaColor.FromRgb(235, 249, 242),
            MediaColor.FromRgb(166, 205, 192),
            MediaColor.FromRgb(130, 231, 190),
            MediaColor.FromArgb(34, 232, 255, 246),
            MediaColor.FromArgb(58, 203, 246, 226),
            MediaColor.FromArgb(22, 232, 255, 246),
            MediaColor.FromArgb(56, 186, 244, 215),
            MediaColor.FromArgb(82, 80, 195, 159)),
        new CalendarTheme(
            "violet-night",
            "紫夜高亮",
            MediaColor.FromRgb(29, 23, 45),
            MediaColor.FromRgb(250, 246, 255),
            MediaColor.FromRgb(196, 181, 220),
            MediaColor.FromRgb(218, 172, 255),
            MediaColor.FromArgb(34, 255, 255, 255),
            MediaColor.FromArgb(60, 238, 216, 255),
            MediaColor.FromArgb(20, 255, 255, 255),
            MediaColor.FromArgb(52, 238, 216, 255),
            MediaColor.FromArgb(92, 150, 93, 211)),
        new CalendarTheme(
            "clear-ink",
            "清透墨蓝",
            MediaColor.FromRgb(10, 28, 44),
            MediaColor.FromRgb(239, 249, 255),
            MediaColor.FromRgb(148, 182, 203),
            MediaColor.FromRgb(118, 209, 255),
            MediaColor.FromArgb(30, 235, 250, 255),
            MediaColor.FromArgb(58, 170, 225, 255),
            MediaColor.FromArgb(20, 235, 250, 255),
            MediaColor.FromArgb(54, 118, 209, 255),
            MediaColor.FromArgb(86, 53, 139, 202))
    };

    public SolidColorBrush TextBrush => new(Text);
    public SolidColorBrush MutedBrush => new(Muted);
    public SolidColorBrush AccentBrush => new(Accent);
    public SolidColorBrush ButtonBrush => new(Button);
    public SolidColorBrush BorderBrush => new(Border);
    public SolidColorBrush DayBrush => new(Day);
    public SolidColorBrush TodayBrush => new(Today);
    public SolidColorBrush SelectedBrush => new(Selected);
    public SolidColorBrush PopupBrush => new(Surface);
    public SolidColorBrush HighlightTextBrush => new(ContrastText());

    private MediaColor ContrastText()
    {
        var brightness = Accent.R * 0.299 + Accent.G * 0.587 + Accent.B * 0.114;
        return brightness > 150 ? MediaColor.FromRgb(20, 24, 28) : MediaColor.FromRgb(255, 255, 255);
    }

    public static CalendarTheme Find(string? name)
    {
        return All.FirstOrDefault(theme => theme.Name == name) ?? Default;
    }

    public static int IndexOf(string name)
    {
        var index = All.ToList().FindIndex(theme => theme.Name == name);
        return index < 0 ? 0 : index;
    }
}

public sealed record HolidayInfo(string Name, bool IsWorkday);

public static class HolidayCalendar
{
    private static readonly Dictionary<DateTime, HolidayInfo> Holidays = Build2026Holidays();

    public static HolidayInfo? Get(DateTime day)
    {
        return Holidays.TryGetValue(day.Date, out var info) ? info : null;
    }

    private static Dictionary<DateTime, HolidayInfo> Build2026Holidays()
    {
        var holidays = new Dictionary<DateTime, HolidayInfo>();
        AddRange(holidays, "元旦", new DateTime(2026, 1, 1), new DateTime(2026, 1, 3));
        AddWorkday(holidays, "元旦调休", new DateTime(2026, 1, 4));

        AddRange(holidays, "春节", new DateTime(2026, 2, 15), new DateTime(2026, 2, 23));
        AddWorkday(holidays, "春节调休", new DateTime(2026, 2, 14));
        AddWorkday(holidays, "春节调休", new DateTime(2026, 2, 28));

        AddRange(holidays, "清明", new DateTime(2026, 4, 4), new DateTime(2026, 4, 6));

        AddRange(holidays, "劳动节", new DateTime(2026, 5, 1), new DateTime(2026, 5, 5));
        AddWorkday(holidays, "劳动节调休", new DateTime(2026, 5, 9));

        AddRange(holidays, "端午", new DateTime(2026, 6, 19), new DateTime(2026, 6, 21));

        AddRange(holidays, "中秋", new DateTime(2026, 9, 25), new DateTime(2026, 9, 27));

        AddRange(holidays, "国庆", new DateTime(2026, 10, 1), new DateTime(2026, 10, 7));
        AddWorkday(holidays, "国庆调休", new DateTime(2026, 9, 20));
        AddWorkday(holidays, "国庆调休", new DateTime(2026, 10, 10));

        return holidays;
    }

    private static void AddRange(Dictionary<DateTime, HolidayInfo> holidays, string name, DateTime start, DateTime end)
    {
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            holidays[day] = new HolidayInfo(name, false);
        }
    }

    private static void AddWorkday(Dictionary<DateTime, HolidayInfo> holidays, string name, DateTime day)
    {
        holidays[day.Date] = new HolidayInfo(name, true);
    }
}

public static class LunarCalendarText
{
    private static readonly ChineseLunisolarCalendar Calendar = new();
    private static readonly string[] MonthNames = ["正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月"];
    private static readonly string[] DayNames =
    [
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
    ];

    public static string GetDayText(DateTime day)
    {
        try
        {
            var lunarYear = Calendar.GetYear(day);
            var lunarMonth = Calendar.GetMonth(day);
            var lunarDay = Calendar.GetDayOfMonth(day);
            var leapMonth = Calendar.GetLeapMonth(lunarYear);
            var isLeap = leapMonth > 0 && lunarMonth == leapMonth;
            if (leapMonth > 0 && lunarMonth >= leapMonth)
            {
                lunarMonth--;
            }

            var festival = GetFestival(lunarMonth, lunarDay);
            if (!string.IsNullOrWhiteSpace(festival))
            {
                return festival;
            }

            if (lunarDay == 1)
            {
                return $"{(isLeap ? "闰" : string.Empty)}{MonthNames[Math.Clamp(lunarMonth - 1, 0, MonthNames.Length - 1)]}";
            }

            return DayNames[Math.Clamp(lunarDay - 1, 0, DayNames.Length - 1)];
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetFestival(int lunarMonth, int lunarDay)
    {
        return (lunarMonth, lunarDay) switch
        {
            (1, 1) => "春节",
            (1, 15) => "元宵",
            (5, 5) => "端午",
            (7, 7) => "七夕",
            (8, 15) => "中秋",
            (9, 9) => "重阳",
            (12, 8) => "腊八",
            (12, 23) => "小年",
            _ => string.Empty
        };
    }
}
