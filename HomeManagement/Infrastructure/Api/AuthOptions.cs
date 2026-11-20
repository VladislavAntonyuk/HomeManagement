namespace HomeManagement.Infrastructure.Api;

/// <summary>
/// The keys of the Auth section: Key signs in to the web UI and the app, McpKey is for AI agents.
/// </summary>
public class AuthOptions
{
    public required string Key { get; set; }

    public required string McpKey { get; set; }
}
