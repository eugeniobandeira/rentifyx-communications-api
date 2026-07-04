using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Domain.Interfaces;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Interfaces.Common;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace RentifyxCommunications.Application.Features.Examples.Handlers.Delete;

public sealed class DeleteExampleHandler(
    IGetByIdRepository<ExampleEntity> getByIdRepository,
    IDeleteRepository<ExampleEntity> deleteRepository,
    IUnitOfWork unitOfWork,
    ILogger<DeleteExampleHandler> logger) : IHandler<Guid, Deleted>
{
    public async Task<ErrorOr<Deleted>> Handle(Guid id, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Deleting example. Id={Id}", id);

        ExampleEntity? entity = await getByIdRepository.GetByIdAsync(id, cancellationToken);

        if (entity is null)
        {
            logger.LogWarning("Example not found for deletion. Id={Id}", id);
            return Error.NotFound(ExampleErrorCodes.NotFound, $"Example {id} not found.");
        }

        await deleteRepository.DeleteAsync(entity, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Example deleted successfully. Response={@Response}", entity);

        return Result.Deleted;
    }
}
