using System.Windows;
using CpuNetWidget.Monitoring;

namespace CpuNetWidget;

public partial class SettingsWindow : Window
{
    internal AppSettings ResultSettings { get; private set; }
    internal bool AutoStartEnabled { get; private set; }

    internal SettingsWindow(
        AppSettings settings,
        IReadOnlyList<TemperatureSensorOption> sensors,
        bool autoStartEnabled)
    {
        InitializeComponent();
        ResultSettings = settings;

        MonitorCpuCheckBox.IsChecked = settings.MonitorCpu;
        MonitorTemperatureCheckBox.IsChecked = settings.MonitorTemperature;
        MonitorDownloadCheckBox.IsChecked = settings.MonitorDownload;
        MonitorUploadCheckBox.IsChecked = settings.MonitorUpload;
        AdministratorCheckBox.IsChecked = settings.RunAsAdministrator;
        TopmostCheckBox.IsChecked = settings.AlwaysOnTop;
        AutoStartCheckBox.IsChecked = autoStartEnabled;

        var chartRanges = new[]
        {
            new ChartRangeChoice(1, "1 分钟"),
            new ChartRangeChoice(5, "5 分钟"),
            new ChartRangeChoice(10, "10 分钟")
        };
        ChartRangeComboBox.ItemsSource = chartRanges;
        ChartRangeComboBox.SelectedItem = chartRanges.First(choice => choice.Minutes == settings.ChartRangeMinutes);

        var choices = new List<SensorChoice> { new(null, "自动选择（推荐）") };
        choices.AddRange(sensors.Select(sensor => new SensorChoice(sensor.Id, sensor.DisplayName)));
        if (!string.IsNullOrWhiteSpace(settings.TemperatureSensorId)
            && choices.All(choice => !string.Equals(choice.Id, settings.TemperatureSensorId, StringComparison.OrdinalIgnoreCase)))
        {
            choices.Add(new SensorChoice(settings.TemperatureSensorId, "之前选择的传感器（当前未检测到）"));
        }

        TemperatureSensorComboBox.ItemsSource = choices;
        TemperatureSensorComboBox.SelectedItem = choices.FirstOrDefault(choice =>
            string.Equals(choice.Id, settings.TemperatureSensorId, StringComparison.OrdinalIgnoreCase)) ?? choices[0];

        PrivilegeStatusText.Text = PrivilegeHelper.IsAdministrator()
            ? "当前状态：正在使用管理员权限运行"
            : "当前状态：普通用户权限";
        UpdateTemperatureControls();
    }

    private void TemperatureMonitoring_Changed(object sender, RoutedEventArgs e) => UpdateTemperatureControls();

    private void UpdateTemperatureControls()
    {
        if (TemperatureSensorComboBox is not null)
            TemperatureSensorComboBox.IsEnabled = MonitorTemperatureCheckBox.IsChecked == true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ResultSettings = new AppSettings
        {
            MonitorCpu = MonitorCpuCheckBox.IsChecked == true,
            MonitorTemperature = MonitorTemperatureCheckBox.IsChecked == true,
            MonitorDownload = MonitorDownloadCheckBox.IsChecked == true,
            MonitorUpload = MonitorUploadCheckBox.IsChecked == true,
            TemperatureSensorId = (TemperatureSensorComboBox.SelectedItem as SensorChoice)?.Id,
            RunAsAdministrator = AdministratorCheckBox.IsChecked == true,
            AlwaysOnTop = TopmostCheckBox.IsChecked == true,
            ChartRangeMinutes = (ChartRangeComboBox.SelectedItem as ChartRangeChoice)?.Minutes ?? 1
        };
        AutoStartEnabled = AutoStartCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private sealed record SensorChoice(string? Id, string DisplayName);
    private sealed record ChartRangeChoice(int Minutes, string DisplayName);
}
