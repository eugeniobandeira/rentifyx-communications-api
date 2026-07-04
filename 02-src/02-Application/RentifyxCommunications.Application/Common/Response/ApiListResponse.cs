namespace RentifyxCommunications.Application.Common.Response;

public sealed record ApiListResponse<T>(
    IReadOnlyCollection<T> Data,
    int TotalCount,
    int Page,
    int PageSize)
{ }
