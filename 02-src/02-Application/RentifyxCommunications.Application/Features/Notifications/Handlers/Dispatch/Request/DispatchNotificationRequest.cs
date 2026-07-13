namespace RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;

public sealed record DispatchNotificationRequest(
    Guid CorrelationId,
    Guid RecipientId,
    string RecipientEmail,
    string Channel,
    string TemplateId,
    IReadOnlyDictionary<string, string> Payload);
