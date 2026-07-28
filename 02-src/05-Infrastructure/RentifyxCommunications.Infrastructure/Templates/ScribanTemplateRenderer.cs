using System.Reflection;
using System.Text.RegularExpressions;
using ErrorOr;
using Microsoft.Extensions.Options;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Options;
using Scriban;
using Scriban.Runtime;

namespace RentifyxCommunications.Infrastructure.Templates;

public sealed partial class ScribanTemplateRenderer(IOptions<FrontendOptions> frontendOptions) : ITemplateRenderer
{
    private const string FrontendBaseUrlField = "frontend_base_url";
    private const string EmailField = "email";

    private static readonly Assembly ResourceAssembly = typeof(ScribanTemplateRenderer).Assembly;

    public Task<ErrorOr<string>> RenderAsync(
        TemplateId templateId,
        EmailAddress recipient,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken = default)
    {
        string? resourceName = ResourceAssembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith($"{templateId.Value}.scriban", StringComparison.Ordinal));

        if (resourceName is null)
            return Task.FromResult<ErrorOr<string>>(Error.NotFound(
                TemplateErrorCodes.NotFound,
                $"Template '{templateId.Value}' was not found."));

        string source = ReadResource(resourceName);

        // frontend_base_url and email are supplied here, not by the caller's
        // payload - the producer (e.g. identity-api) shouldn't need to know
        // this repo's rendered link target, only the raw token. Email is
        // URL-encoded since it can legally contain '+' (a valid local-part
        // character, common in test/alias addresses), which a query-string
        // parser would otherwise decode back into a space.
        Dictionary<string, string> effectivePayload = new(payload)
        {
            [FrontendBaseUrlField] = frontendOptions.Value.BaseUrl,
            [EmailField] = Uri.EscapeDataString(recipient.Value)
        };

        IReadOnlyList<string> requiredFields = ExtractFieldNames(source);
        List<string> missingFields = requiredFields.Where(field => !effectivePayload.ContainsKey(field)).ToList();

        if (missingFields.Count > 0)
            return Task.FromResult<ErrorOr<string>>(Error.Validation(
                TemplateErrorCodes.MissingField,
                $"Payload is missing required field(s): {string.Join(", ", missingFields)}."));

        Template template = Template.Parse(source);
        if (template.HasErrors)
            return Task.FromResult<ErrorOr<string>>(Error.Failure(
                TemplateErrorCodes.ParseError,
                string.Join("; ", template.Messages)));

        ScriptObject scriptObject = new();
        foreach (KeyValuePair<string, string> field in effectivePayload)
            scriptObject[field.Key] = field.Value;

        TemplateContext context = new();
        context.PushGlobal(scriptObject);

        string rendered = template.Render(context);
        return Task.FromResult<ErrorOr<string>>(rendered);
    }

    private static string ReadResource(string resourceName)
    {
        using Stream stream = ResourceAssembly.GetManifestResourceStream(resourceName)!;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static IReadOnlyList<string> ExtractFieldNames(string source)
    {
        return FieldPlaceholderPattern()
            .Matches(source)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();
    }

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")]
    private static partial Regex FieldPlaceholderPattern();
}
