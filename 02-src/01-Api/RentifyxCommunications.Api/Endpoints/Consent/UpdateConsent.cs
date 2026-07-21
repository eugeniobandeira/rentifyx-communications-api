using ErrorOr;
using RentifyxCommunications.Api.Abstract;
using RentifyxCommunications.Api.Extensions;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update.Request;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update.Response;

namespace RentifyxCommunications.Api.Endpoints.Consent;

internal sealed class UpdateConsent : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder consentGroup = app.MapGroup("consent")
                                             .RequireRateLimiting(RateLimitExtension.ConsentPolicyName);

        consentGroup.MapPut("/{recipientId}", HandleAsync)
                    .WithName("UpdateConsent")
                    .WithDescription("Updates the consent preference for a recipient on a given channel.")
                    .WithTags(Tags.CONSENT)
                    .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        string recipientId,
        UpdateConsentBody body,
        IHandler<UpdateConsentRequest, ConsentResponse> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ErrorOr<ConsentResponse> result = await handler.HandleAsync(
            new UpdateConsentRequest(recipientId, body.Channel, body.OptedIn),
            cancellationToken);

        return result.ToResult(httpContext);
    }

    private sealed record UpdateConsentBody(
        string Channel,
        bool OptedIn);
}
