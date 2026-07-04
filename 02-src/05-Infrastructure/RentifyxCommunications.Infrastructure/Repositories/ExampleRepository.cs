using RentifyxCommunications.Domain.Common;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Filters.Examples;
using RentifyxCommunications.Domain.Interfaces.Examples;
using RentifyxCommunications.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace RentifyxCommunications.Infrastructure.Repositories;

public sealed class ExampleRepository(AppDbContext dbContext) : IExampleRepository
{
    public async Task AddAsync(ExampleEntity entity, CancellationToken cancellationToken = default)
        => await dbContext.Examples.AddAsync(entity, cancellationToken);

    public async Task<ExampleEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await dbContext.Examples.FindAsync([id], cancellationToken);

    public async Task<PagedResult<ExampleEntity>> GetAllAsync(
        ExampleFilter filter,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ExampleEntity> query = dbContext.Examples.AsNoTracking();

        if (filter.Name is not null)
            query = query.Where(e => e.Name.Contains(filter.Name));

        if (filter.IsActive is not null)
            query = query.Where(e => e.IsActive == filter.IsActive);

        int total = await query.CountAsync(cancellationToken);

        List<ExampleEntity> items = await query
            .OrderBy(e => e.Name)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ExampleEntity>(items, total);
    }

    public Task UpdateAsync(ExampleEntity entity, CancellationToken cancellationToken = default)
    {
        dbContext.Examples.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(ExampleEntity entity, CancellationToken cancellationToken = default)
    {
        dbContext.Examples.Remove(entity);
        return Task.CompletedTask;
    }
}
