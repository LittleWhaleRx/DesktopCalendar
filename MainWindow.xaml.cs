using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopCalendar;

public partial class MainWindow : Window
{
    private static readonly CultureInfo ZhCn = CultureInfo.GetCultureInfo("zh-CN");
    private readonly string _dataPath;
    private CalendarStore _store = new();
    private DateTime _currentMonth;
    private DateTime _selectedDate;
    private bool _isLoading;
    private bool _isLoaded;
    private bool _isDesktopMode;
    private IntPtr _windowHandle;

    public MainWindow()
    {
        InitializeComponent();

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopCalendar");
        _dataPath = Path.Combine(appData, "calendar-data.json");

        _selectedDate = DateTime.Today;
        _currentMonth = new DateTime(_selectedDate.Year, _selectedDate.Month, 1);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;
        _store = CalendarStore.Load(_dataPath);
        RestoreWindowPlacement();

        OpacitySlider.Value = _store.Ui.OpacityPercent;
        DesktopModeCheck.IsChecked = _store.Ui.DesktopMode;
        LockCheck.IsChecked = _store.Ui.LockPosition;

        _isLoading = false;
        _isLoaded = true;

        ApplyOpacity();
        RenderCalendar();
        LoadSelectedDate();

        if (_store.Ui.DesktopMode)
        {
            Dispatcher.BeginInvoke(TryEnterDesktopMode);
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowPlacement();
        CalendarStore.Save(_dataPath, _store);
    }

    private void Window_PositionChanged(object sender, EventArgs e)
    {
        if (!_isLoaded || _isLoading)
        {
            return;
        }

        SaveWindowPlacement();
        CalendarStore.Save(_dataPath, _store);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            return;
        }

        if (LockCheck.IsChecked == true)
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
        LoadSelectedDate();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PlanBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        UpdateSelectedRecord(plan: PlanBox.Text, done: DoneBox.Text);
    }

