using ErrorOr;
using RentifyxCommunications.Api.Abstract;
using RentifyxCommunications.Api.Extensions;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Response;

namespace RentifyxCommunications.Api.Endpoints.Notifications;

internal sealed class GetNotificationStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("notifications/{id}", HandleAsync)
           .WithName("GetNotificationStatus")
           .WithDescription("Returns the status of a notification by its id.")
           .WithTags(Tags.NOTIFICATIONS)
           .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        string id,
        IHandler<GetNotificationStatusRequest, NotificationStatusResponse> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ErrorOr<NotificationStatusResponse> result = await handler.HandleAsync(
            new GetNotificationStatusRequest(id),
            cancellationToken);

        return result.ToResult(httpContext);
    }
}
