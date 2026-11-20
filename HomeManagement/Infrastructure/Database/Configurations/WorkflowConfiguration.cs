using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeManagement.Infrastructure.Database.Configurations;

public class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        builder.ToTable("Workflows");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.HasIndex(x => x.Name).IsUnique();
        builder.Property(x => x.Description).HasMaxLength(400);

        builder.HasMany(x => x.Triggers)
            .WithOne()
            .HasForeignKey(x => x.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Steps)
            .WithOne()
            .HasForeignKey(x => x.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// The triggers share one table (table per hierarchy). The TriggerType numbers are stored, so they never change.
/// </summary>
public class WorkflowTriggerConfiguration : IEntityTypeConfiguration<WorkflowTrigger>
{
    public void Configure(EntityTypeBuilder<WorkflowTrigger> builder)
    {
        builder.ToTable("WorkflowTriggers");
        builder.HasKey(x => x.Id);
        builder.HasDiscriminator<int>("TriggerType")
            .HasValue<DateTrigger>(1)
            .HasValue<TimeTrigger>(2)
            .HasValue<DeviceResultTrigger>(3)
            .HasValue<DeviceStatusTrigger>(4);
    }
}

public class DateTriggerConfiguration : IEntityTypeConfiguration<DateTrigger>
{
    public void Configure(EntityTypeBuilder<DateTrigger> builder)
    {
        builder.Property(x => x.Operator).HasColumnName("Operator");
    }
}

public class TimeTriggerConfiguration : IEntityTypeConfiguration<TimeTrigger>
{
    public void Configure(EntityTypeBuilder<TimeTrigger> builder)
    {
        builder.Property(x => x.Operator).HasColumnName("Operator");
        builder.ComplexProperty(x => x.From, from =>
        {
            from.Property(x => x.Reference).HasColumnName("FromReference");
            from.Property(x => x.FixedTime).HasColumnName("FromTime");
            from.Property(x => x.OffsetMinutes).HasColumnName("FromOffsetMinutes");
        });
        builder.ComplexProperty(x => x.To, to =>
        {
            to.Property(x => x.Reference).HasColumnName("ToReference");
            to.Property(x => x.FixedTime).HasColumnName("ToTime");
            to.Property(x => x.OffsetMinutes).HasColumnName("ToOffsetMinutes");
        });
    }
}

public class DeviceResultTriggerConfiguration : IEntityTypeConfiguration<DeviceResultTrigger>
{
    public void Configure(EntityTypeBuilder<DeviceResultTrigger> builder)
    {
        builder.Property(x => x.Operator).HasColumnName("Operator");
        builder.Property(x => x.PropertyPath).HasMaxLength(200);
        builder.Property(x => x.ExpectedValue).HasMaxLength(500);
        // Device actions a workflow uses are kept even when the device stops reporting them, see DeviceRegistry.
        builder.HasOne(x => x.DeviceAction)
            .WithMany()
            .HasForeignKey(x => x.DeviceActionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class DeviceStatusTriggerConfiguration : IEntityTypeConfiguration<DeviceStatusTrigger>
{
    public void Configure(EntityTypeBuilder<DeviceStatusTrigger> builder)
    {
        builder.HasOne(x => x.Device)
            .WithMany()
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// The steps share one table (table per hierarchy). The StepType numbers are stored, so they never change.
/// </summary>
public class WorkflowStepConfiguration : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> builder)
    {
        builder.ToTable("WorkflowSteps");
        builder.HasKey(x => x.Id);
        builder.HasDiscriminator<int>("StepType")
            .HasValue<DeviceActionStep>(0)
            .HasValue<DelayStep>(1)
            .HasValue<NotifyStep>(2);
    }
}

public class DeviceActionStepConfiguration : IEntityTypeConfiguration<DeviceActionStep>
{
    public void Configure(EntityTypeBuilder<DeviceActionStep> builder)
    {
        // Device actions a workflow uses are kept even when the device stops reporting them, see DeviceRegistry.
        builder.HasOne(x => x.DeviceAction)
            .WithMany()
            .HasForeignKey(x => x.DeviceActionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class DelayStepConfiguration : IEntityTypeConfiguration<DelayStep>
{
    public void Configure(EntityTypeBuilder<DelayStep> builder)
    {
        builder.Property(x => x.Delay)
            .HasColumnName("DelaySeconds")
            .HasConversion(x => (int)x.TotalSeconds, x => TimeSpan.FromSeconds(x));
    }
}

public class NotifyStepConfiguration : IEntityTypeConfiguration<NotifyStep>
{
    public void Configure(EntityTypeBuilder<NotifyStep> builder)
    {
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.MessageTemplate).HasMaxLength(2000);
    }
}
