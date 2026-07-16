using ErrorOr;
using RentifyxCommunications.Api.Abstract;
using RentifyxCommunications.Api.Extensions;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Response;

namespace RentifyxCommunications.Api.Endpoints.Notifications;

internal sealed class GetNotificationsByRecipientEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("notifications/recipient/{recipientId}", HandleAsync)
           .WithName("GetNotificationsByRecipient")
           .WithDescription("Returns the notifications sent to a given recipient.")
           .WithTags(Tags.NOTIFICATIONS)
           .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        string recipientId,
        IHandler<GetNotificationsByRecipientRequest, NotificationListResponse> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ErrorOr<NotificationListResponse> result = await handler.HandleAsync(
            new GetNotificationsByRecipientRequest(recipientId),
            cancellationToken);

        return result.ToResult(httpContext);
    }
}
