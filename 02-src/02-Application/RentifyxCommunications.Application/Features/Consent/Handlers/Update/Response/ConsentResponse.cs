using RentifyxCommunications.Domain.Enums;

namespace RentifyxCommunications.Application.Features.Consent.Handlers.Update.Response;

public sealed record ConsentResponse(
    Guid RecipientId,
    Channel Channel,
    bool OptedIn,
    DateTime UpdatedAt);
