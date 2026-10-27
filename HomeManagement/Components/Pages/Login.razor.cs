using Microsoft.AspNetCore.Components;

namespace HomeManagement.Components.Pages;

public partial class Login : ComponentBase
{
    private string? Username { get; set; }
    private string? Key { get; set; }
    private bool RememberMe { get; set; }

    [SupplyParameterFromQuery(Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery(Name = "error")]
    public string? Error { get; set; }

    [SupplyParameterFromQuery(Name = "username")]
    public string? PreviousUsername { get; set; }

    private bool HasError => !string.IsNullOrEmpty(Error);

    protected override void OnInitialized()
    {
        Username ??= PreviousUsername;
    }
}
