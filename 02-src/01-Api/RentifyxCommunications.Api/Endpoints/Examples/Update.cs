using ErrorOr;
using RentifyxCommunications.Api.Abstract;
using RentifyxCommunications.Api.Extensions;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Examples.Handlers.Update.Request;
using RentifyxCommunications.Application.Features.Examples.Mapper;
using RentifyxCommunications.Domain.Entities;

namespace RentifyxCommunications.Api.Endpoints.Examples;

internal sealed class Update : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/examples/{id:guid}", HandleAsync)
           .WithName("UpdateExample")
           .WithDescription("Update an existing example.")
           .WithTags(Tags.EXAMPLE);
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateExampleRequest request,
        IHandler<UpdateExampleRequest, ExampleEntity> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ErrorOr<ExampleEntity> result = await handler.HandleAsync(request with { Id = id }, cancellationToken);

        return result.Match(
            entity => Results.Ok(ExampleMapper.ToResponse(entity)),
            errors => errors.ToProblem(httpContext));
    }
}
