using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Examples.Handlers.Create;
using RentifyxCommunications.Application.Features.Examples.Handlers.Create.Request;
using RentifyxCommunications.Application.Features.Examples.Handlers.Create.Validator;
using RentifyxCommunications.Application.Features.Examples.Handlers.Delete;
using RentifyxCommunications.Application.Features.Examples.Handlers.GetAll;
using RentifyxCommunications.Application.Features.Examples.Handlers.GetAll.Request;
using RentifyxCommunications.Application.Features.Examples.Handlers.GetById;
using RentifyxCommunications.Application.Features.Examples.Handlers.Update;
using RentifyxCommunications.Application.Features.Examples.Handlers.Update.Request;
using RentifyxCommunications.Application.Features.Examples.Handlers.Update.Validator;
using RentifyxCommunications.Domain.Common;
using RentifyxCommunications.Domain.Entities;
using ErrorOr;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace RentifyxCommunications.IoC;

internal static class ApplicationDependencyInjection
{
    internal static IServiceCollection Register(IServiceCollection services)
    {
        services.AddScoped<IValidator<CreateExampleRequest>, CreateExampleValidator>();
        services.AddScoped<IValidator<UpdateExampleRequest>, UpdateExampleValidator>();

        services.AddScoped<IHandler<CreateExampleRequest, ExampleEntity>, CreateExampleHandler>();
        services.AddScoped<IHandler<Guid, Deleted>, DeleteExampleHandler>();
        services.AddScoped<IHandler<GetAllExampleRequest, PagedResult<ExampleEntity>>, GetAllExampleHandler>();
        services.AddScoped<IHandler<Guid, ExampleEntity>, GetByIdExampleHandler>();
        services.AddScoped<IHandler<UpdateExampleRequest, ExampleEntity>, UpdateExampleHandler>();

        return services;
    }
}
