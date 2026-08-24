using HomeManagement.Infrastructure.Database.Configurations;
using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using DeviceConfiguration = HomeManagement.Infrastructure.Database.Configurations.DeviceConfiguration;

namespace HomeManagement.Infrastructure.Database;

public class HomeManagementDbContext(DbContextOptions<HomeManagementDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new DeviceConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowTriggerConditionConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowStepConfiguration());
        modelBuilder.ApplyConfiguration(new AppSettingsConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
