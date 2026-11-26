using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeManagement.DeviceSdk;
using HomeManagement.Shared;
using Microsoft.Extensions.Options;

namespace HomeManagement.Devices.TuyaPlug;

/// <summary>
/// A Tuya smart plug controlled through the Tuya cloud: STATUS reads the outlet, ON and OFF switch it.
/// The status is JSON like the Wi-Fi lamp's, e.g. {"status":"on"}, and "offline" when the plug is not connected to the Tuya cloud.
/// </summary>
public sealed class TuyaPlugHandler(
    TuyaCloudClient client,
    DeviceTelemetryPublisher telemetryPublisher,
    IOptions<TuyaOptions> options,
    ILogger<TuyaPlugHandler> logger) : IDeviceHandler
{
    private const string PowerKey = "power";
    private const string OnKey = "power/on";
    private const string OffKey = "power/off";

    private const string On = "on";
    private const string Off = "off";
    private const string Offline = "offline";

    private static readonly IReadOnlyList<DeviceAction> Actions =
    [
        new("ON", CommandType.Post, OnKey),
        new("OFF", CommandType.Post, OffKey),
        new("STATUS", CommandType.Get, PowerKey)
    ];

    public IReadOnlyList<DeviceAction> GetActions() => Actions;

    // The plug is reached through the Tuya cloud, so its local address is not known,
    // and the machine this agent runs on is not the plug.
    public string? GetAddress() => null;

    public IReadOnlyDictionary<string, string> GetMetrics() => FrozenDictionary<string, string>.Empty;

    public async Task<CommandResponse> ExecuteAsync(string command, CancellationToken token)
    {
        // DeviceAgent only sends commands of Post actions.
        var isOn = command == OnKey;
        var settings = options.Value;
        await client.SendCommandAsync(settings.DeviceId, settings.SwitchCode, JsonValue.Create(isOn), token);
        logger.LogInformation("Switched the plug {State}.", isOn ? On : Off);

        // The cloud accepted the command, so publish the new state now instead of at the next telemetry interval,
        // when the plug may not have reported it to the cloud yet.
        var status = FormatStatus(isOn ? On : Off);
        await telemetryPublisher.TryPublishAsync(PowerKey, status, token);
        return new CommandResponse(true, status);
    }

    public async Task<string?> ReadAsync(string key, CancellationToken token)
    {
        if (key != PowerKey)
        {
            return null;
        }

        var settings = options.Value;
        var device = await client.GetDeviceAsync(settings.DeviceId, token);
        if (!device.Online)
        {
            return FormatStatus(Offline);
        }

        var dataPoint = device.Status?.FirstOrDefault(x => x.Code == settings.SwitchCode)
            ?? throw new TuyaException($"The plug has no '{settings.SwitchCode}' data point. It has: {string.Join(", ", device.Status?.Select(x => x.Code) ?? [])}. Set Tuya:SwitchCode to the one that switches the outlet.");

        return FormatStatus(dataPoint.Value?.GetValueKind() == JsonValueKind.True ? On : Off);
    }

    private static string FormatStatus(string status) => new JsonObject { ["status"] = status }.ToJsonString();
}
