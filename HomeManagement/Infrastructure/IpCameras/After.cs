namespace HomeManagement.Infrastructure.IpCameras;

public class After
{
    public string Id { get; set; }
    public string Camera { get; set; }
    public string Label { get; set; }
    public float TopScore { get; set; }
    public bool HasSnapshot { get; set; }
}