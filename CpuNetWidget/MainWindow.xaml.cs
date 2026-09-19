using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using CpuNetWidget.Monitoring;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CpuNetWidget;

public partial class MainWindow : Window
{
    private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "CpuNetWidget";
    private const double CompactWidth = 50;
    private const double CompactHeight = 228;
    private const double DockThickness = 7;
    private const double DockLength = 50;
    private const double DockThreshold = 24;
    private const double RestoreInset = DockThreshold + 8;
    private static readonly System.Windows.Media.Brush CpuNormalBrush = CreateFrozenBrush(84, 214, 167);
    private static readonly System.Windows.Media.Brush WarningBrush = CreateFrozenBrush(255, 184, 108);
    private static readonly System.Windows.Media.Brush CriticalBrush = CreateFrozenBrush(255, 92, 92);

    private readonly CpuUsageReader _cpuUsageReader = new();
    private readonly NetworkSpeedReader _networkSpeedReader = new();
    private readonly CpuTemperatureReader? _temperatureReader;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _edgeDockTimer;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _contextMenu;
    private readonly List<HistorySample> _history = [];
    private AppSettings _settings;
    private bool _reallyClose;
    private int _updateInProgress;
    private double _expandedWindowHeight = 330;
    private double _expandedWindowWidth = 390;
    private bool _compactDragPending;
    private System.Windows.Point _compactDragStart;
    private bool _isEdgeDocked;
    private DockEdge _dockedEdge;
    private double _dockAnchor;
    private long _suppressEdgeDockUntilTimestamp;
    private bool _settingsWindowOpen;
    private bool _isExiting;
    private bool _refreshFailureLogged;
    private TimeSpan ChartRange => TimeSpan.FromMinutes(_settings.ChartRangeMinutes);

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        Topmost = _settings.AlwaysOnTop;

        try
        {
            _temperatureReader = new CpuTemperatureReader();
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log("初始化温度监控模块失败。", exception);
            TemperatureHint.Text = "温度模块不可用";
            TemperatureHint.ToolTip = exception.Message;
        }

        _contextMenu = new Forms.ContextMenuStrip();
        _contextMenu.Items.Add("显示悬浮窗", null, (_, _) => ShowWidget());
        _contextMenu.Items.Add("隐藏悬浮窗", null, (_, _) => Dispatcher.Invoke(Hide));
        _contextMenu.Items.Add("切换窗口模式", null, (_, _) =>
            Dispatcher.Invoke(() => SetCompactMode(!_settings.CompactMode)));
        _contextMenu.Items.Add(new Forms.ToolStripSeparator());
        _contextMenu.Items.Add("设置…", null, async (_, _) => await OpenSettingsAsync());
        _contextMenu.Items.Add(new Forms.ToolStripSeparator());
        _contextMenu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "CPU 网速悬浮窗",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWidget();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await RefreshMetricsAsync();

        _edgeDockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _edgeDockTimer.Tick += (_, _) =>
        {
            _edgeDockTimer.Stop();
            TryDockToScreenEdge();
        };
        LocationChanged += (_, _) =>
        {
            if (!IsLoaded || !IsVisible || WindowState != WindowState.Normal
                || !_settings.CompactMode || _isEdgeDocked) return;
            _edgeDockTimer.Stop();
            _edgeDockTimer.Start();
        };

