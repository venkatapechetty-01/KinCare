using KinCare.API.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KinCare.API.Hubs;

[Authorize]
public class RideStatusHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var facilityId = Context.User?.FindFirst("facility_id")?.Value;
        var orgId = Context.User?.FindFirst("organization_id")?.Value;

        if (facilityId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"facility:{facilityId}");
        }
        else if (orgId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"org:{orgId}");
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
