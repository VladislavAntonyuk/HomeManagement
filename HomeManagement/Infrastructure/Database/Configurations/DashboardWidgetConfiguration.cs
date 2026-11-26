using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeManagement.Infrastructure.Database.Configurations;

public class DashboardWidgetConfiguration : IEntityTypeConfiguration<DashboardWidget>
{
    public void Configure(EntityTypeBuilder<DashboardWidget> builder)
    {
        builder.ToTable("DashboardWidgets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(100);

        // The properties are only read with their widget, so one JSON column is enough.
        builder.ComplexCollection(x => x.Properties, properties => properties.ToJson());

        // Device actions a widget shows are kept even when the device stops reporting them, see DeviceRegistry.
        // Deleting the device removes its widgets.
        builder.HasOne(x => x.DeviceAction)
            .WithMany()
            .HasForeignKey(x => x.DeviceActionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