        Loaded += async (_, _) =>
        {
            KeepInsideWorkingArea();
            ApplyMonitoringVisuals();
            ApplyWindowMode();
            _timer.Start();
            await RefreshMetricsAsync();
        };
    }

    private async Task RefreshMetricsAsync()
    {
        if (_isExiting || Interlocked.Exchange(ref _updateInProgress, 1) == 1) return;

        try
        {
            double? cpuUsage = _settings.MonitorCpu ? _cpuUsageReader.ReadUsage() : null;
            NetworkSpeed? network = _settings.MonitorDownload || _settings.MonitorUpload
                ? _networkSpeedReader.ReadSpeed()
                : null;
            var selectedSensor = _settings.TemperatureSensorId;
            var temperature = _settings.MonitorTemperature && _temperatureReader is not null
                ? await Task.Run(() => _temperatureReader.ReadTemperature(selectedSensor))
                : new TemperatureReading(null,
                    _settings.MonitorTemperature ? "温度模块不可用" : "监控已关闭");

            if (_isExiting) return;
            UpdateMetricValues(cpuUsage, temperature, network);
            if (_settings.ShowNetworkChart)
            {
                AddHistory(new HistorySample(
                    _settings.MonitorDownload ? network?.DownloadBytesPerSecond : null,
                    _settings.MonitorUpload ? network?.UploadBytesPerSecond : null,
                    Stopwatch.GetTimestamp()));
            }
            UpdateTrayText(cpuUsage, temperature, network);
            _refreshFailureLogged = false;
        }
        catch (Exception exception)
        {
            if (!_refreshFailureLogged)
            {
                AppDiagnostics.Log("刷新监控数据失败。", exception);
                _refreshFailureLogged = true;
            }
            if (!_isExiting)
            {
                TemperatureHint.Text = "本次刷新失败，将自动重试";
                TemperatureHint.ToolTip = exception.Message;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _updateInProgress, 0);
        }
    }

    private void UpdateMetricValues(double? cpuUsage, TemperatureReading temperature, NetworkSpeed? network)
    {
        if (_settings.MonitorCpu)
        {
            CpuText.Text = cpuUsage.HasValue ? $"{cpuUsage:0}%" : "--%";
            CpuProgress.Value = cpuUsage ?? 0;
        }

        if (_settings.MonitorTemperature)
        {
            if (temperature.Celsius.HasValue)
            {
                var value = temperature.Celsius.Value;
                TemperatureText.Text = $"{value:0}°C";
                TemperatureText.Foreground = value switch
                {
                    >= 90 => CriticalBrush,
                    >= 75 => WarningBrush,
                    _ => System.Windows.Media.Brushes.White
                };
                TemperatureHint.Text = temperature.SensorName;
                TemperatureHint.ToolTip = temperature.SensorName;
            }
            else
            {
                TemperatureText.Text = "--°C";
                TemperatureText.Foreground = System.Windows.Media.Brushes.White;
                TemperatureHint.Text = temperature.SensorName;
                TemperatureHint.ToolTip = temperature.SensorName;
            }
        }

        if (_settings.MonitorDownload)
            DownloadText.Text = network.HasValue ? FormatSpeed(network.Value.DownloadBytesPerSecond) : "-- B/s";
        if (_settings.MonitorUpload)
            UploadText.Text = network.HasValue ? FormatSpeed(network.Value.UploadBytesPerSecond) : "-- B/s";

        UpdateCompactDisplay(cpuUsage, temperature, network);
    }

    private void UpdateCompactDisplay(double? cpuUsage, TemperatureReading temperature, NetworkSpeed? network)
    {
        CompactCpuText.Text = _settings.MonitorCpu && cpuUsage.HasValue ? $"{cpuUsage:0}%" : "--%";
        CompactTemperatureText.Text = _settings.MonitorTemperature && temperature.Celsius.HasValue
            ? $"{temperature.Celsius:0}°" : "--°";
        CompactDownloadText.Text = _settings.MonitorDownload && network.HasValue
            ? FormatCompactSpeed(network.Value.DownloadBytesPerSecond) : "--";
        CompactUploadText.Text = _settings.MonitorUpload && network.HasValue
            ? FormatCompactSpeed(network.Value.UploadBytesPerSecond) : "--";

        UpdateCompactCpuArc(_settings.MonitorCpu ? cpuUsage : null);
        CompactPanel.ToolTip = $"拖到屏幕边缘可自动收起，双击展开完整面板\nCPU {CompactCpuText.Text}  温度 {CompactTemperatureText.Text}\n" +
                               $"下载 {CompactDownloadText.Text}/s  上传 {CompactUploadText.Text}/s";
    }

    private void UpdateCompactCpuArc(double? usage)
    {
        if (!usage.HasValue || usage.Value <= 0)
        {
            CompactCpuArc.Data = null;
            return;
        }

        var value = Math.Clamp(usage.Value, 0, 100);
        var angle = Math.Min(359.99, value * 3.6);
        const double center = 22;
        const double radius = 18.5;
        var start = new System.Windows.Point(center, center - radius);
        var radians = (angle - 90) * Math.PI / 180;
        var end = new System.Windows.Point(
            center + radius * Math.Cos(radians),
            center + radius * Math.Sin(radians));

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new System.Windows.Size(radius, radius), 0,
            angle >= 180, SweepDirection.Clockwise, true));
        CompactCpuArc.Data = new PathGeometry([figure]);
        CompactCpuArc.Stroke = value switch
        {
            >= 90 => CriticalBrush,
            >= 75 => WarningBrush,
            _ => CpuNormalBrush
        };
    }

    private void ApplyMonitoringVisuals()
    {
        SetPanelState(CpuPanel, CpuText, _settings.MonitorCpu, "--%", CpuProgress);
        SetPanelState(TemperaturePanel, TemperatureText, _settings.MonitorTemperature, "--°C");
        SetPanelState(DownloadPanel, DownloadText, _settings.MonitorDownload, "0 B/s");
        SetPanelState(UploadPanel, UploadText, _settings.MonitorUpload, "0 B/s");
        TemperatureHint.Text = _settings.MonitorTemperature ? "正在读取传感器" : "监控已关闭";

        DownloadLine.Visibility = _settings.ShowNetworkChart && _settings.MonitorDownload
            ? Visibility.Visible : Visibility.Collapsed;
        UploadLine.Visibility = _settings.ShowNetworkChart && _settings.MonitorUpload
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyChartVisibility();
    }

    private void ApplyChartVisibility()
    {
        ChartPanel.Visibility = _settings.ShowNetworkChart ? Visibility.Visible : Visibility.Collapsed;
        ChartSeparator.Visibility = _settings.ShowNetworkChart ? Visibility.Visible : Visibility.Collapsed;
        ChartRow.Height = _settings.ShowNetworkChart ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        if (!IsLoaded || _settings.CompactMode) return;
        if (_settings.ShowNetworkChart)
        {
            MinHeight = 300;
            Height = Math.Max(330, _expandedWindowHeight);
        }
        else
        {
            if (Height >= 300) _expandedWindowHeight = Height;
            MinHeight = 190;
            Height = 190;
        }
    }

    private void ApplyWindowMode()
    {
        if (!_settings.CompactMode || !_settings.AutoHideAtScreenEdge) _isEdgeDocked = false;
        FullPanel.Visibility = !_settings.CompactMode ? Visibility.Visible : Visibility.Collapsed;
        CompactPanel.Visibility = _settings.CompactMode && !_isEdgeDocked
            ? Visibility.Visible : Visibility.Collapsed;
        DockedStripPanel.Visibility = _settings.CompactMode && _isEdgeDocked
            ? Visibility.Visible : Visibility.Collapsed;
        if (!IsLoaded) return;

        if (_isEdgeDocked)
        {
            ApplyDockedDimensions(GetCurrentWorkingArea());
            return;
        }

        if (_settings.CompactMode)
        {
            if (Width >= 350) _expandedWindowWidth = Width;
            if (Height >= 300) _expandedWindowHeight = Height;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = CompactWidth;
            MinHeight = CompactHeight;
            Width = CompactWidth;
            Height = CompactHeight;
        }
        else
        {
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 350;
            Width = Math.Max(390, _expandedWindowWidth);
            ApplyChartVisibility();
        }
        KeepInsideWorkingArea();
    }

    private void SetCompactMode(bool compact)
    {
        if (!compact) _isEdgeDocked = false;
        _settings.CompactMode = compact;
        _settings.Save();
        ApplyWindowMode();
    }

    private static void SetPanelState(StackPanel panel, TextBlock valueText, bool enabled,
        string enabledPlaceholder, System.Windows.Controls.ProgressBar? progressBar = null)
    {
        panel.Opacity = enabled ? 1 : 0.38;
        valueText.Text = enabled ? enabledPlaceholder : "已关闭";
        if (progressBar is not null)
        {
            progressBar.Value = 0;
            progressBar.Visibility = enabled ? Visibility.Visible : Visibility.Hidden;
        }
    }

    private void AddHistory(HistorySample sample)
    {
        _history.Add(sample);
        var now = Stopwatch.GetTimestamp();
        _history.RemoveAll(item => Stopwatch.GetElapsedTime(item.Timestamp, now) > ChartRange);
        RenderChart();
    }

    private void RenderChart()
    {
        ChartTitleText.Text = $"实时网速 · 最近 {_settings.ChartRangeMinutes} 分钟";
        if (!_settings.ShowNetworkChart)
        {
            DownloadLine.Points.Clear();
            UploadLine.Points.Clear();
            return;
        }

        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        if (width <= 1 || height <= 1 || _history.Count == 0) return;

        var networkMaximum = 1024d;
        foreach (var sample in _history)
        {
            if (sample.Download is { } download && double.IsFinite(download))
                networkMaximum = Math.Max(networkMaximum, download);
            if (sample.Upload is { } upload && double.IsFinite(upload))
                networkMaximum = Math.Max(networkMaximum, upload);
        }

        var now = Stopwatch.GetTimestamp();
        DownloadLine.Points = BuildPoints(sample => sample.Download,
            value => value / networkMaximum, width, height, now);
        UploadLine.Points = BuildPoints(sample => sample.Upload,
            value => value / networkMaximum, width, height, now);
        NetworkScaleText.Text = _settings.MonitorDownload || _settings.MonitorUpload
            ? $"网络峰值 {FormatSpeed(networkMaximum)}"
            : "网络监控已关闭";
    }

    private PointCollection BuildPoints(Func<HistorySample, double?> selector,
        Func<double, double> normalize, double width, double height, long now)
    {
        var points = new PointCollection();
        var rangeSeconds = ChartRange.TotalSeconds;
        foreach (var sample in _history)
        {
            var value = selector(sample);
            if (!value.HasValue) continue;
            var ageSeconds = Stopwatch.GetElapsedTime(sample.Timestamp, now).TotalSeconds;
            var x = width * Math.Clamp(1 - ageSeconds / rangeSeconds, 0, 1);
            var normalized = Math.Clamp(normalize(value.Value), 0, 1);
            points.Add(new System.Windows.Point(x, height * (1 - normalized)));
        }
        return points;
    }

    private async Task OpenSettingsAsync()
    {
        if (_settingsWindowOpen || _isExiting) return;
        _settingsWindowOpen = true;
        try
        {
            IReadOnlyList<TemperatureSensorOption> sensors = [];
            if (_temperatureReader is not null)
            {
                try { sensors = await Task.Run(_temperatureReader.GetAvailableSensors); }
                catch (Exception exception)
                {
                    AppDiagnostics.Log("枚举温度传感器失败。", exception);
                }
            }

            if (_isExiting) return;
            var oldSettings = _settings;
            var dialog = new SettingsWindow(_settings, sensors, IsAutoStartEnabled()) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            TrySetAutoStart(dialog.AutoStartEnabled);

            _settings = dialog.ResultSettings;
            if (!_settings.Save())
            {
                System.Windows.MessageBox.Show(
                    $"设置已经在本次运行中生效，但无法保存到注册表。\n\n诊断日志：\n{AppDiagnostics.LogPath}",
                    "设置未持久化", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Topmost = _settings.AlwaysOnTop;
            _cpuUsageReader.Reset();
            _networkSpeedReader.Reset();
            _history.Clear();
            ApplyMonitoringVisuals();
            ApplyWindowMode();
            RenderChart();

            if (!oldSettings.RunAsAdministrator && _settings.RunAsAdministrator
                && !PrivilegeHelper.IsAdministrator())
            {
                var restart = System.Windows.MessageBox.Show(
                    "管理员权限设置已保存。是否立即重启并显示 Windows UAC 确认？",
                    "需要重启", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (restart == MessageBoxResult.Yes && PrivilegeHelper.TryRestartAsAdministrator())
                    ExitApplication();
            }
        }
        finally
        {
            _settingsWindowOpen = false;
        }
    }

    private void UpdateTrayText(double? cpuUsage, TemperatureReading temperature, NetworkSpeed? network)
    {
        var parts = new List<string>();
        if (_settings.MonitorCpu) parts.Add(cpuUsage.HasValue ? $"CPU {cpuUsage:0}%" : "CPU --");
        if (_settings.MonitorTemperature) parts.Add(temperature.Celsius.HasValue ? $"{temperature.Celsius:0}°C" : "温度 --");
        if (_settings.MonitorDownload) parts.Add(network.HasValue ? $"↓ {FormatSpeed(network.Value.DownloadBytesPerSecond)}" : "↓ --");
        if (_settings.MonitorUpload) parts.Add(network.HasValue ? $"↑ {FormatSpeed(network.Value.UploadBytesPerSecond)}" : "↑ --");
        _notifyIcon.Text = TruncateTrayText(parts.Count > 0 ? string.Join("  ", parts) : "CPU 网速悬浮窗 · 监控已关闭");
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        bytesPerSecond = NormalizeRate(bytesPerSecond);
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:0} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:0.0} KB/s";
        if (bytesPerSecond < 1024 * 1024 * 1024) return $"{bytesPerSecond / 1024 / 1024:0.0} MB/s";
        return $"{bytesPerSecond / 1024 / 1024 / 1024:0.00} GB/s";
    }

    private static string FormatCompactSpeed(double bytesPerSecond)
    {
        bytesPerSecond = NormalizeRate(bytesPerSecond);
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:0} B";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:0} K";
        if (bytesPerSecond < 1024 * 1024 * 1024) return $"{bytesPerSecond / 1024 / 1024:0.0} M";
        return $"{bytesPerSecond / 1024 / 1024 / 1024:0.0} G";
    }

    private static string TruncateTrayText(string text) => text.Length <= 63 ? text : text[..63];

    private static double NormalizeRate(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (_settings.CompactMode) return;

        for (var element = e.OriginalSource as DependencyObject; element is not null;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is System.Windows.Controls.Primitives.ButtonBase or Thumb or ResizeGrip) return;
        }

        e.Handled = TryDragMove();
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e) => await OpenSettingsAsync();

    private void CompactModeButton_Click(object sender, RoutedEventArgs e) => SetCompactMode(true);

    private void CompactPanel_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _contextMenu.Show(Forms.Cursor.Position);
        e.Handled = true;
    }

    private void CompactPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            _compactDragPending = false;
            CompactPanel.ReleaseMouseCapture();
            SetCompactMode(false);
            e.Handled = true;
            return;
        }

        _compactDragPending = true;
        _compactDragStart = e.GetPosition(this);
        CompactPanel.CaptureMouse();
        e.Handled = true;
    }

    private void CompactPanel_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_compactDragPending || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _compactDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _compactDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _compactDragPending = false;
        CompactPanel.ReleaseMouseCapture();
        if (TryDragMove()) TryDockToScreenEdge();
        e.Handled = true;
    }

    private void CompactPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _compactDragPending = false;
        CompactPanel.ReleaseMouseCapture();
        e.Handled = true;
    }

    private bool TryDragMove()
    {
        try
        {
            DragMove();
            return true;
        }
        catch (InvalidOperationException exception)
        {
            AppDiagnostics.Log("拖动悬浮窗失败。", exception);
            return false;
        }
    }

    private void TryDockToScreenEdge()
    {
        if (_isEdgeDocked || !IsVisible || WindowState != WindowState.Normal
            || !_settings.CompactMode || !_settings.AutoHideAtScreenEdge
            || Stopwatch.GetTimestamp() < _suppressEdgeDockUntilTimestamp) return;
        var area = GetCurrentWorkingArea();
        var nearestEdge = DockEdge.Left;
        var nearestDistance = Math.Abs(Left - area.Left);
        SelectNearerEdge(DockEdge.Right, Math.Abs(area.Right - (Left + ActualWidth)));
        SelectNearerEdge(DockEdge.Top, Math.Abs(Top - area.Top));
        SelectNearerEdge(DockEdge.Bottom, Math.Abs(area.Bottom - (Top + ActualHeight)));
        if (nearestDistance > DockThreshold) return;

        _dockedEdge = nearestEdge;
        _dockAnchor = _dockedEdge is DockEdge.Left or DockEdge.Right
            ? Top + ActualHeight / 2
            : Left + ActualWidth / 2;
        _isEdgeDocked = true;
        CompactPanel.Visibility = Visibility.Collapsed;
        DockedStripPanel.Visibility = Visibility.Visible;
        ApplyDockedDimensions(area);

        void SelectNearerEdge(DockEdge edge, double distance)
        {
            if (distance >= nearestDistance) return;
            nearestEdge = edge;
            nearestDistance = distance;
        }
    }

    private void ApplyDockedDimensions(Rect area)
    {
        ResizeMode = ResizeMode.NoResize;
        if (_dockedEdge is DockEdge.Left or DockEdge.Right)
        {
            MinWidth = DockThickness;
            MinHeight = DockLength;
            Width = DockThickness;
            Height = DockLength;
            Top = Math.Clamp(_dockAnchor - DockLength / 2, area.Top, area.Bottom - DockLength);
            Left = _dockedEdge == DockEdge.Left ? area.Left : area.Right - DockThickness;
            DockedStripBorder.CornerRadius = _dockedEdge == DockEdge.Left
                ? new CornerRadius(0, 5, 5, 0)
                : new CornerRadius(5, 0, 0, 5);
        }
        else
        {
            MinWidth = DockLength;
            MinHeight = DockThickness;
            Width = DockLength;
            Height = DockThickness;
            Left = Math.Clamp(_dockAnchor - DockLength / 2, area.Left, area.Right - DockLength);
            Top = _dockedEdge == DockEdge.Top ? area.Top : area.Bottom - DockThickness;
            DockedStripBorder.CornerRadius = _dockedEdge == DockEdge.Top
                ? new CornerRadius(0, 0, 5, 5)
                : new CornerRadius(5, 5, 0, 0);
        }
    }

    private void DockedStripPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        var edge = _dockedEdge;
        var anchor = _dockAnchor;
        _isEdgeDocked = false;
        _suppressEdgeDockUntilTimestamp = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        ApplyWindowMode();

        var area = GetCurrentWorkingArea();
        switch (edge)
        {
            case DockEdge.Left:
                Left = area.Left + RestoreInset;
                Top = Math.Clamp(anchor - CompactHeight / 2, area.Top, area.Bottom - CompactHeight);
                break;
            case DockEdge.Right:
                Left = area.Right - CompactWidth - RestoreInset;
                Top = Math.Clamp(anchor - CompactHeight / 2, area.Top, area.Bottom - CompactHeight);
                break;
            case DockEdge.Top:
                Left = Math.Clamp(anchor - CompactWidth / 2, area.Left, area.Right - CompactWidth);
                Top = area.Top + RestoreInset;
                break;
            case DockEdge.Bottom:
                Left = Math.Clamp(anchor - CompactWidth / 2, area.Left, area.Right - CompactWidth);
                Top = area.Bottom - CompactHeight - RestoreInset;
                break;
        }
        e.Handled = true;
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderChart();

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e) => MinimizeToTray();

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            Dispatcher.BeginInvoke(MinimizeToTray, DispatcherPriority.Background);
    }

    private void MinimizeToTray()
    {
        _edgeDockTimer.Stop();
        Hide();
        WindowState = WindowState.Normal;
    }

    private void ShowWidget()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    private static bool TrySetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RegistryRunPath, writable: true)
                ?? throw new InvalidOperationException("无法打开 Windows 开机启动注册表项。");
            if (enabled)
            {
                var processPath = Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName
                    ?? throw new InvalidOperationException("无法确定程序路径。");
                processPath = System.IO.Path.GetFullPath(processPath);
                if (!System.IO.File.Exists(processPath))
                    throw new System.IO.FileNotFoundException("找不到当前程序文件。", processPath);
                key.SetValue(RegistryValueName, $"\"{processPath}\"");
            }
            else
            {
                key.DeleteValue(RegistryValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log("修改开机启动设置失败。", exception);
            System.Windows.MessageBox.Show($"修改开机启动失败：\n\n{exception.Message}", "CPU 网速悬浮窗",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath);
            return key?.GetValue(RegistryValueName) is string;
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log("读取开机启动设置失败。", exception);
            return false;
        }
    }

    private void KeepInsideWorkingArea()
    {
        var area = GetCurrentWorkingArea();
        var maxLeft = Math.Max(area.Left, area.Right - Width);
        var maxTop = Math.Max(area.Top, area.Bottom - Height);
        var desiredLeft = double.IsFinite(Left) ? Left : area.Right - Width - 24;
        var desiredTop = double.IsFinite(Top) ? Top : area.Top + 24;
        Left = Math.Clamp(desiredLeft, area.Left, maxLeft);
        Top = Math.Clamp(desiredTop, area.Top, maxTop);
    }

    private Rect GetCurrentWorkingArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var bounds = Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(
            bounds.Left / dpi.DpiScaleX,
            bounds.Top / dpi.DpiScaleY,
            bounds.Width / dpi.DpiScaleX,
            bounds.Height / dpi.DpiScaleY);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyClose)
        {
            e.Cancel = true;
            Hide();
            _notifyIcon.ShowBalloonTip(1500, "CPU 网速悬浮窗",
                "程序仍在托盘运行，双击托盘图标可恢复。", Forms.ToolTipIcon.Info);
            return;
        }
        base.OnClosing(e);
    }

    private void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;
        _reallyClose = true;
        _timer.Stop();
        _edgeDockTimer.Stop();
        try
        {
            _temperatureReader?.Dispose();
        }
        catch (Exception exception)
        {
            AppDiagnostics.Log("释放温度监控资源失败。", exception);
        }
        finally
        {
            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch (Exception exception)
            {
                AppDiagnostics.Log("释放托盘图标失败。", exception);
            }
            try { _contextMenu.Dispose(); }
            catch (Exception exception) { AppDiagnostics.Log("释放托盘菜单失败。", exception); }
            try { Close(); }
            catch (Exception exception) { AppDiagnostics.Log("关闭主窗口失败。", exception); }
            System.Windows.Application.Current.Shutdown();
        }
    }

    private static System.Windows.Media.Brush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private readonly record struct HistorySample(double? Download, double? Upload, long Timestamp);
    private enum DockEdge { Left, Right, Top, Bottom }
}
