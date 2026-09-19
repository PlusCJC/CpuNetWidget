using Microsoft.Win32;

namespace CpuNetWidget;

internal sealed class AppSettings
{
    private const string RegistryPath = @"Software\CpuNetWidget";

    public bool MonitorCpu { get; set; } = true;
    public bool MonitorTemperature { get; set; } = true;
    public bool MonitorDownload { get; set; } = true;
    public bool MonitorUpload { get; set; } = true;
    public string? TemperatureSensorId { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool RunAsAdministrator { get; set; }
    public int ChartRangeMinutes { get; set; } = 1;
    public bool ShowNetworkChart { get; set; } = true;
    public bool CompactMode { get; set; } = true;
    public bool AutoHideAtScreenEdge { get; set; } = true;

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            if (key is null) return settings;

            settings.MonitorCpu = ReadBoolean(key, nameof(MonitorCpu), true);
            settings.MonitorTemperature = ReadBoolean(key, nameof(MonitorTemperature), true);
            settings.MonitorDownload = ReadBoolean(key, nameof(MonitorDownload), true);
            settings.MonitorUpload = ReadBoolean(key, nameof(MonitorUpload), true);
            settings.TemperatureSensorId = key.GetValue(nameof(TemperatureSensorId)) as string;
            settings.AlwaysOnTop = ReadBoolean(key, nameof(AlwaysOnTop), true);
            settings.RunAsAdministrator = ReadBoolean(key, nameof(RunAsAdministrator), false);
            settings.ChartRangeMinutes = ReadChartRange(key);
            settings.ShowNetworkChart = ReadBoolean(key, nameof(ShowNetworkChart), true);
            settings.CompactMode = ReadBoolean(key, nameof(CompactMode), true);
            settings.AutoHideAtScreenEdge = ReadBoolean(key, nameof(AutoHideAtScreenEdge), true);
        }
        catch (Exception exception)
        {
            // Invalid or inaccessible settings should never prevent startup.
            AppDiagnostics.Log("读取注册表设置失败，已使用默认设置。", exception);
        }

        return settings;
    }

    public bool Save()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
                ?? throw new InvalidOperationException("无法创建设置注册表项。");
            key.SetValue(nameof(MonitorCpu), MonitorCpu ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(nameof(MonitorTemperature), MonitorTemperature ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(nameof(MonitorDownload), MonitorDownload ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(nameof(MonitorUpload), MonitorUpload ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(nameof(AlwaysOnTop), AlwaysOnTop ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(nameof(RunAsAdministrator), RunAsAdministrator ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(nameof(ChartRangeMinutes), ChartRangeMinutes, RegistryValueKind.DWord);
            key.SetValue(nameof(ShowNetworkChart), ShowNetworkChart ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(nameof(CompactMode), CompactMode ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(nameof(AutoHideAtScreenEdge), AutoHideAtScreenEdge ? 1 : 0, RegistryValueKind.DWord);

            if (string.IsNullOrWhiteSpace(TemperatureSensorId))
                key.DeleteValue(nameof(TemperatureSensorId), throwOnMissingValue: false);
            else
                key.SetValue(nameof(TemperatureSensorId), TemperatureSensorId, RegistryValueKind.String);
            return true;
        }
        catch (Exception exception)
        {
            // Monitoring still works with in-memory settings if persistence fails.
            AppDiagnostics.Log("保存注册表设置失败。", exception);
            return false;
        }
    }

    private static bool ReadBoolean(RegistryKey key, string name, bool defaultValue) => key.GetValue(name) switch
    {
        int value => value != 0,
        long value => value != 0,
        _ => defaultValue
    };

    private static int ReadChartRange(RegistryKey key)
    {
        var value = key.GetValue(nameof(ChartRangeMinutes)) switch
        {
            int minutes => minutes,
            long minutes when minutes is >= int.MinValue and <= int.MaxValue => (int)minutes,
            _ => 1
        };
        return value is 1 or 5 or 10 ? value : 1;
    }
}
