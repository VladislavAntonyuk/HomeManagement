using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeManagement.Infrastructure.Database.Configurations;

public class DeviceConfigurationConfiguration : IEntityTypeConfiguration<HomeManagement.Infrastructure.Database.Entities.DeviceConfiguration>
{
    public void Configure(EntityTypeBuilder<HomeManagement.Infrastructure.Database.Entities.DeviceConfiguration> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Value).IsRequired();
    }
}