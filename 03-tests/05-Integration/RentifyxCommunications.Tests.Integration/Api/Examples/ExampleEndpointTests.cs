using Xunit;

namespace RentifyxCommunications.Tests.Integration.Api.Examples;

public sealed class ExampleEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact(Skip = "Not yet implemented")]
    public async Task GetAll_ShouldReturnOk()
    {
        HttpResponseMessage response = await _client.GetAsync("/api/v1/examples");
        response.EnsureSuccessStatusCode();
    }
}
