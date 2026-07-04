using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Infrastructure.Context.Configurations;
using Microsoft.EntityFrameworkCore;

namespace RentifyxCommunications.Infrastructure.Context;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ExampleEntity> Examples => Set<ExampleEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ExampleConfiguration());
    }
}
