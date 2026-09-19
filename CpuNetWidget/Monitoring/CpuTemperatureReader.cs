using System.Diagnostics;
using LibreHardwareMonitor.Hardware;

namespace CpuNetWidget.Monitoring;

internal sealed class CpuTemperatureReader : IDisposable
{
    private const string AcpiPrefix = "acpi:";
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly object _sync = new();
    private readonly List<AcpiCounter> _acpiCounters = [];
    private bool _disposed;

    public CpuTemperatureReader()
    {
        _computer = new Computer { IsCpuEnabled = true };
        _computer.Open();
        InitializeAcpiCounters();
    }

    public IReadOnlyList<TemperatureSensorOption> GetAvailableSensors()
    {
        lock (_sync)
        {
            UpdateHardware();
            var result = GetCpuTemperatureSensors()
                .Select(sensor => new TemperatureSensorOption(
                    sensor.Identifier.ToString(), $"硬件传感器 · {sensor.Name}"))
                .ToList();
            result.AddRange(_acpiCounters.Select(counter =>
                new TemperatureSensorOption(AcpiPrefix + counter.InstanceName,
                    $"Windows ACPI · {counter.InstanceName}")));
            return result;
        }
    }

    public TemperatureReading ReadTemperature(string? selectedSensorId)
    {
        lock (_sync)
        {
            try
            {
                UpdateHardware();
                if (!string.IsNullOrWhiteSpace(selectedSensorId))
                {
                    return selectedSensorId.StartsWith(AcpiPrefix, StringComparison.OrdinalIgnoreCase)
                        ? ReadSelectedAcpiSensor(selectedSensorId[AcpiPrefix.Length..])
                        : ReadSelectedHardwareSensor(selectedSensorId);
                }

                var hardwareReading = ReadAutomaticHardwareSensor();
                if (hardwareReading.Celsius.HasValue) return hardwareReading;

                var acpiReading = ReadAutomaticAcpiSensor();
                if (acpiReading.Celsius.HasValue) return acpiReading;

                return new TemperatureReading(null, "传感器无数据（可在设置中启用管理员权限）", null);
            }
            catch (Exception exception)
            {
                return new TemperatureReading(null, $"读取失败：{exception.Message}", null);
            }
        }
    }

    private TemperatureReading ReadAutomaticHardwareSensor()
    {
        var sensor = GetCpuTemperatureSensors()
            .Where(sensor => sensor.Value.HasValue && IsPlausibleCelsius(sensor.Value.Value))
            .OrderByDescending(sensor => GetSensorPriority(sensor.Name))
            .ThenByDescending(sensor => sensor.Value)
            .FirstOrDefault();
        return sensor is null
            ? new TemperatureReading(null, "硬件传感器无数据", null)
            : new TemperatureReading(sensor.Value, $"硬件 · {sensor.Name}", sensor.Identifier.ToString());
    }

    private TemperatureReading ReadSelectedHardwareSensor(string id)
    {
        var sensor = GetCpuTemperatureSensors().FirstOrDefault(candidate =>
            candidate.Identifier.ToString().Equals(id, StringComparison.OrdinalIgnoreCase));
        if (sensor?.Value is float value && IsPlausibleCelsius(value))
            return new TemperatureReading(value, $"硬件 · {sensor.Name}", id);
        return new TemperatureReading(null, $"所选硬件传感器无数据：{sensor?.Name ?? id}", id);
    }

    private TemperatureReading ReadAutomaticAcpiSensor()
    {
        return _acpiCounters.Select(ReadAcpiCounter)
            .Where(reading => reading.Celsius.HasValue)
            .OrderByDescending(reading => GetAcpiPriority(reading.SensorName))
            .ThenByDescending(reading => reading.Celsius)
            .FirstOrDefault(new TemperatureReading(null, "Windows ACPI 温区无数据", null));
    }

    private TemperatureReading ReadSelectedAcpiSensor(string instanceName)
    {
        var counter = _acpiCounters.FirstOrDefault(candidate =>
            candidate.InstanceName.Equals(instanceName, StringComparison.OrdinalIgnoreCase));
        return counter is null
            ? new TemperatureReading(null, $"未找到 Windows ACPI 温区：{instanceName}", AcpiPrefix + instanceName)
            : ReadAcpiCounter(counter);
    }

    private static TemperatureReading ReadAcpiCounter(AcpiCounter counter)
    {
        try
        {
            var celsius = counter.Counter.NextValue() - 273.15f;
            return IsPlausibleCelsius(celsius)
                ? new TemperatureReading(celsius, $"Windows ACPI · {counter.InstanceName}", AcpiPrefix + counter.InstanceName)
                : new TemperatureReading(null, $"Windows ACPI 数据异常：{counter.InstanceName}", AcpiPrefix + counter.InstanceName);
        }
        catch
        {
            return new TemperatureReading(null, $"Windows ACPI 温区不可用：{counter.InstanceName}", AcpiPrefix + counter.InstanceName);
        }
    }

    private ISensor[] GetCpuTemperatureSensors() => _computer.Hardware
        .Where(hardware => hardware.HardwareType == HardwareType.Cpu)
        .SelectMany(GetSensorsRecursively)
        .Where(sensor => sensor.SensorType == SensorType.Temperature)
        .Where(sensor => !sensor.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    private void UpdateHardware() => _computer.Accept(_visitor);

    private void InitializeAcpiCounters()
    {
        try
        {
            var category = new PerformanceCounterCategory("Thermal Zone Information");
            foreach (var instance in category.GetInstanceNames())
            {
                _acpiCounters.Add(new AcpiCounter(instance,
                    new PerformanceCounter("Thermal Zone Information", "Temperature", instance, readOnly: true)));
            }
        }
        catch
        {
            // ACPI is an optional fallback; hardware sensors may still work.
        }
    }

    private static IEnumerable<ISensor> GetSensorsRecursively(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors) yield return sensor;
        foreach (var subHardware in hardware.SubHardware)
        foreach (var sensor in GetSensorsRecursively(subHardware))
            yield return sensor;
    }

    private static bool IsPlausibleCelsius(float value) => value is > -20 and < 150;

    private static int GetSensorPriority(string name)
    {
        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) return 98;
        if (name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)) return 90;
        if (name.Contains("Core Average", StringComparison.OrdinalIgnoreCase)) return 85;
        if (name.Contains("Core Max", StringComparison.OrdinalIgnoreCase)) return 80;
        return 10;
    }

    private static int GetAcpiPriority(string name) =>
        name.Contains("THRM", StringComparison.OrdinalIgnoreCase) ? 100 : 10;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            foreach (var counter in _acpiCounters) counter.Counter.Dispose();
            _computer.Close();
            _disposed = true;
        }
    }

    private sealed record AcpiCounter(string InstanceName, PerformanceCounter Counter);

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware) subHardware.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}

internal sealed record TemperatureSensorOption(string Id, string DisplayName);
internal readonly record struct TemperatureReading(float? Celsius, string SensorName, string? SensorId);
