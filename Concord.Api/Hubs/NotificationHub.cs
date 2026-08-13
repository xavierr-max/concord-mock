using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Concord.Api.Hubs;

[Authorize]
public sealed class NotificationHub : Hub;
