using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HomeManagement.Shared;

public class NetworkManager
{
    public static string GetLocalSubnet()
    {
        var subnetBytes = GetSubnets();
        return $"{subnetBytes[0]}.{subnetBytes[1]}.{subnetBytes[2]}.";
    }

    public static string GetLocalIp()
    {
        var subnetBytes = GetSubnets();
        return $"{subnetBytes[0]}.{subnetBytes[1]}.{subnetBytes[2]}.{subnetBytes[3]}";
    }

    private static byte[] GetSubnets()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue; // skip loopback
            }

            var ipProps = ni.GetIPProperties();
            foreach (var ipInfo in ipProps.UnicastAddresses)
            {
                if (ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    var ipBytes = ipInfo.Address.GetAddressBytes();
                    return ipBytes;
                }
            }
        }

        return [0, 0, 0, 0];
    }
}
