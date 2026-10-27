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
    actions.AddRange(commandsOptions.Value.Commands.Select(x => new DeviceAction(x.Name, CommandType.Post, "command", JsonSerializer.Serialize(x.Command))));
    actions.AddRange(commandsOptions.Value.Commands.Select(x => new DeviceAction(x.Name, x.Method, x.Endpoint)));

    return new DeviceConfig
    {
        Name = configuration.GetValue("DeviceName", string.Empty),
        Address = NetworkManager.GetAddress(),
        Actions = actions
    };
});

foreach (var command in commandsSection.Get<CommandsSettings>()?.Commands ?? [])
{
    var method = command.Method == CommandType.Get ? HttpMethods.Get : HttpMethods.Post;
    app.MapMethods($"/{command.Endpoint}", [method], (CancellationToken token) => RunCommandAsync(command, token));
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

static async Task<IResult> RunCommandAsync(CommandInfo command, CancellationToken token)
{
    var result = await Process.RunAndCaptureTextAsync(new ProcessStartInfo
    {
        FileName = command.FileName,
        Arguments = string.Join(' ', command.Arguments),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    }, token);

    return result.ExitStatus.ExitCode == 0
        ? Results.Text(result.StandardOutput)
        : Results.Text(result.StandardError, statusCode: StatusCodes.Status500InternalServerError);
}