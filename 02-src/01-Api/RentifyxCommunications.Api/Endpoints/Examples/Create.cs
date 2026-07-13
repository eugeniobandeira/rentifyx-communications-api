using ErrorOr;
using RentifyxCommunications.Api.Abstract;
using RentifyxCommunications.Api.Extensions;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Examples.Handlers.Create.Request;
using RentifyxCommunications.Application.Features.Examples.Mapper;
using RentifyxCommunications.Domain.Entities;

namespace RentifyxCommunications.Api.Endpoints.Examples;

internal sealed class Create : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/examples", HandleAsync)
           .WithName("CreateExample")
           .WithDescription("Create a new example.")
           .WithTags(Tags.EXAMPLE);
    }

    private static async Task<IResult> HandleAsync(
        CreateExampleRequest request,
        IHandler<CreateExampleRequest, ExampleEntity> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ErrorOr<ExampleEntity> result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            entity => Results.Created($"/api/v1/examples/{entity.Id}", ExampleMapper.ToResponse(entity)),
            errors => errors.ToProblem(httpContext));
    }
}
