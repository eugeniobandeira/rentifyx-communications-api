using ErrorOr;
using RentifyxCommunications.Api.Abstract;
using RentifyxCommunications.Api.Extensions;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Common.Mapper;
using RentifyxCommunications.Application.Features.Examples.Handlers.GetAll.Request;
using RentifyxCommunications.Application.Features.Examples.Mapper;
using RentifyxCommunications.Domain.Common;
using RentifyxCommunications.Domain.Entities;

namespace RentifyxCommunications.Api.Endpoints.Examples;

internal sealed class GetAll : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/examples", HandleAsync)
           .WithName("GetAllExamples")
           .WithDescription("Get all active examples.")
           .WithTags(Tags.EXAMPLE);
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] GetAllExampleRequest request,
        IHandler<GetAllExampleRequest, PagedResult<ExampleEntity>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ErrorOr<PagedResult<ExampleEntity>> result = await handler.HandleAsync(request, cancellationToken);

        return result.Match(
            pagedResult => Results.Ok(ApiListResponseMapper.ToListResponse(
                [.. pagedResult.Items.Select(ExampleMapper.ToResponse)],
                pagedResult.Total,
                request.Page,
                request.PageSize)),
            errors => errors.ToProblem(httpContext));
    }
}
