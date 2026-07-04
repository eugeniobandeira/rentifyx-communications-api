namespace RentifyxCommunications.Domain.Interfaces.Common;

public interface IAddRepository<in T> where T : class
{
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
}
