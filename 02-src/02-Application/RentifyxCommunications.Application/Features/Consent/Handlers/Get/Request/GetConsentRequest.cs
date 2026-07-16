#pragma warning disable CA1716 // "Get" folder name mirrors the feature action, not a reserved-keyword API surface.
namespace RentifyxCommunications.Application.Features.Consent.Handlers.Get.Request;
#pragma warning restore CA1716

public sealed record GetConsentRequest(
    string RecipientId,
    string Channel);
