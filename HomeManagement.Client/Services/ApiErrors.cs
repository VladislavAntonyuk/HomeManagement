using System.Globalization;
using System.Net;
using System.Text.Json;
using Refit;

namespace HomeManagement.Client.Services;

public static class ApiErrors
{
    public static string ToMessage(Exception exception) => exception switch
    {
        HubNotConfiguredException e => e.Message,
        ApiException { StatusCode: HttpStatusCode.Unauthorized } => AppResources.ErrorUnauthorized,
        ApiException e when ReadProblem(e) is { } problem => problem,
        ApiException e => string.Format(CultureInfo.CurrentCulture, AppResources.ErrorStatus, (int)e.StatusCode, e.ReasonPhrase),
        HttpRequestException => AppResources.ErrorUnreachable,
        TaskCanceledException or TimeoutException => AppResources.ErrorTimeout,
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
