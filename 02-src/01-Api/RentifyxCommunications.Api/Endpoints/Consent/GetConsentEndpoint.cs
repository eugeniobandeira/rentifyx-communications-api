using ErrorOr;
using RentifyxCommunications.Api.Abstract;
using RentifyxCommunications.Api.Extensions;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get.Request;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get.Response;

namespace RentifyxCommunications.Api.Endpoints.Consent;

internal sealed class GetConsentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder consentGroup = app.MapGroup("consent")
                                             .RequireRateLimiting(RateLimitExtension.ConsentPolicyName);

        consentGroup.MapGet("/{recipientId}", HandleAsync)
                    .WithName("GetConsent")
                    .WithDescription("Returns the consent preference for a recipient on a given channel.")
                    .WithTags(Tags.CONSENT)
                    .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        string recipientId,
        string channel,
        IHandler<GetConsentRequest, ConsentResponse> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ErrorOr<ConsentResponse> result = await handler.HandleAsync(
            new GetConsentRequest(recipientId, channel),
            cancellationToken);

        return result.ToResult(httpContext);
    }
}
