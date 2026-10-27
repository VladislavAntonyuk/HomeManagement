using HomeManagement.Application.Login;
using HomeManagement.Components;
using LiveStreamingServerNet.Flv.Installer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Extensions.Apps;
using MudBlazor.Services;
using System.Text.Json.Serialization;
using HomeManagement.Infrastructure;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Extensions;
using HomeManagement.Infrastructure.Mcp;

var builder = WebApplication.CreateBuilder(args);

builder.AddDatabase();

builder.Services.AddMudServices();
builder.Services.AddHybridCache();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.AddAuth();
builder.AddServiceBus();
builder.AddSenders();

builder.AddDevices();
builder.AddInternetWatchdog();
builder.AddLiveCamera();
builder.AddWorkflow();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .AddAuthorizationFilters()
    .WithTools<HomeManagementMcpTools>()
    .WithResources<HomeManagementMcpResources>()
    .WithMcpApps();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HomeManagementDbContext>>();
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    await dbContext.Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.UseHttpFlv();
app.MapStaticAssets();
app.MapLogin();

app.MapMcp("/mcp")
    .RequireAuthorization(McpApiKeyAuthenticationDefaults.PolicyName)
    .WithMetadata(new SkipStatusCodePagesAttribute());
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .WithBrowserOptions(options =>
    {
        options.AddAutoPause();
    })
    .RequireHost([AppHosts.MainHost, .. builder.Configuration.GetSection("UiHosts").Get<string[]>()!]);

app.MapReverseProxy();

await app.RunAsync();