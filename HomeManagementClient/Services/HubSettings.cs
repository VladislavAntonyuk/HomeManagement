using System.Diagnostics.CodeAnalysis;

namespace HomeManagementClient.Services;

/// <summary>
/// Address of the hub and the key the app signs in with. The key is kept in secure storage where the platform has one.
/// </summary>
public sealed class HubSettings(IPreferences preferences, ISecureStorage secureStorage)
{
    private const string HubUrlKey = "HubUrl";
    private const string ApiKeyKey = "HubApiKey";

    public const string DefaultHubUrl = "http://home-management.local";

    private bool _isLoaded;

    public event EventHandler? Changed;

    public Uri? HubUri { get; private set; }

    public string? ApiKey { get; private set; }

    [MemberNotNullWhen(true, nameof(HubUri), nameof(ApiKey))]
    public bool IsConfigured => HubUri is not null && !string.IsNullOrEmpty(ApiKey);

    public async Task LoadAsync()
    {
        if (_isLoaded)
        {
            return;
        }

        HubUri = TryParseHubUrl(preferences.Get<string?>(HubUrlKey, null), out var uri) ? uri : null;
        ApiKey = await ReadApiKeyAsync();
        _isLoaded = true;
    }

    public async Task SaveAsync(Uri hubUri, string apiKey)
    {
        preferences.Set(HubUrlKey, hubUri.ToString());
        await WriteApiKeyAsync(apiKey);
        HubUri = hubUri;
        ApiKey = apiKey;
        _isLoaded = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Accepts "host", "host:port" and full http(s) URLs.
    /// </summary>
    public static bool TryParseHubUrl(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = $"http://{text}";
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            return false;
        }

        uri = new UriBuilder(parsed) { Path = "/", Query = string.Empty, Fragment = string.Empty }.Uri;
        return true;
    }

    private async Task<string?> ReadApiKeyAsync()
    {
        try
        {
            return await secureStorage.GetAsync(ApiKeyKey);
        }
        catch (Exception)
        {
            // No secure storage on this platform (e.g. Linux GTK, unpackaged Windows) or it cannot be read
            return preferences.Get<string?>(ApiKeyKey, null);
        }
    }

    private async Task WriteApiKeyAsync(string apiKey)
    {
        try
        {
            await secureStorage.SetAsync(ApiKeyKey, apiKey);
        }
        catch (Exception)
        {
            preferences.Set(ApiKeyKey, apiKey);
        }
    }
}
