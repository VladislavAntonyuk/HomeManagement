using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Infrastructure.Database;

public class HomeManagementDbContext(DbContextOptions<HomeManagementDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<DashboardWidget> DashboardWidgets => Set<DashboardWidget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Every IEntityTypeConfiguration in this assembly, so a new one cannot be forgotten.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HomeManagementDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