    private void DoneBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        UpdateSelectedRecord(plan: PlanBox.Text, done: DoneBox.Text);
    }

    private void AddAnniversary_Click(object sender, RoutedEventArgs e)
    {
        var name = AnniversaryInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _store.Anniversaries.Add(new AnniversaryItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Date = _selectedDate.Date
        });

        AnniversaryInput.Clear();
        CalendarStore.Save(_dataPath, _store);
        RenderCalendar();
        LoadSelectedDate();
    }

    private void DeleteAnniversary_Click(object sender, RoutedEventArgs e)
    {
        if (AnniversaryList.SelectedItem is not AnniversaryView item)
        {
            return;
        }

        _store.Anniversaries.RemoveAll(a => a.Id == item.Id);
        CalendarStore.Save(_dataPath, _store);
        RenderCalendar();
        LoadSelectedDate();
    }

    private void DesktopModeCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || !_isLoaded)
        {
            return;
        }

        _store.Ui.DesktopMode = DesktopModeCheck.IsChecked == true;
        if (_store.Ui.DesktopMode)
        {
            TryEnterDesktopMode();
        }
        else
        {
            ExitDesktopMode();
        }

        CalendarStore.Save(_dataPath, _store);
    }

    private void LockCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || !_isLoaded)
        {
            return;
        }

        _store.Ui.LockPosition = LockCheck.IsChecked == true;
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
        MonthTitle.Text = _currentMonth.ToString("yyyy年 M月", ZhCn);
        CalendarGrid.Children.Clear();

        var firstDay = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
        var startDate = firstDay.AddDays(-(int)firstDay.DayOfWeek);

        for (var i = 0; i < 42; i++)
        {
            var day = startDate.AddDays(i);
            CalendarGrid.Children.Add(CreateDayButton(day));
        }
    }

    private Button CreateDayButton(DateTime day)
    {
        var isCurrentMonth = day.Month == _currentMonth.Month;
        var isToday = day.Date == DateTime.Today;
        var isSelected = day.Date == _selectedDate.Date;

        var dateText = new TextBlock
        {
            Text = day.Day.ToString(CultureInfo.InvariantCulture),
            FontWeight = isToday || isSelected ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = 15,
            Foreground = isCurrentMonth ? Brushes.White : new SolidColorBrush(Color.FromArgb(145, 255, 255, 255))
        };

        var markerText = new TextBlock
        {
            Text = BuildMarkerText(day),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11,
            LineHeight = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(166, 218, 238))
        };

        var content = new StackPanel
        {
            Margin = new Thickness(8, 6, 8, 6)
        };
        content.Children.Add(dateText);
        content.Children.Add(markerText);

        var button = new Button
        {
            Content = content,
            Tag = day.Date,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(3),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Opacity = isCurrentMonth ? 1 : 0.42,
            Background = BuildDayBackground(isToday, isSelected),
            BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromRgb(138, 218, 255))
                : new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            Foreground = Brushes.White,
            Template = CreateDayButtonTemplate()
        };
        button.Click += DayButton_Click;
        return button;
    }

    private static ControlTemplate CreateDayButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "ButtonBorder";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Stretch);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button))
        {
            VisualTree = border
        };

        var hover = new Trigger
        {
            Property = IsMouseOverProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromArgb(54, 255, 255, 255))));
        template.Triggers.Add(hover);

        return template;
    }

    private Brush BuildDayBackground(bool isToday, bool isSelected)
    {
        if (isSelected)
        {
            return new SolidColorBrush(Color.FromArgb(82, 83, 177, 221));
        }

        if (isToday)
        {
            return new SolidColorBrush(Color.FromArgb(56, 255, 255, 255));
        }

        return new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
    }

    private string BuildMarkerText(DateTime day)
    {
        var markers = new List<string>();
        var key = DayKey(day);

        if (_store.Days.TryGetValue(key, out var record))
        {
            if (!string.IsNullOrWhiteSpace(record.Plan))
            {
                markers.Add("计划");
            }

            if (!string.IsNullOrWhiteSpace(record.Done))
            {
                markers.Add("记录");
            }
        }

        if (_store.Anniversaries.Any(a => IsSameMonthDay(a.Date, day)))
        {
            markers.Add("纪念");
        }

        return string.Join(Environment.NewLine, markers);
    }

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateTime day })
        {
            return;
        }

        _selectedDate = day.Date;
        _currentMonth = new DateTime(day.Year, day.Month, 1);
        RenderCalendar();
        LoadSelectedDate();
    }

    private void LoadSelectedDate()
    {
        _isLoading = true;

        SelectedDateTitle.Text = _selectedDate.ToString("yyyy年M月d日 dddd", ZhCn);
        var key = DayKey(_selectedDate);
        if (_store.Days.TryGetValue(key, out var record))
        {
            PlanBox.Text = record.Plan;
            DoneBox.Text = record.Done;
        }
        else
        {
            PlanBox.Clear();
            DoneBox.Clear();
        }

        var anniversaries = _store.Anniversaries
            .Where(a => IsSameMonthDay(a.Date, _selectedDate))
            .OrderBy(a => a.Date)
            .Select(a => new AnniversaryView(a.Id, BuildAnniversaryText(a)))
            .ToList();

        AnniversaryList.ItemsSource = anniversaries;
        AnniversaryTodayLabel.Text = anniversaries.Count == 0
            ? "今天没有纪念日"
            : string.Join("  /  ", anniversaries.Select(a => a.Text));

        _isLoading = false;
    }

    private static string BuildAnniversaryText(AnniversaryItem item)
    {
        var years = DateTime.Today.Year - item.Date.Year;
        if (years <= 0)
        {
            return $"{item.Name} ({item.Date:yyyy-MM-dd})";
        }

        return $"{item.Name} ({years}周年)";
    }

    private void UpdateSelectedRecord(string plan, string done)
    {
        var key = DayKey(_selectedDate);
        if (string.IsNullOrWhiteSpace(plan) && string.IsNullOrWhiteSpace(done))
        {
            _store.Days.Remove(key);
        }
        else
        {
            _store.Days[key] = new DayRecord
            {
                Plan = plan,
                Done = done
            };
        }

        CalendarStore.Save(_dataPath, _store);
        RenderCalendar();
    }

    private void ApplyOpacity()
    {
        var alpha = (byte)Math.Clamp(OpacitySlider.Value / 100d * 255d, 130d, 245d);
        RootChrome.Background = new SolidColorBrush(Color.FromArgb(alpha, 16, 20, 24));
    }

    private void TryEnterDesktopMode()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
        }

        var result = DesktopHost.AttachToDesktop(_windowHandle);
        _isDesktopMode = result.Attached;
        StatusText.Text = result.Attached ? result.Message : $"{result.Message}，已保持普通窗口";

        if (!result.Attached)
        {
            DesktopHost.Detach(_windowHandle);
            _store.Ui.DesktopMode = false;
            _isLoading = true;
            DesktopModeCheck.IsChecked = false;
            _isLoading = false;
        }
    }

    private void ExitDesktopMode()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            DesktopHost.Detach(_windowHandle);
        }

        _isDesktopMode = false;
        StatusText.Text = "普通窗口模式";
    }

    private void RestoreWindowPlacement()
    {
        var placement = _store.Ui.Window;
        if (placement.Width >= MinWidth && placement.Height >= MinHeight)
        {
            Width = placement.Width;
            Height = placement.Height;
        }

        if (placement.Left > -10000 && placement.Top > -10000)
        {
            Left = placement.Left;
            Top = placement.Top;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 42;
            Top = SystemParameters.WorkArea.Top + 42;
        }
    }

    private void SaveWindowPlacement()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        _store.Ui.Window.Left = Left;
        _store.Ui.Window.Top = Top;
        _store.Ui.Window.Width = Width;
        _store.Ui.Window.Height = Height;
    }

    private static bool IsSameMonthDay(DateTime left, DateTime right)
    {
        return left.Month == right.Month && left.Day == right.Day;
    }

    private static string DayKey(DateTime day)
    {
        return day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

public sealed record AnniversaryView(string Id, string Text)
{
    public override string ToString()
    {
        return Text;
    }
}

public sealed class CalendarStore
{
    public Dictionary<string, DayRecord> Days { get; set; } = new();
    public List<AnniversaryItem> Anniversaries { get; set; } = new();
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
            return JsonSerializer.Deserialize<CalendarStore>(json) ?? new CalendarStore();
        }
        catch
        {
            return new CalendarStore();
        }
    }

    public static void Save(string path, CalendarStore store)
    {
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
}

public sealed class DayRecord
{
    public string Plan { get; set; } = string.Empty;
    public string Done { get; set; } = string.Empty;
}

public sealed class AnniversaryItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}

public sealed class UiSettings
{
    public WidgetWindowPlacement Window { get; set; } = new();
    public double OpacityPercent { get; set; } = 82;
    public bool DesktopMode { get; set; }
    public bool LockPosition { get; set; }
}

public sealed class WidgetWindowPlacement
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 920;
    public double Height { get; set; } = 590;
}
