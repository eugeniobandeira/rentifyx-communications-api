using ErrorOr;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;

namespace RentifyxCommunications.Domain.ValueObjects;

public sealed class ConsentPreference
{
    public Guid RecipientId { get; }
    public Channel Channel { get; }
    public bool OptedIn { get; }
    public DateTime UpdatedAt { get; }

    private ConsentPreference(Guid recipientId, Channel channel, bool optedIn, DateTime updatedAt)
    {
        RecipientId = recipientId;
        Channel = channel;
        OptedIn = optedIn;
        UpdatedAt = updatedAt;
    }

    public static ErrorOr<ConsentPreference> Create(Guid recipientId, Channel channel, bool optedIn, DateTime updatedAt)
    {
        if (recipientId == Guid.Empty)
            return Error.Validation(NotificationErrorCodes.InvalidRecipientId, "Recipient id must not be empty.");

        return new ConsentPreference(recipientId, channel, optedIn, updatedAt);
    }
}
