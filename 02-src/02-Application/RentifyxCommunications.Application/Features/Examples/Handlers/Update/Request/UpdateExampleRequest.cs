namespace RentifyxCommunications.Application.Features.Examples.Handlers.Update.Request;

public sealed record UpdateExampleRequest(string Name, string Description)
{
    public Guid Id { get; init; }
}
