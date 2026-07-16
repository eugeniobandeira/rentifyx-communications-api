#pragma warning disable CA1716 // "Get" folder name mirrors the feature action, not a reserved-keyword API surface.
namespace RentifyxCommunications.Application.Features.Consent.Handlers.Get.Response;
#pragma warning restore CA1716

public sealed record ConsentResponse(
    Guid RecipientId,
    string Channel,
    bool OptedIn,
    DateTime? UpdatedAt);
