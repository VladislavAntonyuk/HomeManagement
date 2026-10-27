using HomeManagement.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Infrastructure.Extensions;

public static class DatabaseExtensions
{
    public static void AddDatabase(this WebApplicationBuilder builder)
    {
        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HomeManagement");
        Directory.CreateDirectory(dataFolder);
        var databasePath = Path.Combine(dataFolder, "home_management.db");

        builder.Services.AddDbContextFactory<HomeManagementDbContext>(options => options
            .UseSqlite($"Data Source={databasePath}", o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution));
    }
}