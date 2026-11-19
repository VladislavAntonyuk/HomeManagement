using HomeManagement.DeviceSdk;
using HomeManagement.Devices.ProcessDevice;
using Silverback.Configuration;
using Silverback.Messaging.Configuration;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<CommandsSettings>(builder.Configuration.GetSection("CommandsSettings"));
builder.Services.AddSystemd();

builder.Services.AddSilverback()
    .WithConnectionToMessageBroker(options => options.AddMqtt().ConnectAfterStartup().RetryOnConnectionFailure());

// DeviceName, DeviceId, TelemetryIntervalSeconds and the Mqtt section are read from the root of the configuration.
builder.Services.AddDeviceAgent<ProcessDeviceHandler>(builder.Configuration.Bind);

await builder.Build().RunAsync();
