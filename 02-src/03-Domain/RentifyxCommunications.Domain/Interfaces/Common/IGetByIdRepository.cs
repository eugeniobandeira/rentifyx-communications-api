namespace RentifyxCommunications.Domain.Interfaces.Common;

public interface IGetByIdRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
