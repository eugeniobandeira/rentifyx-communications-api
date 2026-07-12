using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RentifyxCommunications.Api.Middlewares;
using System.Text.Json;
using Xunit;

namespace RentifyxCommunications.Tests.Api.Middlewares;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_IncludesExceptionMessageInDetail_WhenDevelopment()
    {
        GlobalExceptionHandler handler = CreateHandler(Environments.Development);
        HttpContext context = CreateHttpContext();
        InvalidOperationException exception = new("boom - sensitive internal detail");

        await handler.TryHandleAsync(context, exception, CancellationToken.None);

        ProblemDetails problem = await ReadProblemDetailsAsync(context);
        problem.Detail.Should().Be(exception.Message);
    }

    [Fact]
    public async Task TryHandleAsync_SuppressesExceptionMessageInDetail_WhenProduction()
    {
        GlobalExceptionHandler handler = CreateHandler(Environments.Production);
        HttpContext context = CreateHttpContext();
        InvalidOperationException exception = new("boom - sensitive internal detail");

        await handler.TryHandleAsync(context, exception, CancellationToken.None);

        ProblemDetails problem = await ReadProblemDetailsAsync(context);
        problem.Detail.Should().NotBeNullOrEmpty();
        problem.Detail.Should().NotContain(exception.Message);
    }

    [Fact]
    public async Task TryHandleAsync_SetsProblemJsonContentType()
    {
        GlobalExceptionHandler handler = CreateHandler(Environments.Production);
        HttpContext context = CreateHttpContext();

        await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        context.Response.ContentType.Should().Be("application/problem+json");
    }

    private static GlobalExceptionHandler CreateHandler(string environmentName) =>
        new(NullLogger<GlobalExceptionHandler>.Instance, new FakeHostEnvironment(environmentName));

    private static HttpContext CreateHttpContext()
    {
        DefaultHttpContext context = new()
        {
            Response = { Body = new MemoryStream() }
        };
        return context;
    }

    private static async Task<ProblemDetails> ReadProblemDetailsAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        ProblemDetails? problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body);
        return problem!;
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "RentifyxCommunications.Tests.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
