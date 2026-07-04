using Bogus;
using RentifyxCommunications.Domain.Entities;

namespace RentifyxCommunications.Tests.Common.Builders;

public sealed class ExampleBuilder
{
    private readonly Faker _faker = new();

    private string _name = string.Empty;
    private string _description = string.Empty;

    public ExampleBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public ExampleBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public ExampleEntity Build() =>
        ExampleEntity.Create(
            string.IsNullOrWhiteSpace(_name) ? _faker.Lorem.Word() : _name,
            string.IsNullOrWhiteSpace(_description) ? _faker.Lorem.Sentence() : _description);
}
