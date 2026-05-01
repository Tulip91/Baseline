using System.Net.NetworkInformation;
using BaseLine.Core;
using BaseLine.Infrastructure;

namespace BaseLine.Services;

public sealed class NetworkDiscoveryService
{
    private readonly IRegistryAccessor _registryAccessor;

    public NetworkDiscoveryService(IRegistryAccessor registryAccessor)
    {
        _registryAccessor = registryAccessor;
    }

    public IReadOnlyList<NetworkAdapterProfile> GetActiveAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                              adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .Select(ToProfile)
            .OrderByDescending(adapter => adapter.IsActive)
            .ThenBy(adapter => adapter.Name)
            .ToList();
    }

    public NetworkAdapterProfile? GetPreferredAdapter(string? preferredAdapterId)
    {
        var adapters = GetActiveAdapters();
        if (!string.IsNullOrWhiteSpace(preferredAdapterId))
        {
            var exact = adapters.FirstOrDefault(adapter => string.Equals(adapter.AdapterId, preferredAdapterId, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        return adapters.FirstOrDefault(adapter => adapter.IsActive) ?? adapters.FirstOrDefault();
    }

    private NetworkAdapterProfile ToProfile(NetworkInterface adapter)
    {
        var properties = adapter.GetIPProperties();
        var ipv4 = properties.GetIPv4Properties();
        var guid = adapter.Id.Trim('{', '}');
        var path = $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{{{guid}}}";
        var dnsServers = properties.DnsAddresses.Select(address => address.ToString()).ToList();
        var explicitDns = _registryAccessor.ReadValue(RegistryRoot.LocalMachine, path, "NameServer")?.StringValue;
        if (!string.IsNullOrWhiteSpace(explicitDns))
        {
            dnsServers = explicitDns.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        return new NetworkAdapterProfile
        {
            AdapterId = adapter.Id,
            Name = adapter.Name,
            Description = adapter.Description,
            IsActive = properties.GatewayAddresses.Count > 0 || properties.UnicastAddresses.Count > 0,
            InterfaceRegistryPath = path,
            DnsServers = dnsServers,
            IsDhcpEnabled = _registryAccessor.ReadValue(RegistryRoot.LocalMachine, path, "EnableDHCP")?.NumericValue == 1,
            InterfaceMetric = ConvertMetric(_registryAccessor.ReadValue(RegistryRoot.LocalMachine, path, "InterfaceMetric")?.NumericValue, ipv4?.Index)
        };
    }

    private static int? ConvertMetric(long? registryValue, int? fallback)
    {
        return registryValue is not null ? Convert.ToInt32(registryValue.Value) : fallback;
    }
}
