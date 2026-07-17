using System.Text.Json;
using ErrorOr;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.Enums;

namespace RentifyxCommunications.Application.Features.Notifications.Handlers.Dispatch.Common;

/// <summary>
/// Maps a dispatch failure to <see cref="FailureClassification"/>, per the F-09 spec's
/// classification table. Unmatched error codes default to <see cref="FailureClassification.PoisonPill"/>
/// (fail closed) so an unclassified failure never loops through retry indefinitely.
/// </summary>
public static class FailureClassifier
{
    private static readonly HashSet<string> TransientCodes =
    [
        SesErrorCodes.SendFailed,
        ResilienceErrorCodes.RateLimitExceeded,
        ResilienceErrorCodes.CircuitOpen
    ];

    public static FailureClassification Classify(IReadOnlyList<Error> errors)
    {
        if (errors.Count == 0)
            return FailureClassification.PoisonPill;

        return TransientCodes.Contains(errors[0].Code)
            ? FailureClassification.Transient
            : FailureClassification.PoisonPill;
    }

    public static FailureClassification Classify(Exception exception) =>
        exception is JsonException ? FailureClassification.PoisonPill : FailureClassification.Transient;
}
