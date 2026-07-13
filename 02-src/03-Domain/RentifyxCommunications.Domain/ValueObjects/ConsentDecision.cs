namespace RentifyxCommunications.Domain.ValueObjects;

public sealed class ConsentDecision
{
    public bool IsSuppressed { get; }

    private ConsentDecision(bool isSuppressed)
    {
        IsSuppressed = isSuppressed;
    }

    public static ConsentDecision NoRecordFound() => new(isSuppressed: false);

    public static ConsentDecision FromPreference(ConsentPreference preference) => new(isSuppressed: !preference.OptedIn);
}
