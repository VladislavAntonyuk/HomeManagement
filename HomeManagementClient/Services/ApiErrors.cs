using System.Net;
using System.Text.Json;
using Refit;

namespace HomeManagementClient.Services;

public static class ApiErrors
{
    /// <summary>
    /// Turns a failed hub call into a message for the user.
    /// </summary>
    public static string ToMessage(Exception exception) => exception switch
    {
        HubNotConfiguredException e => e.Message,
        ApiException { StatusCode: HttpStatusCode.Unauthorized } => "The hub rejected the key. Check it in Settings.",
        ApiException e when ReadProblem(e) is { } problem => problem,
        ApiException e => $"The hub answered {(int)e.StatusCode} {e.ReasonPhrase}.",
        HttpRequestException => "The hub is not reachable. Check the address in Settings and your network.",
        TaskCanceledException or TimeoutException => "The hub did not answer in time.",
        _ => exception.Message
    };

    private static string? ReadProblem(ApiException exception)
    {
        if (string.IsNullOrEmpty(exception.Content))
        {
            return null;
        }

        try
        {
            var problem = JsonSerializer.Deserialize(exception.Content, ApiJsonContext.Default.ProblemDetailsDto);
            return problem?.Detail ?? problem?.Title;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
