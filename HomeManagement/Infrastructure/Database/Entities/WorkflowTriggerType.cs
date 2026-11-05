namespace HomeManagement.Infrastructure.Database.Entities;

public enum WorkflowTriggerType
{
    Manual,
    Date,
    Time,
    DeviceResult,

    /// <summary>
    /// Matches while a device is online or offline. Runs the workflow when the device changes to that status.
    /// </summary>
    DeviceStatus
}
