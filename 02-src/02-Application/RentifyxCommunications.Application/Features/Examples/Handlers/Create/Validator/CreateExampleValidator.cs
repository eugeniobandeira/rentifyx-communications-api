using FluentValidation;
using RentifyxCommunications.Application.Features.Examples.Handlers.Create.Request;
using RentifyxCommunications.Domain.Constants;
using RentifyxCommunications.Domain.MessageResource;

namespace RentifyxCommunications.Application.Features.Examples.Handlers.Create.Validator;

public sealed class CreateExampleValidator : AbstractValidator<CreateExampleRequest>
{
    public CreateExampleValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
                .WithMessage(ValidationMessageResource.NAME_REQUIRED)
            .MaximumLength(ValidationConstants.ExampleRules.NameMaxLength)
                .WithMessage(ValidationMessageResource.NAME_MAX_LENGTH);

        RuleFor(x => x.Description)
            .NotEmpty()
                .WithMessage(ValidationMessageResource.DESCRIPTION_REQUIRED)
            .MaximumLength(ValidationConstants.ExampleRules.DescriptionMaxLength)
                .WithMessage(ValidationMessageResource.DESCRIPTION_MAX_LENGTH);
    }
}
