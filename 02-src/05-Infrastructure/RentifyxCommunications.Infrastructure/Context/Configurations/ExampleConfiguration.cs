using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;

namespace RentifyxCommunications.Infrastructure.Context.Configurations;

internal sealed class ExampleConfiguration : IEntityTypeConfiguration<ExampleEntity>
{
    public void Configure(EntityTypeBuilder<ExampleEntity> builder)
    {
        builder.ToTable("Examples");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(ValidationConstants.ExampleRules.NameMaxLength);

        builder.Property(e => e.Description)
            .IsRequired()
            .HasMaxLength(ValidationConstants.ExampleRules.DescriptionMaxLength);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt);
    }
}
