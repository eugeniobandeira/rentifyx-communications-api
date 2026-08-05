namespace RentifyxCommunications.Domain.Constants;

public static class DispatchValidationErrorCodes
{
    public const string IdempotencyKeyRequired = "Dispatch.IdempotencyKeyRequired";
    public const string RecipientIdRequired = "Dispatch.RecipientIdRequired";
    public const string RecipientEmailRequired = "Dispatch.RecipientEmailRequired";
    public const string TemplateIdRequired = "Dispatch.TemplateIdRequired";
    public const string InvalidChannel = "Dispatch.InvalidChannel";
    public const string PayloadRequired = "Dispatch.PayloadRequired";
}
