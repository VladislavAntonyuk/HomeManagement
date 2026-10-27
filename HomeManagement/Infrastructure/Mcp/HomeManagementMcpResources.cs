using System.ComponentModel;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace HomeManagement.Infrastructure.Mcp;

[McpServerResourceType]
public class HomeManagementMcpResources
{
    private static readonly Lazy<string> DashboardHtml = new(() => ReadEmbedded("HomeManagement.Mcp.dashboard.html"));

    [McpServerResource(UriTemplate = HomeManagementMcpTools.DashboardUri, Name = "home_dashboard", Title = "Home dashboard", MimeType = McpApps.HtmlMimeType)]
    [Description("Interactive home dashboard rendered by MCP hosts that support MCP Apps")]
    public static string Dashboard() => DashboardHtml.Value;

    private static string ReadEmbedded(string name)
    {
        using var stream = typeof(HomeManagementMcpResources).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
