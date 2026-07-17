using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using RentifyxCommunications.Application.Common.Handler;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get.Request;
using RentifyxCommunications.Application.Features.Consent.Handlers.Get.Validator;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update.Request;
using RentifyxCommunications.Application.Features.Consent.Handlers.Update.Validator;
using GetConsentResponse = RentifyxCommunications.Application.Features.Consent.Handlers.Get.Response.ConsentResponse;
using UpdateConsentResponse = RentifyxCommunications.Application.Features.Consent.Handlers.Update.Response.ConsentResponse;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Common;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Response;
using RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Validator;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Response;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetByRecipient.Validator;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Request;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Response;
using RentifyxCommunications.Application.Features.Notifications.Handlers.GetStatus.Validator;

namespace RentifyxCommunications.IoC;

internal static class ApplicationDependencyInjection
{
    internal static IServiceCollection Register(IServiceCollection services)
    {
        services.AddScoped<IValidator<DispatchNotificationRequest>, DispatchNotificationValidator>();
        services.AddScoped<IHandler<DispatchNotificationRequest, DispatchNotificationResponse>, DispatchNotificationHandler>();
        services.AddSingleton<NotificationMetrics>();
        services.AddScoped<NotificationDispatchProcessor>();

        services.AddScoped<IValidator<GetNotificationStatusRequest>, GetNotificationStatusValidator>();
        services.AddScoped<IHandler<GetNotificationStatusRequest, NotificationStatusResponse>, GetNotificationStatusHandler>();

        services.AddScoped<IValidator<GetNotificationsByRecipientRequest>, GetNotificationsByRecipientValidator>();
        services.AddScoped<IHandler<GetNotificationsByRecipientRequest, NotificationListResponse>, GetNotificationsByRecipientHandler>();

        services.AddScoped<IValidator<GetConsentRequest>, GetConsentValidator>();
        services.AddScoped<IHandler<GetConsentRequest, GetConsentResponse>, GetConsentHandler>();

        services.AddScoped<IValidator<UpdateConsentRequest>, UpdateConsentValidator>();
        services.AddScoped<IHandler<UpdateConsentRequest, UpdateConsentResponse>, UpdateConsentHandler>();

        return services;
    }
}
