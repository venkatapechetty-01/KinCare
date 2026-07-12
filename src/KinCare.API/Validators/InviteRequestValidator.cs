using FluentValidation;
using KinCare.API.Domain;
using KinCare.API.Endpoints;

namespace KinCare.API.Validators;

public class InviteRequestValidator : AbstractValidator<InviteRequest>
{
    public InviteRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Role).Must(r => r == UserRole.FacilityAdmin || r == UserRole.OrgAdmin)
            .WithMessage("Role must be FacilityAdmin or OrgAdmin.");
        RuleFor(x => x.FacilityId).NotNull()
            .When(x => x.Role == UserRole.FacilityAdmin)
            .WithMessage("FacilityId is required for FacilityAdmin role.");
    }
}
