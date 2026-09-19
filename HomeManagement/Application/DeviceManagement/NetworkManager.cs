using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using HomeManagement.Shared;

namespace HomeManagement.Application.DeviceManagement;

public class NetworkManager
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<List<DeviceConfig>> ScanNetworkAsync(string baseIp, int timeout, CancellationToken token)
    {
        var tasks = new List<Task<DeviceConfig?>>();

        for (var i = 1; i <= 254; i++)
        {
            var ip = baseIp + i;
            tasks.Add(PingAndResolveAsync(ip, timeout, token));
        }

        var results = await Task.WhenAll(tasks);
        return results.OfType<DeviceConfig>().ToList();
    }

    private static async Task<DeviceConfig?> PingAndResolveAsync(string ip, int timeout, CancellationToken token)
    {
        using Ping ping = new();
        try
        {
            var reply = await ping.SendPingAsync(ip, TimeSpan.FromMilliseconds(timeout), cancellationToken: token);
            if (reply.Status == IPStatus.Success)
            {
                return await GetDeviceConfigAsync($"http://{ip}", token);
            }
        }
        catch
        {
            // Ignore failures
        }

        return null;
    }

    public static async Task<DeviceConfig?> GetDeviceConfigAsync(string address, CancellationToken token)
    {
        try
        {
            using var httpClient = new HttpClient();
            return await httpClient.GetFromJsonAsync<DeviceConfig>($"{address}/config", JsonSerializerOptions, token);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
