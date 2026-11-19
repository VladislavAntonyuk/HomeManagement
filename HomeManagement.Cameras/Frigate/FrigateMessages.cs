namespace HomeManagement.Cameras.Frigate;

/// <summary>
/// Discriminator of the string payload of <see cref="FrigateTopics.Available"/>.
/// </summary>
public sealed class FrigateAvailabilityPayload
{
    private FrigateAvailabilityPayload()
    {
    }
}

/// <summary>
/// Discriminator of the string payload of the Frigate state topics, e.g. <see cref="FrigateTopics.ProfileState"/>.
/// </summary>
public sealed class FrigateStatePayload
{
    private FrigateStatePayload()
    {
    }
}

/// <summary>
/// Discriminator of the string payload sent to Frigate set topics, e.g. <see cref="FrigateTopics.ProfileSet"/>.
/// </summary>
public sealed class FrigateCommandPayload
{
    private FrigateCommandPayload()
    {
    }
}

public sealed class FrigateEventMessage
{
    /// <summary>
    /// new, update or end.
    /// </summary>
    public string? Type { get; set; }

    public FrigateTrackedObject? After { get; set; }
}

public sealed class FrigateTrackedObject
{
    public required string Id { get; set; }

    public required string Camera { get; set; }

    public string? Label { get; set; }

    public float TopScore { get; set; }

    public bool HasSnapshot { get; set; }
}
