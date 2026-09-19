using System.Net.NetworkInformation;

namespace CpuNetWidget.Monitoring;

internal sealed class NetworkSpeedReader
{
    private readonly Dictionary<string, Sample> _previousSamples = new(StringComparer.Ordinal);
    private DateTime _lastReadUtc = DateTime.UtcNow;

    public NetworkSpeed ReadSpeed()
    {
        var now = DateTime.UtcNow;
        var elapsedSeconds = Math.Max((now - _lastReadUtc).TotalSeconds, 0.001);
        _lastReadUtc = now;

        long receivedDelta = 0;
        long sentDelta = 0;
        var currentIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var adapter in GetActiveInternetAdapters())
        {
            try
            {
                var statistics = adapter.GetIPStatistics();
                var current = new Sample(statistics.BytesReceived, statistics.BytesSent);
                currentIds.Add(adapter.Id);

                if (_previousSamples.TryGetValue(adapter.Id, out var previous))
                {
                    receivedDelta += Math.Max(0, current.BytesReceived - previous.BytesReceived);
                    sentDelta += Math.Max(0, current.BytesSent - previous.BytesSent);
                }

                _previousSamples[adapter.Id] = current;
            }
            catch (NetworkInformationException)
            {
                // A network adapter can disappear while it is being queried.
            }
        }

        foreach (var staleId in _previousSamples.Keys.Where(id => !currentIds.Contains(id)).ToArray())
        {
            _previousSamples.Remove(staleId);
        }

        return new NetworkSpeed(receivedDelta / elapsedSeconds, sentDelta / elapsedSeconds);
    }

    public void Reset()
    {
        _previousSamples.Clear();
        _lastReadUtc = DateTime.UtcNow;
    }

    private static IEnumerable<NetworkInterface> GetActiveInternetAdapters()
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
