using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
        ShowNetworkChartCheckBox.IsChecked = settings.ShowNetworkChart;
        ChartRange1Radio.IsChecked = settings.ChartRangeMinutes == 1;
        ChartRange5Radio.IsChecked = settings.ChartRangeMinutes == 5;
        ChartRange10Radio.IsChecked = settings.ChartRangeMinutes == 10;

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
        UpdateChartControls();
    }

    private void TemperatureMonitoring_Changed(object sender, RoutedEventArgs e) => UpdateTemperatureControls();

    private void ChartEnabled_Changed(object sender, RoutedEventArgs e) => UpdateChartControls();

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        for (var element = e.OriginalSource as DependencyObject; element is not null;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.ComboBox
                or System.Windows.Controls.Primitives.ScrollBar) return;
        }

        DragMove();
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateTemperatureControls()
    {
        if (TemperatureSensorComboBox is not null)
            TemperatureSensorComboBox.IsEnabled = MonitorTemperatureCheckBox.IsChecked == true;
    }

    private void UpdateChartControls()
    {
        if (ChartRangePanel is not null)
            ChartRangePanel.IsEnabled = ShowNetworkChartCheckBox.IsChecked == true;
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
            ShowNetworkChart = ShowNetworkChartCheckBox.IsChecked == true,
            ChartRangeMinutes = ChartRange10Radio.IsChecked == true ? 10
                : ChartRange5Radio.IsChecked == true ? 5 : 1
        };
        AutoStartEnabled = AutoStartCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private sealed record SensorChoice(string? Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
