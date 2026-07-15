using FluentAssertions;
using RentifyxCommunications.Domain.Constants;
using Xunit;

namespace RentifyxCommunications.Tests.Domain.Constants;

public sealed class RetryTopicChainTests
{
    [Theory]
    [InlineData(0, RetryTopicChain.Retry5sTopic)]
    [InlineData(1, RetryTopicChain.Retry1mTopic)]
    [InlineData(2, RetryTopicChain.Retry10mTopic)]
    [InlineData(3, RetryTopicChain.DlqTopic)]
    [InlineData(4, RetryTopicChain.DlqTopic)]
    public void NextStage_ShouldReturnCorrectTopic_ForEachRetryCount(int retryCount, string expectedTopic)
    {
        string result = RetryTopicChain.NextStage(retryCount);

        result.Should().Be(expectedTopic);
    }

    [Theory]
    [InlineData(RetryTopicChain.Retry5sTopic, 5)]
    [InlineData(RetryTopicChain.Retry1mTopic, 60)]
    [InlineData(RetryTopicChain.Retry10mTopic, 600)]
    public void DelayFor_ShouldReturnCorrectDelay_ForEachRetryTopic(string topic, int expectedSeconds)
    {
        TimeSpan result = RetryTopicChain.DelayFor(topic);

        result.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Fact]
    public void DelayFor_ShouldThrow_ForATopicWithNoConfiguredDelay()
    {
        Action act = () => RetryTopicChain.DelayFor(RetryTopicChain.DlqTopic);

        act.Should().Throw<ArgumentException>();
    }
}
