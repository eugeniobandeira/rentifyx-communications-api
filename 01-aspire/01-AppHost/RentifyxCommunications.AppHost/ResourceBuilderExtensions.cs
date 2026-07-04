using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;

namespace RentifyxCommunications.AppHost;

internal static class ResourceBuilderExtensions
{
    private static IResourceBuilder<T> WithOpenApiDocs<T>(
        this IResourceBuilder<T> builder,
        string name,
        string displayName,
        string openApiUiPath)
        where T : IResourceWithEndpoints
    {
        return builder.WithCommand(
            name,
            displayName,
            async _ =>
            {
                try
                {
                    bool hasHttps = builder.Resource.Annotations
                        .OfType<EndpointAnnotation>()
                        .Any(e => e.Name.Equals("https", StringComparison.OrdinalIgnoreCase));

                    EndpointReference endpoint = hasHttps
                        ? builder.GetEndpoint("https")
                        : builder.GetEndpoint("http");
                    string url = $"{endpoint.Url}/{openApiUiPath}";
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    return new ExecuteCommandResult { Success = true };
                }
                catch (InvalidOperationException ex)
                {
                    return new ExecuteCommandResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid operation: " + ex.Message
                    };
                }
            },
            new CommandOptions
            {
                UpdateState = context =>
                    context.ResourceSnapshot.HealthStatus == HealthStatus.Healthy
                        ? ResourceCommandState.Enabled
                        : ResourceCommandState.Disabled,
                IconName = "Document",
                IconVariant = IconVariant.Filled
            });
    }

    internal static IResourceBuilder<T> WithScalar<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEndpoints
    {
        return builder.WithOpenApiDocs(
            name: "scalar-docs",
            displayName: "Scalar API Documentation",
            openApiUiPath: "scalar/v1");
    }
}

