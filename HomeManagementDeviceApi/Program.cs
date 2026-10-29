using HomeManagementDeviceApi;
using HomeManagementDeviceApi.Mqtt;
using Silverback.Configuration;
using Silverback.Messaging.Configuration;
using Silverback.Messaging.Messages;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // Keeps existing deployments that set ASPNETCORE_ENVIRONMENT working.
    EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
});

builder.Services.AddOptions<DeviceSettings>()
    .Bind(builder.Configuration)
    .Validate(x => !string.IsNullOrEmpty(x.GetDeviceId()), "DeviceName or DeviceId is required.")
    .ValidateOnStart();
builder.Services.Configure<MqttSettings>(builder.Configuration.GetSection("Mqtt"));
builder.Services.Configure<CommandsSettings>(builder.Configuration.GetSection("CommandsSettings"));

builder.Services.AddSystemd();
builder.Services.AddSingleton<DeviceAgent>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceAgent>());

builder.Services.AddSilverback()
    .WithConnectionToMessageBroker(options => options.AddMqtt().ConnectAfterStartup().RetryOnConnectionFailure())
    .AddBrokerClientsConfigurator<AgentBrokerClientsConfigurator>()
    .AddSingletonBrokerClientCallback(sp => sp.GetRequiredService<DeviceAgent>())
    .AddDelegateSubscriber((IInboundEnvelope<StringMessage<DeviceCommandPayload>> envelope, DeviceAgent agent) => agent.OnCommandAsync(envelope));

await builder.Build().RunAsync();
