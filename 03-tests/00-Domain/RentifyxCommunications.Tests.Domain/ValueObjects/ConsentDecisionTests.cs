using FluentAssertions;
using RentifyxCommunications.Domain.Enums;
using RentifyxCommunications.Domain.ValueObjects;
using Xunit;

namespace RentifyxCommunications.Tests.Domain.ValueObjects;

public sealed class ConsentDecisionTests
{
    [Fact]
    public void NoRecordFound_ShouldNotBeSuppressed()
    {
        ConsentDecision.NoRecordFound().IsSuppressed.Should().BeFalse();
    }

    [Fact]
    public void FromPreference_WhenOptedIn_ShouldNotBeSuppressed()
    {
        ConsentPreference preference = ConsentPreference.Create(Guid.NewGuid(), Channel.Email, optedIn: true, DateTime.UtcNow).Value;

        ConsentDecision.FromPreference(preference).IsSuppressed.Should().BeFalse();
    }

    [Fact]
    public void FromPreference_WhenOptedOut_ShouldBeSuppressed()
    {
        ConsentPreference preference = ConsentPreference.Create(Guid.NewGuid(), Channel.Email, optedIn: false, DateTime.UtcNow).Value;

        ConsentDecision.FromPreference(preference).IsSuppressed.Should().BeTrue();
    }
}
