using ErrorOr;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Domain.Interfaces.Notifications;

public interface ITemplateRenderer
{
    Task<ErrorOr<string>> RenderAsync(
        TemplateId templateId,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken = default);
}
