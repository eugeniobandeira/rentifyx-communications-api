using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace RentifyxCommunications.Api.Messaging;

/// <summary>
/// Extracts a W3C <c>traceparent</c>/<c>tracestate</c> pair from Kafka message headers so consumers
/// can start a child <see cref="Activity"/> linked to the producer's trace.
/// </summary>
internal static class TraceContextHeaders
{
    internal const string TraceParentHeader = "traceparent";
    internal const string TraceStateHeader = "tracestate";

    /// <summary>
    /// Reads <c>traceparent</c>/<c>tracestate</c> from <paramref name="headers"/> and parses them into an
    /// <see cref="ActivityContext"/>. Returns <c>default(ActivityContext)</c> (a new root trace) when the
    /// <c>traceparent</c> header is missing, or when it is present but malformed - in the malformed case a
    /// warning is logged via <paramref name="logger"/>.
    /// </summary>
    internal static ActivityContext Extract(Headers? headers, ILogger logger)
    {
        string? traceParent = ReadStringHeader(headers, TraceParentHeader);
        if (traceParent is null)
            return default;

        string? traceState = ReadStringHeader(headers, TraceStateHeader);

        if (ActivityContext.TryParse(traceParent, traceState, isRemote: true, out ActivityContext parentContext))
            return parentContext;

        logger.LogWarning("Malformed traceparent header value: {TraceParent}", traceParent);
        return default;
    }

    private static string? ReadStringHeader(Headers? headers, string key) =>
        headers is not null && headers.TryGetLastBytes(key, out byte[] bytes) ? Encoding.UTF8.GetString(bytes) : null;
}
