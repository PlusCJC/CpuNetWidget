using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CpuNetWidget.Monitoring;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CpuNetWidget;

public partial class MainWindow : Window
{
    private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "CpuNetWidget";

    private readonly CpuUsageReader _cpuUsageReader = new();
    private readonly NetworkSpeedReader _networkSpeedReader = new();
    private readonly CpuTemperatureReader? _temperatureReader;
    private readonly DispatcherTimer _timer;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _contextMenu;
    private readonly List<HistorySample> _history = [];
    private AppSettings _settings;
    private bool _reallyClose;
    private int _updateInProgress;
    private int HistoryCapacity => _settings.ChartRangeMinutes * 60;

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
            TemperatureHint.Text = "温度模块不可用";
            TemperatureHint.ToolTip = exception.Message;
        }

        _contextMenu = new Forms.ContextMenuStrip();
        _contextMenu.Items.Add("显示悬浮窗", null, (_, _) => ShowWidget());
        _contextMenu.Items.Add("隐藏悬浮窗", null, (_, _) => Dispatcher.Invoke(Hide));
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

        Loaded += async (_, _) =>
        {
            KeepInsideWorkingArea();
            ApplyMonitoringVisuals();
            _timer.Start();
            await RefreshMetricsAsync();
        };
    }

    private async Task RefreshMetricsAsync()
    {
        if (Interlocked.Exchange(ref _updateInProgress, 1) == 1) return;

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
                    _settings.MonitorTemperature ? "温度模块不可用" : "监控已关闭", null);

            UpdateMetricValues(cpuUsage, temperature, network);
            AddHistory(new HistorySample(
                cpuUsage,
                temperature.Celsius,
                _settings.MonitorDownload ? network?.DownloadBytesPerSecond : null,
                _settings.MonitorUpload ? network?.UploadBytesPerSecond : null));
            UpdateTrayText(cpuUsage, temperature, network);
        }
        finally
        {
            Interlocked.Exchange(ref _updateInProgress, 0);
        }
    }

    private void UpdateMetricValues(double? cpuUsage, TemperatureReading temperature, NetworkSpeed? network)
    {
        if (_settings.MonitorCpu && cpuUsage.HasValue)
        {
            CpuText.Text = $"{cpuUsage:0}%";
            CpuProgress.Value = cpuUsage.Value;
        }

        if (_settings.MonitorTemperature)
        {
            if (temperature.Celsius.HasValue)
            {
                var value = temperature.Celsius.Value;
                TemperatureText.Text = $"{value:0}°C";
                TemperatureText.Foreground = value switch
                {
                    >= 90 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 92, 92)),
                    >= 75 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 184, 108)),
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

        if (_settings.MonitorDownload && network.HasValue)
            DownloadText.Text = FormatSpeed(network.Value.DownloadBytesPerSecond);
        if (_settings.MonitorUpload && network.HasValue)
            UploadText.Text = FormatSpeed(network.Value.UploadBytesPerSecond);
    }

    private void ApplyMonitoringVisuals()
    {
        SetPanelState(CpuPanel, CpuText, _settings.MonitorCpu, "--%", CpuProgress);
        SetPanelState(TemperaturePanel, TemperatureText, _settings.MonitorTemperature, "--°C");
        SetPanelState(DownloadPanel, DownloadText, _settings.MonitorDownload, "0 B/s");
        SetPanelState(UploadPanel, UploadText, _settings.MonitorUpload, "0 B/s");
        TemperatureHint.Text = _settings.MonitorTemperature ? "正在读取传感器" : "监控已关闭";

        CpuLine.Visibility = _settings.MonitorCpu ? Visibility.Visible : Visibility.Collapsed;
        TemperatureLine.Visibility = _settings.MonitorTemperature ? Visibility.Visible : Visibility.Collapsed;
        DownloadLine.Visibility = _settings.MonitorDownload ? Visibility.Visible : Visibility.Collapsed;
        UploadLine.Visibility = _settings.MonitorUpload ? Visibility.Visible : Visibility.Collapsed;
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
        if (_history.Count > HistoryCapacity) _history.RemoveAt(0);
        RenderChart();
    }

    private void RenderChart()
    {
        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        if (width <= 1 || height <= 1 || _history.Count == 0) return;

        ChartTitleText.Text = $"实时曲线 · 最近 {_settings.ChartRangeMinutes} 分钟";

        var networkMaximum = _history
            .SelectMany(sample => new[] { sample.Download, sample.Upload })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty(1024)
            .Max();
        networkMaximum = Math.Max(1024, networkMaximum);

        CpuLine.Points = BuildPoints(sample => sample.Cpu, value => value / 100.0, width, height);
        TemperatureLine.Points = BuildPoints(sample => sample.Temperature,
            value => (value - 20.0) / 80.0, width, height);
        DownloadLine.Points = BuildPoints(sample => sample.Download,
            value => value / networkMaximum, width, height);
        UploadLine.Points = BuildPoints(sample => sample.Upload,
            value => value / networkMaximum, width, height);
        NetworkScaleText.Text = _settings.MonitorDownload || _settings.MonitorUpload
            ? $"网络峰值 {FormatSpeed(networkMaximum)}"
            : "网络监控已关闭";
    }

    private PointCollection BuildPoints(Func<HistorySample, double?> selector,
        Func<double, double> normalize, double width, double height)
    {
        var points = new PointCollection();
        var leadingEmptySlots = HistoryCapacity - _history.Count;
        for (var index = 0; index < _history.Count; index++)
        {
            var value = selector(_history[index]);
            if (!value.HasValue) continue;
            var x = HistoryCapacity == 1
                ? width
                : (leadingEmptySlots + index) * width / (HistoryCapacity - 1);
            var normalized = Math.Clamp(normalize(value.Value), 0, 1);
            points.Add(new System.Windows.Point(x, height * (1 - normalized)));
        }
        return points;
    }

    private async Task OpenSettingsAsync()
    {
        IReadOnlyList<TemperatureSensorOption> sensors = [];
        if (_temperatureReader is not null)
        {
            try { sensors = await Task.Run(_temperatureReader.GetAvailableSensors); }
            catch { /* The settings window can still open without a sensor list. */ }
        }

        var oldSettings = _settings;
        var dialog = new SettingsWindow(_settings, sensors, IsAutoStartEnabled()) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (!TrySetAutoStart(dialog.AutoStartEnabled)) return;

        _settings = dialog.ResultSettings;
        _settings.Save();
        Topmost = _settings.AlwaysOnTop;
        _cpuUsageReader.Reset();
        _networkSpeedReader.Reset();
        _history.Clear();
        ApplyMonitoringVisuals();
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

    private void UpdateTrayText(double? cpuUsage, TemperatureReading temperature, NetworkSpeed? network)
    {
        var parts = new List<string>();
        if (_settings.MonitorCpu && cpuUsage.HasValue) parts.Add($"CPU {cpuUsage:0}%");
        if (_settings.MonitorTemperature) parts.Add(temperature.Celsius.HasValue ? $"{temperature.Celsius:0}°C" : "温度 --");
        if (_settings.MonitorDownload && network.HasValue) parts.Add($"↓ {FormatSpeed(network.Value.DownloadBytesPerSecond)}");
        if (_settings.MonitorUpload && network.HasValue) parts.Add($"↑ {FormatSpeed(network.Value.UploadBytesPerSecond)}");
        _notifyIcon.Text = TruncateTrayText(parts.Count > 0 ? string.Join("  ", parts) : "CPU 网速悬浮窗 · 监控已关闭");
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:0} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:0.0} KB/s";
        if (bytesPerSecond < 1024 * 1024 * 1024) return $"{bytesPerSecond / 1024 / 1024:0.0} MB/s";
        return $"{bytesPerSecond / 1024 / 1024 / 1024:0.00} GB/s";
    }

    private static string TruncateTrayText(string text) => text.Length <= 63 ? text : text[..63];

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var point = PointToScreen(new System.Windows.Point(ActualWidth - 12, 34));
        _contextMenu.Show((int)point.X, (int)point.Y);
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderChart();

    private void MinimizeToTray_Click(object sender, RoutedEventArgs e) => MinimizeToTray();

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            Dispatcher.BeginInvoke(MinimizeToTray, DispatcherPriority.Background);
    }

    private void MinimizeToTray()
    {
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
                ?? Registry.CurrentUser.CreateSubKey(RegistryRunPath, writable: true);
            if (enabled)
            {
                var processPath = Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName
                    ?? throw new InvalidOperationException("无法确定程序路径。");
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
        catch { return false; }
    }

    private void KeepInsideWorkingArea()
    {
        var area = SystemParameters.WorkArea;
        if (double.IsNaN(Left) || Left < area.Left || Left + Width > area.Right)
            Left = area.Right - Width - 24;
        if (double.IsNaN(Top) || Top < area.Top || Top + Height > area.Bottom)
            Top = area.Top + 24;
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
        _reallyClose = true;
        _timer.Stop();
        _temperatureReader?.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private readonly record struct HistorySample(double? Cpu, double? Temperature, double? Download, double? Upload);
}
