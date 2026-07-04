using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Examples.Handlers.GetAll.Request;
using RentifyxCommunications.Application.Features.Examples.Mapper;
using RentifyxCommunications.Domain.Common;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Interfaces.Examples;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace RentifyxCommunications.Application.Features.Examples.Handlers.GetAll;

public sealed class GetAllExampleHandler(
    IExampleRepository repository,
    ILogger<GetAllExampleHandler> logger) : IHandler<GetAllExampleRequest, PagedResult<ExampleEntity>>
{
    public async Task<ErrorOr<PagedResult<ExampleEntity>>> Handle(GetAllExampleRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Fetching examples. Payload={@Payload}", request);

        PagedResult<ExampleEntity> result = await repository.GetAllAsync(ExampleMapper.ToFilter(request), cancellationToken);

        logger.LogDebug("Fetched {Count} of {Total} examples.", result.Items.Count, result.Total);

        return ErrorOrFactory.From(result);
    }
}
