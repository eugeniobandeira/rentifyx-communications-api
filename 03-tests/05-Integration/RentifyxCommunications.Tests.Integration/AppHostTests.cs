extern alias AppHostRef;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace RentifyxCommunications.Tests.Integration;

public sealed class AppHostTests
{
    [Fact]
    public async Task AppHost_StartsApiResource_AndHealthEndpointRespondsHealthy()
    {
        IDistributedApplicationTestingBuilder appHost =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHostRef::Projects.RentifyxCommunications_AppHost>();

        appHost.Services.ConfigureHttpClientDefaults(clientBuilder => clientBuilder.AddStandardResilienceHandler());

        await using DistributedApplication app = await appHost.BuildAsync();
        ResourceNotificationService resourceNotificationService =
            app.Services.GetRequiredService<ResourceNotificationService>();

        await app.StartAsync();

        await resourceNotificationService
            .WaitForResourceAsync("clean-arch-api", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(60));

        using HttpClient httpClient = app.CreateHttpClient("clean-arch-api");
        using HttpResponseMessage response = await httpClient.GetAsync("/health");

        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
