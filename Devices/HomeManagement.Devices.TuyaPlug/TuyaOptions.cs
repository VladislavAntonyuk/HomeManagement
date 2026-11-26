namespace HomeManagement.Devices.TuyaPlug;

/// <summary>
/// Access to the Tuya Cloud project the plug is linked to, read from the <c>Tuya</c> configuration section.
/// </summary>
public class TuyaOptions
{
    public const string SectionName = "Tuya";

    /// <summary>
    /// OpenAPI endpoint of the data center of the Tuya account, e.g. https://openapi.tuyaeu.com for Central Europe.
    /// </summary>
    public Uri BaseUrl { get; set; } = new("https://openapi.tuyaeu.com");

    /// <summary>
    /// Access ID (client id) of the cloud project.
    /// </summary>
    public string AccessId { get; set; } = string.Empty;

    /// <summary>
    /// Access Secret (client secret) of the cloud project.
    /// </summary>
    public string AccessSecret { get; set; } = string.Empty;

    /// <summary>
    /// Tuya id of the plug, shown on the Devices tab of the cloud project.
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// Data point that switches the outlet. Single-outlet plugs use switch_1.
    /// </summary>
    public string SwitchCode { get; set; } = "switch_1";
}
