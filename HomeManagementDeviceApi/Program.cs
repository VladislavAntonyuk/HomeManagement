using HomeManagement.Shared;
using HomeManagementDeviceApi;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<CommandsSettings>(builder.Configuration.GetSection("CommandsSettings"));

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/info", (IConfiguration configuration) => new DeviceInfo
{
    Name = configuration.GetValue("DeviceName", string.Empty),
    Address = NetworkManager.GetAddress(),
    Metrics = new Dictionary<string, string>
    {
        { "UptimeSeconds", DeviceManager.GetUptime().ToString() },
        { "Temperature", DeviceManager.GetTemperature().ToString(CultureInfo.InvariantCulture) }
    }
});
app.MapGet("/config", (IConfiguration configuration, IOptions<CommandsSettings> commandsOptions) =>
{
    var actions = new List<DeviceAction>()
    {
        new ("INFO", CommandType.Get, "info"),
        new ("SHUTDOWN", CommandType.Post, "shutdown"),
        new ("RESTART", CommandType.Post, "restart")
    };
    actions.AddRange(commandsOptions.Value.Commands.Select(x => new DeviceAction($"Start {x.Name}", CommandType.Post, "command", JsonSerializer.Serialize(x.StartCommand))));
    actions.AddRange(commandsOptions.Value.Commands.Select(x => new DeviceAction($"Stop {x.Name}", CommandType.Post, "command", JsonSerializer.Serialize(x.StopCommand))));

    return new DeviceConfig
    {
        Name = configuration.GetValue("DeviceName", string.Empty),
        Address = NetworkManager.GetAddress(),
        Actions = actions
    };
});
app.MapPost("/command", async (Command command, CancellationToken token) =>
{
    if (string.IsNullOrWhiteSpace(command.FileName))
    {
        return "Command file name is required.";
    }

    var arguments = string.Join(' ', command.Arguments);
    var result = await Process.RunAndCaptureTextAsync(new ProcessStartInfo
    {
        FileName = command.FileName,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    }, token);

    return result.ExitStatus.ExitCode == 0 ? result.StandardOutput : result.StandardError;
});

app.MapPost("/shutdown", () =>
{
    Process.StartAndForget("poweroff");
});

app.MapPost("/restart", () =>
{
    Process.StartAndForget("reboot");
});

app.Run();