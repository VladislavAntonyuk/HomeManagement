namespace HomeManagement.Infrastructure.Database.Entities;

public enum WorkflowTriggerType
{
    Manual,
    DateTime,
    Sunrise,
    Sunset,
    DeviceResult
}