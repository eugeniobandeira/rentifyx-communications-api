using FluentAssertions;
using Microsoft.AspNetCore.Http;
using RentifyxCommunications.Api.Middlewares;
using RentifyxCommunications.Domain.Constants;
using Xunit;

namespace RentifyxCommunications.Tests.Api.Middlewares;

public sealed class CorrelationIdMiddlewareTests
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    [Fact]
    public async Task InvokeAsync_GeneratesNewCorrelationId_WhenHeaderNotPresent()
    {
        HttpContext context = new DefaultHttpContext();
        CorrelationIdMiddleware middleware = new(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        string? correlationId = context.Response.Headers[CorrelationIdHeader];
        correlationId.Should().NotBeNullOrWhiteSpace();
        context.Items[CorrelationIdConstants.Key].Should().Be(correlationId);
    }

    [Fact]
    public async Task InvokeAsync_PropagatesExistingCorrelationId_WhenHeaderPresent()
    {
        const string existingCorrelationId = "existing-correlation-id-123";
        HttpContext context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdHeader] = existingCorrelationId;
        CorrelationIdMiddleware middleware = new(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.Headers[CorrelationIdHeader].ToString().Should().Be(existingCorrelationId);
        context.Items[CorrelationIdConstants.Key].Should().Be(existingCorrelationId);
    }
}
