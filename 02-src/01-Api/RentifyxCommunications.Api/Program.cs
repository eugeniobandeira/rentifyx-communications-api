using RentifyxCommunications.Api.Extensions;
using RentifyxCommunications.Api.Middlewares;
using RentifyxCommunications.IoC;
using RentifyxCommunications.ServiceDefaults;
using Serilog;
using Serilog.Formatting.Json;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new JsonFormatter())
    .CreateBootstrapLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.AddServiceDefaults();

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .Enrich.WithThreadId()
        .WriteTo.Console(new JsonFormatter()));

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddOpenApiDocumentation(builder.Configuration);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddCorsPolicy(builder.Configuration);
    builder.Services.AddVersioning();
    builder.Services.AddRateLimiting(builder.Configuration);
    builder.Services.AddEndpoints();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    WebApplication app = builder.Build();

    app.MapDefaultEndpoints();

    app.UseExceptionHandler();
    app.UseCorrelationId();
    app.UseRateLimiting();
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        };
    });

    if (app.Environment.IsDevelopment())
        app.UseOpenApiDocumentation();

    if (!app.Environment.IsDevelopment())
        app.UseHttpsRedirection();
    app.UseCorsPolicy();
    app.MapEndpoints();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
