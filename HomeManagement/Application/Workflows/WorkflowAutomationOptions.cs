namespace HomeManagement.Application.Workflows;

public class WorkflowAutomationOptions
{
    public bool Enabled { get; set; }

    public int CheckIntervalSeconds { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string TimeZoneId { get; set; }

    public int SunriseAdjustmentMinutes { get; set; }

    public int SunsetAdjustmentMinutes { get; set; }
}