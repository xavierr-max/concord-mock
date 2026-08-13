namespace Concord.Api.Configurations;

public sealed class PresenceSettings
{
    public const string SectionName = "Presence";
    public TimeSpan DisconnectGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}
