using System.Diagnostics.CodeAnalysis;
using RentifyxCommunications.Api.Authentication;

namespace RentifyxCommunications.Api.Extensions;

[ExcludeFromCodeCoverage]
internal static class AuthenticationExtension
{
    public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(ApiKeyDefaults.Scheme)
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyDefaults.Scheme,
                _ => { });

        services.AddAuthorization();

        return services;
    }
}
