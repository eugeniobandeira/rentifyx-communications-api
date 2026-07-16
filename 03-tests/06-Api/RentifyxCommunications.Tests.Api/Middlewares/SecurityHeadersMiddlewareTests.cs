using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using RentifyxCommunications.Api.Middlewares;
using Xunit;

namespace RentifyxCommunications.Tests.Api.Middlewares;

public sealed class SecurityHeadersMiddlewareTests
{
    private const string StrictTransportSecurityHeader = "Strict-Transport-Security";
    private const string StrictTransportSecurityValue = "max-age=31536000; includeSubDomains";

    private const string ContentTypeOptionsHeader = "X-Content-Type-Options";
    private const string ContentTypeOptionsValue = "nosniff";

    private const string FrameOptionsHeader = "X-Frame-Options";
    private const string FrameOptionsValue = "DENY";

    private const string ContentSecurityPolicyHeader = "Content-Security-Policy";
    private const string ContentSecurityPolicyValue = "default-src 'self'";

    [Fact]
    public async Task InvokeAsync_AddsAllSecurityHeaders_OnSuccessfulResponse()
    {
        FiringHttpResponseFeature responseFeature = new();
        HttpContext context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(responseFeature);

        SecurityHeadersMiddleware middleware = new(async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await responseFeature.FireOnStartingAsync();
        });

        await middleware.InvokeAsync(context);

        context.Response.Headers[StrictTransportSecurityHeader].ToString().Should().Be(StrictTransportSecurityValue);
        context.Response.Headers[ContentTypeOptionsHeader].ToString().Should().Be(ContentTypeOptionsValue);
        context.Response.Headers[FrameOptionsHeader].ToString().Should().Be(FrameOptionsValue);
        context.Response.Headers[ContentSecurityPolicyHeader].ToString().Should().Be(ContentSecurityPolicyValue);
    }

    [Fact]
    public async Task InvokeAsync_AddsAllSecurityHeaders_WhenDownstreamProducesErrorResponse()
    {
        FiringHttpResponseFeature responseFeature = new();
        HttpContext context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(responseFeature);

        SecurityHeadersMiddleware middleware = new(async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await responseFeature.FireOnStartingAsync();
        });

        await middleware.InvokeAsync(context);

        context.Response.Headers[StrictTransportSecurityHeader].ToString().Should().Be(StrictTransportSecurityValue);
        context.Response.Headers[ContentTypeOptionsHeader].ToString().Should().Be(ContentTypeOptionsValue);
        context.Response.Headers[FrameOptionsHeader].ToString().Should().Be(FrameOptionsValue);
        context.Response.Headers[ContentSecurityPolicyHeader].ToString().Should().Be(ContentSecurityPolicyValue);
    }

    /// <summary>
    /// The default <see cref="IHttpResponseFeature"/> used by <see cref="DefaultHttpContext"/> implements
    /// <c>OnStarting</c> as a no-op — firing registered callbacks is normally the hosting server's
    /// (e.g. Kestrel's) responsibility, not something <see cref="DefaultHttpContext"/> does on its own.
    /// This fake stores the registered callback and exposes a way to fire it, so the test can simulate
    /// what a real server does when the response starts.
    /// </summary>
    private sealed class FiringHttpResponseFeature : IHttpResponseFeature
    {
        private Func<object, Task>? _onStartingCallback;
        private object? _onStartingState;

        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state)
        {
            _onStartingCallback = callback;
            _onStartingState = state;
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public async Task FireOnStartingAsync()
        {
            HasStarted = true;

            if (_onStartingCallback is not null)
                await _onStartingCallback(_onStartingState!);
        }
    }
}
