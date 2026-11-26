using HomeManagement.Components;
using HomeManagement.Cameras;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Extensions.Apps;
using MudBlazor.Services;
using HomeManagement.Infrastructure;
using HomeManagement.Infrastructure.Api;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Extensions;
using HomeManagement.Infrastructure.Mcp;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddDatabase();

builder.AddLocalizationSupport();
builder.Services.AddMudServices();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.AddAuth();
builder.AddServiceBus();
builder.AddSenders();

builder.AddDevices();
builder.AddInternetWatchdog();
builder.AddLiveCamera();
builder.AddWorkflow();

builder.Services.AddHttpClient();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .AddAuthorizationFilters()
    .WithTools<HomeManagementMcpTools>()
    .WithResources<HomeManagementMcpResources>()
    .WithMcpApps();

var app = builder.Build();

app.MapDefaultEndpoints();

await using (var dbContext = await app.Services.GetRequiredService<IDbContextFactory<HomeManagementDbContext>>().CreateDbContextAsync())
{
    await dbContext.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRequestLocalization();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.UseCameras();
app.MapStaticAssets();
app.MapLogin();
app.MapCultureEndpoint();
app.MapHomeApi();

app.MapMcp("/mcp")
    .RequireAuthorization(ApiAuthenticationDefaults.McpPolicyName)
    .WithMetadata(new SkipStatusCodePagesAttribute());
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .WithBrowserOptions(options => options.AddAutoPause())
    .RequireHost([AppHosts.MainHost, .. builder.Configuration.GetSection("UiHosts").Get<string[]>() ?? []]);

app.MapReverseProxy();

await app.RunAsync();