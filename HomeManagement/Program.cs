using HomeManagement.Application.Login;
using HomeManagement.Components;
using HomeManagement.Infrastructure;
using LiveStreamingServerNet.Flv.Installer;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using System.Text.Json.Serialization;
using HomeManagement.Infrastructure.Database;
using HomeManagement.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddDatabase();

builder.Services.AddMudServices();
builder.Services.AddHybridCache();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.AddAuth();
builder.AddServiceBus();
builder.AddSenders();

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
    .WithTools<HomeManagementMcpTools>();

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

app.MapMcp("/mcp").RequireAuthorization(McpApiKeyAuthenticationDefaults.PolicyName);
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .WithBrowserOptions(options =>
    {
        options.AddAutoPause();
    })
    .RequireHost(
#if DEBUG
        "localhost:5140"
#else
        "home-management.local", "100.87.210.52"
#endif
        );

app.MapReverseProxy();

await app.RunAsync();