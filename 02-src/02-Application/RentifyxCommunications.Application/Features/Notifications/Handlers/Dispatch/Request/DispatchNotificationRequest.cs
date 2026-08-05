namespace RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;

public sealed record DispatchNotificationRequest(
    Guid IdempotencyKey,
    Guid RecipientId,
    string RecipientEmail,
    string Channel,
    string TemplateId,
    IReadOnlyDictionary<string, string> Payload);
