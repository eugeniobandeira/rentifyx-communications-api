using Microsoft.OpenApi;
using RentifyxCommunications.Api.Extensions.Options;
using Scalar.AspNetCore;

namespace RentifyxCommunications.Api.Extensions;

public static class OpenApiExtensions
{
    public static IServiceCollection AddOpenApiDocumentation(this IServiceCollection services, IConfiguration configuration)
    {
        OpenApiDocumentationOptions openApiOptions = configuration.GetSection("OpenApi").Get<OpenApiDocumentationOptions>()
            ?? throw new InvalidOperationException("OpenApi is not configured.");

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, context, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "RentifyxCommunications API",
                    Version = "v1",
                    Description = "API generated with Clean Architecture Template",
                    Contact = new OpenApiContact
                    {
                        Name = openApiOptions.ContactName,
                        Url = new Uri(openApiOptions.ContactUrl)
                    }
                };

                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static IApplicationBuilder UseOpenApiDocumentation(this WebApplication app)
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.Title = "RentifyxCommunications API";
            options.Theme = ScalarTheme.DeepSpace;
            options.TagSorter = TagSorter.Alpha;
            options.OperationSorter = OperationSorter.Alpha;
        });

        return app;
    }
}
