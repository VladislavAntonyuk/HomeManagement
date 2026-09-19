namespace HomeManagement.Infrastructure.IpCameras;

public class After
{
    public required string Id { get; set; }
    public required string Camera { get; set; }
    public string? Label { get; set; }
    public float TopScore { get; set; }
    public bool HasSnapshot { get; set; }
}