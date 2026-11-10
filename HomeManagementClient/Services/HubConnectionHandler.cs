using HomeManagement.Shared.Api;

namespace HomeManagementClient.Services;

/// <summary>
/// Sends every API request to the hub currently configured in <see cref="HubSettings"/> and signs it with the API key,
/// so changing the settings takes effect without recreating the Refit clients.
/// </summary>
public sealed class HubConnectionHandler(HubSettings settings) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await settings.LoadAsync();
        if (!settings.IsConfigured || request.RequestUri is null)
        {
            throw new HubNotConfiguredException();
        }

        request.RequestUri = new Uri(settings.HubUri, request.RequestUri.PathAndQuery);
        request.Headers.Remove(ApiRoutes.ApiKeyHeader);
        request.Headers.Add(ApiRoutes.ApiKeyHeader, settings.ApiKey);
        return await base.SendAsync(request, cancellationToken);
    }
}

public sealed class HubNotConfiguredException() : InvalidOperationException("Set the hub address and key in Settings.");
