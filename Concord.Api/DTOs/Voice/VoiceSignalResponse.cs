namespace Concord.Api.DTOs.Voice;

public sealed record VoiceOfferResponse(Guid SenderUserId, Guid ChannelId, string Sdp);

public sealed record VoiceAnswerResponse(Guid SenderUserId, Guid ChannelId, string Sdp);

public sealed record VoiceIceCandidateResponse(Guid SenderUserId, Guid ChannelId, string Candidate);
