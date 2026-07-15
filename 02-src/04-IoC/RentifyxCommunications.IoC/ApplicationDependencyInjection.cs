using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Response;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Validator;

namespace RentifyxCommunications.IoC;

internal static class ApplicationDependencyInjection
{
    internal static IServiceCollection Register(IServiceCollection services)
    {
        services.AddScoped<IValidator<DispatchNotificationRequest>, DispatchNotificationValidator>();
        services.AddScoped<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>, DispatchNotificationHandler>();
        services.AddSingleton<NotificationMetrics>();
        services.AddScoped<NotificationDispatchProcessor>();

        return services;
    }
}
