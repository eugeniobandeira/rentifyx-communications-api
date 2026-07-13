using RentifyxCommunications.Api.Abstract;

namespace RentifyxCommunications.Api.Endpoints.Health;

internal sealed class HealthCheck : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/health", HandleAsync)
           .WithName("HealthCheck")
           .WithDescription("Returns whether the application is up and running.")
           .WithTags(Tags.HEALTH)
           .AllowAnonymous();
    }

    private static IResult HandleAsync()
    {
        return Results.Ok(new { status = "healthy" });
    }
}
