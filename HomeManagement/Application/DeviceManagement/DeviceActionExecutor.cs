using System.Diagnostics;
using System.Text;
using HomeManagement.Infrastructure.Database.Entities;

namespace HomeManagement.Application.DeviceManagement;

public interface IDeviceActionExecutor
{
    Task<DeviceActionExecutionResult> ExecuteAsync(Device device, DeviceAction action, CancellationToken token = default);
}

public record DeviceActionExecutionResult(bool IsSuccess, string Message, string? RawResponse = null);

public class DeviceActionExecutor(IHttpClientFactory httpClientFactory) : IDeviceActionExecutor
{
    public async Task<DeviceActionExecutionResult> ExecuteAsync(Device device, DeviceAction action, CancellationToken token = default)
    {
        try
        {
            if (DeviceAddress.IsPhysicalAddress(device.Address))
            {
                var output = await Process.RunAndCaptureTextAsync(new ProcessStartInfo
                {
                    FileName = action.Command,
                    Arguments = action.CommandArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }, token);

                return output.ExitStatus.ExitCode == 0
                    ? new DeviceActionExecutionResult(true, output.StandardOutput, output.StandardOutput)
                    : new DeviceActionExecutionResult(false, output.StandardError, output.StandardError);
            }

            using var httpClient = httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(device.Address);

            foreach (var config in device.Configurations)
            {
                httpClient.DefaultRequestHeaders.Add(config.Name, config.Value);
            }

            var result = action.CommandType switch
            {
                Shared.CommandType.Get => await httpClient.GetAsync(string.IsNullOrWhiteSpace(action.CommandArgs) ? action.Command : $"{action.Command}?{action.CommandArgs}", token),
                Shared.CommandType.Post => await httpClient.PostAsync(action.Command,
                    string.IsNullOrWhiteSpace(action.CommandArgs) ? null : new StringContent(action.CommandArgs, Encoding.UTF8, "application/json"),
                    token),
                _ => throw new ArgumentOutOfRangeException(nameof(action.CommandType), action.CommandType, null)
            };

            var content = await result.Content.ReadAsStringAsync(token);
            if (string.IsNullOrEmpty(content))
            {
                content = result.IsSuccessStatusCode
                    ? $"{action.Action} successfully executed"
                    : "Error has occurred";
            }

            return new DeviceActionExecutionResult(result.IsSuccessStatusCode, content, content);
        }
        catch (Exception e)
        {
            return new DeviceActionExecutionResult(false, e.Message);
        }
    }
}
