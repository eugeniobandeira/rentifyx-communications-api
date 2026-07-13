namespace RentifyxCommunications.Domain.ValueObjects;

public sealed class ConsentDecision
{
    public bool IsSuppressed { get; }

    private ConsentDecision(bool isSuppressed)
    {
        IsSuppressed = isSuppressed;
    }

    public static ConsentDecision NoRecordFound()
    {
        return new ConsentDecision(isSuppressed: false);
    }

    public static ConsentDecision FromPreference(ConsentPreference preference)
    {
        return new ConsentDecision(isSuppressed: !preference.OptedIn);
    }
}
