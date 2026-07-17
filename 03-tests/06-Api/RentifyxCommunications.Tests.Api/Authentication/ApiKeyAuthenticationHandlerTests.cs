using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RentifyxCommunications.Api.Authentication;
using RentifyxCommunications.Application.Abstractions;
using Xunit;

namespace RentifyxCommunications.Tests.Api.Authentication;

public sealed class ApiKeyAuthenticationHandlerTests
{
    private const string KnownApiKey = "known-api-key-value";
    private const string ApiKeySecretName = "rentifyx/comms/api-key";

    [Fact]
    public async Task AuthenticateAsync_Succeeds_WhenHeaderMatchesConfiguredKey()
    {
        ApiKeyAuthenticationHandler handler = await CreateHandlerAsync(KnownApiKey);

        AuthenticateResult result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticateAsync_Fails_WhenHeaderIsMissing()
    {
        ApiKeyAuthenticationHandler handler = await CreateHandlerAsync(headerValue: null);

        AuthenticateResult result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_Fails_WhenHeaderValueDoesNotMatch()
    {
        ApiKeyAuthenticationHandler handler = await CreateHandlerAsync("wrong-api-key");

        AuthenticateResult result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_Fails_WhenHeaderIsEmptyString()
    {
        ApiKeyAuthenticationHandler handler = await CreateHandlerAsync(string.Empty);

        AuthenticateResult result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    private static async Task<ApiKeyAuthenticationHandler> CreateHandlerAsync(string? headerValue)
    {
        Mock<ISecretsProvider> secretsProviderMock = new();
        secretsProviderMock
            .Setup(p => p.GetSecretAsync(ApiKeySecretName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(KnownApiKey);

        IOptions<SecretsProviderOptions> secretsProviderOptions = Options.Create(
            new SecretsProviderOptions("ses-arn", ApiKeySecretName));

        ApiKeyAuthenticationHandler handler = new(
            new StaticOptionsMonitor(new ApiKeyAuthenticationOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            secretsProviderMock.Object,
            secretsProviderOptions);

        DefaultHttpContext httpContext = new();
        if (headerValue is not null)
            httpContext.Request.Headers[ApiKeyDefaults.HeaderName] = headerValue;

        AuthenticationScheme scheme = new(
            ApiKeyDefaults.Scheme,
            ApiKeyDefaults.Scheme,
            typeof(ApiKeyAuthenticationHandler));

        await handler.InitializeAsync(scheme, httpContext);

        return handler;
    }

    private sealed class StaticOptionsMonitor(ApiKeyAuthenticationOptions options)
        : IOptionsMonitor<ApiKeyAuthenticationOptions>
    {
        public ApiKeyAuthenticationOptions CurrentValue => options;

        public ApiKeyAuthenticationOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<ApiKeyAuthenticationOptions, string?> listener) => null;
    }
}
