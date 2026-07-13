using ErrorOr;

namespace RentifyxCommunications.Application.Common.Handler;

public interface IHandler<TRequest, TResponse>
{
    Task<ErrorOr<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default);
}
