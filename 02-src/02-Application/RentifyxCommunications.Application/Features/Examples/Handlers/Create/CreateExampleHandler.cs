using ErrorOr;
using FluentValidation;
using Microsoft.Extensions.Logging;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Extensions;
using RentifyxCommunications.Application.Features.Examples.Handlers.Create.Request;
using RentifyxCommunications.Application.Features.Examples.Mapper;
using RentifyxCommunications.Domain.Entities;
using RentifyxCommunications.Domain.Interfaces;
using RentifyxCommunications.Domain.Interfaces.Common;

namespace RentifyxCommunications.Application.Features.Examples.Handlers.Create;

public sealed class CreateExampleHandler(
    IAddRepository<ExampleEntity> repository,
    IUnitOfWork unitOfWork,
    IValidator<CreateExampleRequest> validator,
    ILogger<CreateExampleHandler> logger) : IHandler<CreateExampleRequest, ExampleEntity>
{
    public async Task<ErrorOr<ExampleEntity>> Handle(
        CreateExampleRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating example. Payload={@Payload}", request);

        List<Error>? errors = await validator.ValidateToErrorsAsync(request, cancellationToken);
        if (errors is not null)
            return errors;

        ExampleEntity entity = ExampleMapper.CreateExample(request);

        await repository.AddAsync(entity, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Example created successfully. Response={@Response}", entity);

        return entity;
    }
}
