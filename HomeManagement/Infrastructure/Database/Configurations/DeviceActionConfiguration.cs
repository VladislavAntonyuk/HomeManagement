using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeManagement.Infrastructure.Database.Configurations;

public class DeviceActionConfiguration : IEntityTypeConfiguration<DeviceAction>
{
    public void Configure(EntityTypeBuilder<DeviceAction> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Command).IsRequired();
    }
}