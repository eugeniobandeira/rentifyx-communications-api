using ErrorOr;
using FluentValidation;
using Microsoft.Extensions.Logging;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Extensions;
using RentifyxCommunications.Application.Features.Examples.Handlers.Update.Request;
using RentifyxCommunications.Application.Features.Examples.Mapper;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Interfaces;
using RentifyxCommunications.Domain.Interfaces.Common;

namespace RentifyxCommunications.Application.Features.Examples.Handlers.Update;

public sealed class UpdateExampleHandler(
    IGetByIdRepository<ExampleEntity> getByIdRepository,
    IUpdateRepository<ExampleEntity> updateRepository,
    IUnitOfWork unitOfWork,
    IValidator<UpdateExampleRequest> validator,
    ILogger<UpdateExampleHandler> logger) : IHandler<UpdateExampleRequest, ExampleEntity>
{
    public async Task<ErrorOr<ExampleEntity>> Handle(
        UpdateExampleRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Updating example. Payload={@Payload}", request);

        List<Error>? errors = await validator.ValidateToErrorsAsync(request, cancellationToken);
        if (errors is not null)
            return errors;

        ExampleEntity? entity = await getByIdRepository.GetByIdAsync(request.Id, cancellationToken);

        ExampleMapper.UpdateExample(entity!, request);

        await updateRepository.UpdateAsync(entity!, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Example updated successfully. Response={@Response}", entity);

        return entity!;
    }
}
