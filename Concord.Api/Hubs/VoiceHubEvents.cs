namespace Concord.Api.Hubs;

public static class VoiceHubEvents
{
    public const string VoiceUserJoined = nameof(VoiceUserJoined);
    public const string VoiceUserLeft = nameof(VoiceUserLeft);
    public const string VoiceUserUpdated = nameof(VoiceUserUpdated);
    public const string VoiceOfferReceived = nameof(VoiceOfferReceived);
    public const string VoiceAnswerReceived = nameof(VoiceAnswerReceived);
    public const string VoiceIceCandidateReceived = nameof(VoiceIceCandidateReceived);
}
