using ErrorOr;

namespace RentifyxCommunications.Application.Common.Handler;

public interface IHandler<TRequest, TResponse>
{
    Task<ErrorOr<TResponse>> Handle(TRequest request, CancellationToken cancellationToken = default);
}
