using ErrorOr;
using RentifyxCommunications.Domain.ValueObjects;

namespace RentifyxCommunications.Domain.Interfaces;

public interface ITemplateRenderer
{
    Task<ErrorOr<string>> RenderAsync(
        TemplateId templateId,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken = default);
}
