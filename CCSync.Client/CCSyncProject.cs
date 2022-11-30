namespace CCSync.Client;

public record CCSyncProject
{
    public required Guid Auth { get; init; }
    public required string World { get; init; }
    public required string Origin { get; init; }
}