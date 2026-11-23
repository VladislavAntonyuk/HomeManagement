using Microsoft.AspNetCore.Components;

namespace HomeManagement.Components.Layout;

public partial class NavMenu(NavigationManager navigationManager) : ComponentBase
{
    private readonly Uri _baseUri = new(navigationManager.BaseUri);

    private static string Initial(string? name)
        => string.IsNullOrWhiteSpace(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString();
}
