using System.Diagnostics;

namespace RentifyxCommunications.Api.Messaging;

internal static class MessagingActivitySource
{
    internal const string Name = "RentifyxCommunications.Messaging";

    internal static readonly ActivitySource Instance = new(Name);
}
