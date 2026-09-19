using System.Diagnostics;
using System.Net.NetworkInformation;

namespace CpuNetWidget.Monitoring;

internal sealed class NetworkSpeedReader
{
    private readonly Dictionary<string, Sample> _previousSamples = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loggedAdapterFailures = new(StringComparer.Ordinal);
    private long _lastReadTimestamp = Stopwatch.GetTimestamp();
    private long _lastEnumerationFailureLogTimestamp;

    public NetworkSpeed? ReadSpeed()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = Math.Max(Stopwatch.GetElapsedTime(_lastReadTimestamp, now).TotalSeconds, 0.001);

        double receivedDelta = 0;
        double sentDelta = 0;
        var currentIds = new HashSet<string>(StringComparer.Ordinal);

        NetworkInterface[] adapters;
        try
        {
            adapters = GetActiveInternetAdapters();
        }
        catch (Exception exception) when (exception is NetworkInformationException
                                          or PlatformNotSupportedException
                                          or InvalidOperationException)
        {
            if (_lastEnumerationFailureLogTimestamp == 0
                || Stopwatch.GetElapsedTime(_lastEnumerationFailureLogTimestamp, now) >= TimeSpan.FromMinutes(1))
            {
                AppDiagnostics.Log("枚举网络适配器失败。", exception);
                _lastEnumerationFailureLogTimestamp = now;
            }
            return null;
        }

        foreach (var adapter in adapters)
        {
            currentIds.Add(adapter.Id);
            try
            {
                var statistics = adapter.GetIPStatistics();
                var current = new Sample(statistics.BytesReceived, statistics.BytesSent);

                if (_previousSamples.TryGetValue(adapter.Id, out var previous))
                {
                    receivedDelta += Math.Max(0, current.BytesReceived - previous.BytesReceived);
                    sentDelta += Math.Max(0, current.BytesSent - previous.BytesSent);
                }

                _previousSamples[adapter.Id] = current;
                _loggedAdapterFailures.Remove(adapter.Id);
            }
            catch (Exception exception) when (exception is NetworkInformationException
                                               or InvalidOperationException
                                               or PlatformNotSupportedException)
            {
                // A network adapter can disappear while it is being queried.
                _previousSamples.Remove(adapter.Id);
                if (_loggedAdapterFailures.Add(adapter.Id))
                    AppDiagnostics.Log($"读取网络适配器失败：{adapter.Name}", exception);
            }
        }

        foreach (var staleId in _previousSamples.Keys.Where(id => !currentIds.Contains(id)).ToArray())
        {
            _previousSamples.Remove(staleId);
            _loggedAdapterFailures.Remove(staleId);
        }

        _lastReadTimestamp = now;
        return new NetworkSpeed(receivedDelta / elapsedSeconds, sentDelta / elapsedSeconds);
    }

    public void Reset()
    {
        _previousSamples.Clear();
        _loggedAdapterFailures.Clear();
        _lastReadTimestamp = Stopwatch.GetTimestamp();
        _lastEnumerationFailureLogTimestamp = 0;
    }

    private static NetworkInterface[] GetActiveInternetAdapters()
    {
        var active = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .Where(adapter => adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                and not NetworkInterfaceType.Tunnel)
            .ToArray();

        var adaptersWithGateway = active.Where(HasGateway).ToArray();
        return adaptersWithGateway.Length > 0 ? adaptersWithGateway : active;
    }

    private static bool HasGateway(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GatewayAddresses
                .Any(gateway => !gateway.Address.Equals(System.Net.IPAddress.Any)
                    && !gateway.Address.Equals(System.Net.IPAddress.IPv6Any));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    private readonly record struct Sample(long BytesReceived, long BytesSent);
}

internal readonly record struct NetworkSpeed(double DownloadBytesPerSecond, double UploadBytesPerSecond);
