namespace Concord.Api.Hubs;

public static class ChatHubEvents
{
    public const string MessageCreated = nameof(MessageCreated);
    public const string MessageUpdated = nameof(MessageUpdated);
    public const string MessageDeleted = nameof(MessageDeleted);
    public const string UserOnline = nameof(UserOnline);
    public const string UserOffline = nameof(UserOffline);
    public const string UserStatusChanged = nameof(UserStatusChanged);
    public const string TypingStarted = nameof(TypingStarted);
    public const string TypingStopped = nameof(TypingStopped);
}
