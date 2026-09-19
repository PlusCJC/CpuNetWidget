using LibreHardwareMonitor.Hardware;
using System.Diagnostics;

var computer = new Computer { IsCpuEnabled = true, IsMotherboardEnabled = true };
computer.Open();
computer.Accept(new UpdateVisitor());

foreach (var hardware in computer.Hardware)
{
    PrintHardware(hardware, string.Empty);
}
computer.Close();

Console.WriteLine("[Windows ACPI fallback]");
try
{
    var category = new PerformanceCounterCategory("Thermal Zone Information");
    foreach (var instance in category.GetInstanceNames())
    {
        using var counter = new PerformanceCounter("Thermal Zone Information", "Temperature", instance, true);
        var kelvin = counter.NextValue();
        Console.WriteLine($"  {instance} | {kelvin - 273.15f:F1} °C ({kelvin:F1} K)");
    }
}
catch (Exception exception)
{
    Console.WriteLine($"  unavailable: {exception.Message}");
}

static void PrintHardware(IHardware hardware, string indent)
{
    Console.WriteLine($"{indent}[{hardware.HardwareType}] {hardware.Name} ({hardware.Identifier})");
    foreach (var sensor in hardware.Sensors.Where(sensor => sensor.SensorType == SensorType.Temperature))
    {
        Console.WriteLine($"{indent}  {sensor.Name} | value={sensor.Value} | min={sensor.Min} | max={sensor.Max} | id={sensor.Identifier}");
    }
    foreach (var child in hardware.SubHardware)
    {
        PrintHardware(child, indent + "  ");
    }
}

sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var child in hardware.SubHardware) child.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}
