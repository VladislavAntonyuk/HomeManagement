using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using HomeManagement.Shared;

namespace HomeManagement.Application.DeviceManagement;

public interface IDeviceDiscoveryService
{
    Task<List<DeviceConfig>> ScanNetworkAsync(string baseIp, TimeSpan pingTimeout, CancellationToken token);

    Task<DeviceConfig?> GetDeviceConfigAsync(string address, CancellationToken token);
}

public class DeviceDiscoveryService(IHttpClientFactory httpClientFactory) : IDeviceDiscoveryService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<List<DeviceConfig>> ScanNetworkAsync(string baseIp, TimeSpan pingTimeout, CancellationToken token)
    {
        var tasks = new List<Task<DeviceConfig?>>();

        for (var i = 1; i <= 254; i++)
        {
            tasks.Add(PingAndResolveAsync(baseIp + i, pingTimeout, token));
        }

        var results = await Task.WhenAll(tasks);
        return results.OfType<DeviceConfig>().ToList();
    }

    public async Task<DeviceConfig?> GetDeviceConfigAsync(string address, CancellationToken token)
    {
        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            return await httpClient.GetFromJsonAsync<DeviceConfig>($"{address}/config", JsonSerializerOptions, token);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<DeviceConfig?> PingAndResolveAsync(string ip, TimeSpan timeout, CancellationToken token)
    {
        using Ping ping = new();
        try
        {
            var reply = await ping.SendPingAsync(ip, timeout, cancellationToken: token);
            if (reply.Status != IPStatus.Success)
            {
                return null;
            }

            var address = $"http://{ip}";
            var config = await GetDeviceConfigAsync(address, token);
            if (config is not null)
            {
                config.Address = address;
            }

            return config;
        }
        catch
        {
            // Ignore failures
            return null;
        }
    }
}
