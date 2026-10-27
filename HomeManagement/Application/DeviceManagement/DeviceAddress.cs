using System.Net.NetworkInformation;

namespace HomeManagement.Application.DeviceManagement;

public static class DeviceAddress
{
    public static bool IsPhysicalAddress(string address) => PhysicalAddress.TryParse(address, out _);
}
