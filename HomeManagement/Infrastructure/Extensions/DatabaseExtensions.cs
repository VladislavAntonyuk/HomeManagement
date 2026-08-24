using HomeManagement.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Infrastructure.Extensions;

public static class DatabaseExtensions
{
    public static void AddDatabase(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContextFactory<HomeManagementDbContext>(options => options
            .UseSqlite("Data Source=home_management.db", o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution));
    }
}