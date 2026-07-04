using RentifyxCommunications.Domain.Interfaces;

namespace RentifyxCommunications.Infrastructure.Context;

public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public async Task CommitAsync(CancellationToken cancellationToken = default)
        => await context.SaveChangesAsync(cancellationToken);
}
