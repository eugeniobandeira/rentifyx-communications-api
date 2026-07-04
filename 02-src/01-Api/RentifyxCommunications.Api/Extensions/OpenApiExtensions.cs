using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace RentifyxCommunications.Api.Extensions;

public static class OpenApiExtensions
{
    public static IServiceCollection AddOpenApiDocumentation(this IServiceCollection services, IConfiguration configuration)
    {
        string contactName = configuration["OpenApi:ContactName"]!;
        string contactUrl = configuration["OpenApi:ContactUrl"]!;

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
                        Name = contactName,
                        Url = new Uri(contactUrl)
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
