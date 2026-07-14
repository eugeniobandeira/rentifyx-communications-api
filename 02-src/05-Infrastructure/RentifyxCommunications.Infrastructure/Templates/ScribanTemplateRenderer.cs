using System.Reflection;
using System.Text.RegularExpressions;
using ErrorOr;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Interfaces.Notifications;
using RentifyxCommunications.Domain.ValueObjects;
using Scriban;
using Scriban.Runtime;

namespace RentifyxCommunications.Infrastructure.Templates;

public sealed partial class ScribanTemplateRenderer : ITemplateRenderer
{
    private static readonly Assembly ResourceAssembly = typeof(ScribanTemplateRenderer).Assembly;

    public Task<ErrorOr<string>> RenderAsync(
        TemplateId templateId,
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

        IReadOnlyList<string> requiredFields = ExtractFieldNames(source);
        List<string> missingFields = requiredFields.Where(field => !payload.ContainsKey(field)).ToList();

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
        foreach (KeyValuePair<string, string> field in payload)
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
