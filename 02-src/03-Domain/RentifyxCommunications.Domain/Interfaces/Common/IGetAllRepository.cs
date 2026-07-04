using RentifyxCommunications.Domain.Common;

namespace RentifyxCommunications.Domain.Interfaces.Common;

public interface IGetAllRepository<T, TFilter>
    where T : class
    where TFilter : class
{
    Task<PagedResult<T>> GetAllAsync(TFilter filter, CancellationToken cancellationToken = default);
}
