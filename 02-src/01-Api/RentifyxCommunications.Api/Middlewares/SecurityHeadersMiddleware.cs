namespace RentifyxCommunications.Api.Middlewares;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string StrictTransportSecurityHeader = "Strict-Transport-Security";
    private const string StrictTransportSecurityValue = "max-age=31536000; includeSubDomains";

    private const string ContentTypeOptionsHeader = "X-Content-Type-Options";
    private const string ContentTypeOptionsValue = "nosniff";

    private const string FrameOptionsHeader = "X-Frame-Options";
    private const string FrameOptionsValue = "DENY";

    private const string ContentSecurityPolicyHeader = "Content-Security-Policy";
    private const string ContentSecurityPolicyValue = "default-src 'self'";

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[StrictTransportSecurityHeader] = StrictTransportSecurityValue;
            context.Response.Headers[ContentTypeOptionsHeader] = ContentTypeOptionsValue;
            context.Response.Headers[FrameOptionsHeader] = FrameOptionsValue;
            context.Response.Headers[ContentSecurityPolicyHeader] = ContentSecurityPolicyValue;

            return Task.CompletedTask;
        });

        await next(context);
    }
}
