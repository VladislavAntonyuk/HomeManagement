using HomeManagement.Infrastructure.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeManagement.Infrastructure.Database.Configurations;

public class WorkflowConfiguration : IEntityTypeConfiguration<Workflow>
{
    public void Configure(EntityTypeBuilder<Workflow> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
        builder.Property(x => x.Description).HasMaxLength(400);
        builder.Property(x => x.ConditionOperator).IsRequired();
        builder.Property(x => x.LastTriggeredAtUtc);
        builder.Property(x => x.LastConditionMatched);

        builder.HasMany(x => x.TriggerConditions)
            .WithOne()
            .HasForeignKey(x => x.WorkflowId);

        builder.HasMany(x => x.Steps)
                    .WithOne()
                    .HasForeignKey(x => x.WorkflowId);
    }
}
public class WorkflowTriggerConditionConfiguration : IEntityTypeConfiguration<WorkflowTriggerCondition>
{
    public void Configure(EntityTypeBuilder<WorkflowTriggerCondition> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TriggerType).IsRequired();
        builder.HasOne(x => x.TriggerSourceAction)
            .WithMany()
            .HasForeignKey(x => x.TriggerSourceActionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.TriggerPropertyPath).HasMaxLength(200);
        builder.Property(x => x.TriggerExpectedValue).HasMaxLength(500);
    }
}
public class WorkflowStepConfiguration : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StepType).IsRequired();
        builder.HasOne(x => x.DeviceAction)
            .WithMany()
            .HasForeignKey(x => x.DeviceActionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.NotifyTitle).HasMaxLength(200);
        builder.Property(x => x.NotifyMessageTemplate).HasMaxLength(2000);
    }
}