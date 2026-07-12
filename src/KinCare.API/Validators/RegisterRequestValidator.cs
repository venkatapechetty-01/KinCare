using FluentValidation;
using KinCare.API.Domain;
using KinCare.API.Endpoints;

namespace KinCare.API.Validators;

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.OrganizationName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(100);

        // OrgAdmin must supply facility name + address to create their org's first facility
        RuleFor(x => x.FacilityName).NotEmpty().MaximumLength(200)
            .When(x => x.Role == UserRole.OrgAdmin);
        RuleFor(x => x.FacilityAddress).NotEmpty().MaximumLength(500)
            .When(x => x.Role == UserRole.OrgAdmin);

        // FacilityAdmin must supply the facility name they're joining
        RuleFor(x => x.FacilityName).NotEmpty().MaximumLength(200)
            .When(x => x.Role == UserRole.FacilityAdmin)
            .WithMessage("Facility name is required to join as Facility Admin.");

        RuleFor(x => x.Role)
            .Must(r => r == UserRole.OrgAdmin || r == UserRole.FacilityAdmin)
            .WithMessage("Role must be OrgAdmin or FacilityAdmin.");
    }
}
