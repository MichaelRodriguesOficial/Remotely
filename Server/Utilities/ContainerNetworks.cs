using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Remotely.Server.Utilities;

/// <summary>
/// Redes das interfaces do próprio servidor (no Docker, as redes de ponte do contêiner).
/// </summary>
public static class ContainerNetworks
{
    public static IReadOnlyList<System.Net.IPNetwork> GetLocalNetworks()
    {
        var networks = new List<System.Net.IPNetwork>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork &&
                        address.Address.AddressFamily != AddressFamily.InterNetworkV6)
                    {
                        continue;
                    }

                    if (address.Address.IsIPv6LinkLocal)
                    {
                        continue;
                    }

                    var prefix = address.PrefixLength;
                    if (prefix <= 0)
                    {
                        continue;
                    }

                    // Nunca confia numa rede pública inteira (ex.: contêiner em modo host).
                    if (!IsPrivate(address.Address))
                    {
                        continue;
                    }

                    var network = new System.Net.IPNetwork(MaskAddress(address.Address, prefix), prefix);
                    if (!networks.Contains(network))
                    {
                        networks.Add(network);
                    }
                }
            }
        }
        catch
        {
            // Sem acesso às interfaces: fica só com o loopback (sempre confiável).
        }

        return networks;
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fc00::/7 (endereços locais únicos).
            return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168);
    }

    private static IPAddress MaskAddress(IPAddress address, int prefixLength)
    {
        var bytes = address.GetAddressBytes();
        for (var i = 0; i < bytes.Length; i++)
        {
            var bits = Math.Clamp(prefixLength - i * 8, 0, 8);
            bytes[i] &= (byte)(0xFF << (8 - bits));
        }
        return new IPAddress(bytes);
    }
}
