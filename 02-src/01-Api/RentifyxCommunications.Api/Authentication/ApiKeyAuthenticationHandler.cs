using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using RentifyxCommunications.Application.Abstractions;

namespace RentifyxCommunications.Api.Authentication;

internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISecretsProvider secretsProvider,
    IOptions<SecretsProviderOptions> secretsProviderOptions)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out StringValues headerValues))
            return AuthenticateResult.Fail("API key header is missing.");

        string? providedApiKey = headerValues.ToString();
        if (string.IsNullOrEmpty(providedApiKey))
            return AuthenticateResult.Fail("API key header is missing.");

        string expectedApiKey = await secretsProvider.GetSecretAsync(
            secretsProviderOptions.Value.ApiKey,
            Request.HttpContext.RequestAborted);

        byte[] providedBytes = Encoding.UTF8.GetBytes(providedApiKey);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey);

        if (!CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
            return AuthenticateResult.Fail("Invalid API key");

        ClaimsIdentity identity = new(ApiKeyDefaults.Scheme);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, ApiKeyDefaults.Scheme);

        return AuthenticateResult.Success(ticket);
    }
}
