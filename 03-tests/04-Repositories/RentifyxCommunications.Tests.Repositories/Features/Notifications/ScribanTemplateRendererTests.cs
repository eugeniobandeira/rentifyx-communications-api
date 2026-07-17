using ErrorOr;
using FluentAssertions;
using RentifyxCommunications.Domain.ValueObjects;
using RentifyxCommunications.Infrastructure.Templates;
using Xunit;

namespace RentifyxCommunications.Tests.Repositories.Features.Notifications;

public sealed class ScribanTemplateRendererTests
{
    private readonly ScribanTemplateRenderer _sut = new();

    [Fact]
    public async Task RenderAsync_WithCompletePayload_ShouldReturnRenderedString()
    {
        TemplateId templateId = TemplateId.Create("welcome-email").Value;
        Dictionary<string, string> payload = new() { ["name"] = "Alice" };

        ErrorOr<string> result = await _sut.RenderAsync(templateId, payload);

        result.IsError.Should().BeFalse();
        result.Value.Should().Contain("Hello Alice");
    }

    [Fact]
    public async Task RenderAsync_WithUnknownTemplate_ShouldReturnNotFoundError()
    {
        TemplateId templateId = TemplateId.Create("does-not-exist").Value;

        ErrorOr<string> result = await _sut.RenderAsync(templateId, new Dictionary<string, string>());

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task RenderAsync_WithMissingPayloadField_ShouldReturnValidationError()
    {
        TemplateId templateId = TemplateId.Create("welcome-email").Value;

        ErrorOr<string> result = await _sut.RenderAsync(templateId, new Dictionary<string, string>());

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Validation);
        result.FirstError.Description.Should().Contain("name");
    }

    [Fact]
    public async Task RenderAsync_EmailVerificationWithToken_ShouldContainVerificationLink()
    {
        TemplateId templateId = TemplateId.Create("email-verification").Value;
        Dictionary<string, string> payload = new() { ["token"] = "abc123" };

        ErrorOr<string> result = await _sut.RenderAsync(templateId, payload);

        result.IsError.Should().BeFalse();
        result.Value.Should().Contain("token=abc123");
    }

    [Fact]
    public async Task RenderAsync_PasswordResetWithToken_ShouldContainResetLink()
    {
        TemplateId templateId = TemplateId.Create("password-reset").Value;
        Dictionary<string, string> payload = new() { ["token"] = "xyz789" };

        ErrorOr<string> result = await _sut.RenderAsync(templateId, payload);

        result.IsError.Should().BeFalse();
        result.Value.Should().Contain("token=xyz789");
    }
}
