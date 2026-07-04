namespace RentifyxCommunications.Application.Features.Examples;

public sealed record ExampleResponse(
    Guid Id,
    string Name,
    string Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
