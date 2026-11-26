using HomeManagement.DeviceSdk;
using HomeManagement.Devices.TuyaPlug;
using Silverback.Configuration;
using Silverback.Messaging.Configuration;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSystemd();

builder.Services.AddOptions<TuyaOptions>()
    .Bind(builder.Configuration.GetSection(TuyaOptions.SectionName))
    .Validate(x => !string.IsNullOrWhiteSpace(x.AccessId), "Tuya:AccessId is required.")
    .Validate(x => !string.IsNullOrWhiteSpace(x.AccessSecret), "Tuya:AccessSecret is required.")
    .Validate(x => !string.IsNullOrWhiteSpace(x.DeviceId), "Tuya:DeviceId is required.")
    .Validate(x => !string.IsNullOrWhiteSpace(x.SwitchCode), "Tuya:SwitchCode is required.")
    .ValidateOnStart();

builder.Services.AddHttpClient<TuyaCloudClient>();

builder.Services.AddSilverback()
    .WithConnectionToMessageBroker(options => options.AddMqtt().ConnectAfterStartup().RetryOnConnectionFailure());

// DeviceName, DeviceId and TelemetryIntervalSeconds are read from the root of the configuration, the broker from ConnectionStrings:mqtt.
builder.Services.AddDeviceAgent<TuyaPlugHandler>(builder.Configuration.Bind);

await builder.Build().RunAsync();
